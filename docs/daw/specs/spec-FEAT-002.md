# Spec FEAT-002: Aprobación y rechazo de solicitudes por el manager

| Field | Value |
|-------|-------|
| Ticket | FEAT-002 |
| PRD | `docs/daw/prd/prd-FEAT-002.md` |
| Tier | FEATURE |
| Date | 2026-08-07 |
| Spec loops | 0 |

## Summary

`Solicitud` gana tres columnas para registrar su resolución, respaldadas por una quinta check
constraint. `PermisosService` —hoy sin acceso a datos— gana una fábrica de contextos y separa
explícitamente dos preguntas que el PRD trata como relacionadas pero no son la misma: **quién puede
ver** una solicitud ajena (el titular, su manager, el designado de su manager) y **quién puede
resolverla** (manager o designado únicamente, nunca el titular — la autoaprobación no está permitida).
`SolicitudesService.ResolverAsync` aprueba o rechaza dentro de una transacción serializable con el
mismo reintento dirigido que FEAT-001c ya usa para su propia carrera de concurrencia. El listado de
pendientes del equipo y la pantalla nueva completan el flujo; un link en el menú, visible solo para
quien tiene autoridad sobre algún equipo, lo hace alcanzable.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 (manager/designado aprueba una Pendiente) | Block 3 |
| FR-02 (manager/designado rechaza con motivo obligatorio) | Block 3 |
| FR-03 (impedir resolver una que no está Pendiente) | Block 3 |
| FR-04 (registrar quién, cuándo, y el motivo) | Block 1 (esquema) + Block 3 (escritura) |
| FR-05 (listado de pendientes del equipo) | Block 4 |
| FR-06 (restringir visualización y resolución) | Block 2 |
| NFR-01 (cobertura ≥ 80%) | Strategy: gate `daw-test`; el proyecto viene de 95%+ y no baja |
| NFR-02 (p95 < 3s con 50 concurrentes en aprobar/rechazar) | Strategy: mismo argumento estructural que NFR-04 de PRD-001 ya usado en FEAT-001b/c — la medición queda diferida a un ticket de performance propio |
| NFR-03 (índices para el listado del equipo) | Strategy: reutiliza los índices `IX_Empleados_ManagerId`/`IX_Empleados_DesignadoId` que EF Core ya creó automáticamente para las FK autorreferenciadas de `Empleado` en la migración inicial — **no se agrega ningún índice nuevo** |

**Nota de trazabilidad sobre FR-06.** El PRD lo enuncia como una cota conjunta ("restringir tanto la
visualización como la resolución... únicamente a ese empleado, a su manager y al designado de su
manager"), pero FR-01/FR-02 —más específicos— acotan la resolución a manager/designado solamente, sin
el propio empleado. Esta spec lee FR-06 como el límite exterior (nadie fuera de esas tres personas
toca el dato, ni para ver ni para resolver) y FR-01/FR-02 como una restricción más estricta dentro de
ese límite, específica para resolver. No hay contradicción: toda resolución válida según FR-01/FR-02
también es válida según FR-06, nunca al revés. Decisión confirmada con el usuario en PLAN.

**AC → tests** está en el bloque de cada uno; el mapa completo se recompone en *Final verification*.

## Dependencies between blocks

```
Block 1  (esquema: columnas + constraint)
   └─> Block 2  (PermisosService: organigrama)
          ├─> Block 3  (ResolverAsync)
          └─> Block 4  (listado de pendientes del equipo)
                 └─> Block 5  (UI: pantalla + link + "quién resolvió" en Mis Solicitudes)
```

Orden de ejecución: **1 → 2 → 3 → 4 → 5**. El Bloque 5 depende de 3 y 4 a la vez (necesita poder
resolver y poder listar); se ordena al final porque es el único que no tiene lógica de dominio propia.

---

## Block 1 — Esquema: columnas de resolución

**Files**
- `src/GestionVacaciones.Data/Entidades/Solicitud.cs` (modificado) — tres propiedades nuevas.
- `src/GestionVacaciones.Data/VacacionesDbContext.cs` (modificado) — la FK `ResueltoPorId → Empleado`
  (`DeleteBehavior.NoAction`, mismo criterio que las tres FK existentes hacia `Empleado`) y la check
  constraint nueva.
- `src/GestionVacaciones.Data/Migrations/` (nuevo) — migración generada con `dotnet ef migrations add`.
- `tests/GestionVacaciones.Tests/Persistencia/EsquemaDeSolicitudTests.cs` (modificado) — los 3 tests
  que hoy afirman "exactamente 4 check constraints" pasan a 5; tests nuevos para la constraint y para
  la FK.

**Data model**

`Solicitud` gana:
- `ResueltoPorId` (`int?`, FK a `Empleado.Id`, `DeleteBehavior.NoAction`) — quién resolvió.
- `FechaResolucion` (`DateTimeOffset?`) — cuándo, con el mismo desplazamiento local que
  `FechaCreacion`.
- `MotivoDeRechazo` (`string?`) — solo cuando el resultado fue un rechazo.

**Check constraint nueva**, `CK_Solicitud_ResolucionCoherente`, siguiendo la forma booleana de las
4 existentes (disyunción de conjunciones, sin `CASE`):
```
(Estado = 0 AND ResueltoPorId IS NULL AND FechaResolucion IS NULL AND MotivoDeRechazo IS NULL)
OR (Estado = 1 AND ResueltoPorId IS NOT NULL AND FechaResolucion IS NOT NULL AND MotivoDeRechazo IS NULL)
OR (Estado = 2 AND ResueltoPorId IS NOT NULL AND FechaResolucion IS NOT NULL AND MotivoDeRechazo IS NOT NULL)
```
Los literales 0/1/2 son los mismos que ya usa `CK_Solicitud_EstadoValido` para
`Pendiente`/`Aprobada`/`Rechazada`.

**Sin índices nuevos.** `IX_Empleados_ManagerId` e `IX_Empleados_DesignadoId` ya existen —EF Core los
creó automáticamente para las FK autorreferenciadas de `Empleado` en `20260802014814_InicialV2`— y
sirven a NFR-03 tal cual están. Este bloque no toca `HasIndex` de `Empleado`.

**Error handling**
- Migración fallida al aplicarse → no se da por buena: revertir (`dotnet ef migrations remove` o
  `database update <anterior>`), corregir, reaplicar (`AGENTS.md`).
- La constraint es defensa en profundidad: la regla de negocio vive en `SolicitudesService`
  (Block 3); si una fila llega a violarla es que la regla de negocio falló, no al revés.

**Required tests**
- [ ] `La_check_constraint_de_resolucion_exige_coherencia_con_el_estado` — contra la instancia real:
  insertar directamente por SQL una fila `Pendiente` con `ResueltoPorId` no nulo falla; una
  `Aprobada` sin `ResueltoPorId`/`FechaResolucion` falla; una `Rechazada` sin `MotivoDeRechazo` falla.
- [ ] `Las_check_constraints_siguen_siendo_las_mismas_cinco` (reemplaza a
  `Las_cuatro_...`) — la lista completa, con el nombre nuevo incluido.
- [ ] `La_migracion_revierte_dejando_el_esquema_anterior` (modificado) — aplicada y revertida, la
  constraint nueva y las columnas desaparecen, las 4 anteriores siguen.
- [ ] `La_fk_de_resueltoPorId_no_hace_cascada` — camino triste: intentar borrar un `Empleado` que
  resolvió alguna solicitud falla por `NoAction`, igual que ya pasa con `EmpleadoId`.

**Completion criterion**
Los 4 tests pasan; la migración aplica y revierte limpiamente; los tests que antes afirmaban "4
constraints" quedan actualizados a 5 y no hay ningún otro archivo de test con el conteo viejo.

---

## Block 2 — `PermisosService` gana organigrama

**Files**
- `src/GestionVacaciones.Data/Services/PermisosService.cs` (modificado) — constructor sobrecargado,
  dos métodos async nuevos.
- `tests/GestionVacaciones.Tests/Dominio/PermisosDeVisibilidadTests.cs` (modificado) — casos nuevos
  para manager/designado.
- `tests/GestionVacaciones.Tests/Dominio/AutorizacionDeResolucionTests.cs` (nuevo).

**Logic**

Constructor **sobrecargado**, no reemplazado: el de 0 parámetros sigue existiendo tal cual (21 call
sites de test dependen de él) y sigue respaldando únicamente los métodos síncronos existentes. El
constructor nuevo recibe `IDbContextFactory<VacacionesDbContext>`.

- `public async Task<bool> PuedeVerLasSolicitudesDeAsync(IdentidadDelEmpleado quienConsulta, int empleadoDeLasSolicitudes, CancellationToken)`
  — `true` si es la propia persona (delega en el método síncrono existente, sin duplicar la
  comparación) **o** si `quienConsulta` es el manager de `empleadoDeLasSolicitudes`, **o** si
  `quienConsulta` es el designado del manager de `empleadoDeLasSolicitudes`.
- `public async Task<bool> PuedeResolverLasSolicitudesDeAsync(IdentidadDelEmpleado quienConsulta, int empleadoDeLasSolicitudes, CancellationToken)`
  — **sin el caso `self`**: solo `true` si es el manager, o el designado del manager. Un empleado
  nunca puede resolver su propia solicitud, aunque `PuedeVerLasSolicitudesDeAsync` para el mismo par
  devuelva `true`. Mitigación de **R-22**: el `self` se excluye como condición propia del método, no
  como ausencia accidental de una condición — así que ni un dato externo anómalo
  (`Empleado.ManagerId == Empleado.Id`) alcanzaría a autorizar autoaprobación.
- `public async Task<IReadOnlyList<int>> EmpleadosBajoAutoridadDeAsync(IdentidadDelEmpleado quienConsulta, CancellationToken)`
  — los `EmpleadoId` sobre los que `quienConsulta` es manager o designado del manager. Lista vacía si
  no tiene ninguno (no es un error: es la respuesta de alguien sin equipo a cargo).
- Los tres métodos exigen las versiones `Exigir*Async` correspondientes, mismo patrón que ya
  establece `ExigirPoderVerLasSolicitudesDe`: lanzan `AccesoASolicitudesDenegadoException` en vez de
  devolver `false`, para que la negación no se pueda ignorar por descuido.

**La consulta** proyecta únicamente `Id`, `ManagerId`, `DesignadoId` de `Empleado` — nunca
`Nombre`/`Correo` (PII) para esta decisión, mitigación de la parte de Information Disclosure de
R-22/C15 del modelo de amenazas. Cada método abre y cierra su propio contexto vía
`IDbContextFactory` (NFR-05), sin `catch`.

**Input validation**

Los mismos `ArgumentNullException.ThrowIfNull` que ya usan los métodos síncronos, sobre
`quienConsulta`.

**Error handling**
- Fallo de persistencia dentro de cualquiera de los tres métodos → se propaga, sin `catch`.
- `SinEmpleadoSeleccionadoException` si `quienConsulta` no tiene empleado seleccionado — mismo
  criterio que el método síncrono existente, no se degrada a `false`.

**Required tests**
- [ ] `El_manager_puede_ver_las_solicitudes_de_su_equipo` — `PuedeVerLasSolicitudesDeAsync`.
- [ ] `El_designado_puede_ver_las_solicitudes_del_equipo_de_su_manager`.
- [ ] `Un_empleado_sin_relacion_no_puede_ver_las_solicitudes_de_otro` — camino triste, valida FR-06.
- [ ] `El_manager_puede_resolver_las_solicitudes_de_su_equipo` — `PuedeResolverLasSolicitudesDeAsync`.
- [ ] `El_designado_puede_resolver_las_solicitudes_del_equipo_de_su_manager`.
- [ ] `Un_empleado_no_puede_resolver_su_propia_solicitud` — camino triste, aunque
  `PuedeVerLasSolicitudesDeAsync` para el mismo par devuelva `true`. Mitigación de R-22.
- [ ] `Un_empleado_sin_relacion_no_puede_resolver_la_solicitud_de_otro` — camino triste, valida FR-06.
- [ ] `EmpleadosBajoAutoridadDeAsync_devuelve_vacio_para_quien_no_tiene_equipo` — camino triste.
- [ ] `EmpleadosBajoAutoridadDeAsync_incluye_al_equipo_del_manager_y_al_del_designado`.
- [ ] `Las_consultas_de_organigrama_no_traen_nombre_ni_correo` — verifica por reflexión/SQL capturado
  que la proyección no incluye columnas de PII. Mitigación de R-22.
- [ ] `Un_fallo_de_persistencia_al_consultar_el_organigrama_se_propaga` — camino triste, con
  `FabricaQueNadieDebeUsar` o un interceptor que fuerza el fallo: los tres métodos nuevos no
  degradan a `false`/lista vacía ante un error real de la base.
- [ ] `Sin_identidad_los_metodos_de_organigrama_no_se_degradan` — camino triste: sin empleado
  seleccionado, se propaga `SinEmpleadoSeleccionadoException` en los tres métodos nuevos, mismo
  criterio que ya exige el método síncrono existente.

**Completion criterion**
Los 12 tests pasan; los ~21 call sites de test que hoy construyen `new PermisosService()` sin
parámetros siguen compilando sin cambios; `SaldoDelAnioTests`, `TopeAnualEnElAltaTests`,
`SuperposicionDePeriodosTests` y el resto de los consumidores existentes de los métodos síncronos
siguen verdes sin tocarlos.

---

## Block 3 — `SolicitudesService.ResolverAsync`

**Files**
- `src/GestionVacaciones.Data/Services/SolicitudesService.cs` (modificado) — nuevo método
  `ResolverAsync`, `SolicitudDelListado` gana dos campos.
- `src/GestionVacaciones.Data/Services/ErroresDeSolicitud.cs` (modificado) — dos literales nuevos.
- `tests/GestionVacaciones.Tests/Dominio/ResolucionDeSolicitudTests.cs` (nuevo).
- `tests/GestionVacaciones.Tests/Dominio/MensajesLiteralesDeFeat002Tests.cs` (nuevo) — hermano de
  `MensajesLiteralesDeFeat001bTests.cs`/`...Feat001cTests.cs`.
- `tests/GestionVacaciones.Tests/Identidad/DiagnosticoSinPiiTests.cs` (modificado) — actualiza el
  constructor posicional de `SolicitudDelListado` (gap del impact-scan).
- `tests/GestionVacaciones.Tests/Componentes/ListadoDeSolicitudesTests.cs` (modificado) — ídem.

**Logic**

```
public async Task<ResultadoDeLaResolucion> ResolverAsync(
    int solicitudId, bool aprobar, string? motivo, CancellationToken)
```

Quien resuelve sale de `IEmpleadoActualProvider.Identidad`, nunca de un parámetro (mismo patrón que
`CrearAsync`, mitigación de R-13).

1. Si `aprobar == false` y `motivo` está vacío/blanco → `Rechazada(ErroresDeSolicitud.MotivoDeRechazoObligatorio)`,
   sin abrir contexto. Valida AC-03.
2. Abre `contexto` + transacción `Serializable` (mismo patrón que `CrearAsync`).
3. Lee la solicitud por `Id`. Si no existe → `ArgumentException` (caso no cubierto por ningún AC:
   es un id inexistente, no un estado de negocio — se trata como precondición incumplida del
   llamador, misma familia que el período invertido de `ImputacionPorAnio`).
4. `_permisos.ExigirPoderResolverLasSolicitudesDeAsync(quienActua, solicitud.EmpleadoId, cancelacion)`
   — la única sede de la decisión (Block 2). Si niega, la excepción se propaga (no se convierte en
   `Rechazada`: es un 403, no una validación de negocio).
5. Si `solicitud.Estado != Pendiente` → rollback, `Rechazada(ErroresDeSolicitud.SolicitudYaResuelta)`.
   Valida AC-04.
6. Si pasa todo: `solicitud.Estado = Aprobada|Rechazada`, `ResueltoPorId = quienActua.Id`,
   `FechaResolucion = _tiempo.GetLocalNow()`, `MotivoDeRechazo = aprobar ? null : motivo`. Guardar y
   commitear.

**El reintento dirigido (R-25, mismo mecanismo que C14 de FEAT-001c).** El tramo completo de la
transacción queda envuelto en la misma captura de `SqlException.Number == 1205` que
`CrearAsync` ya usa. Al capturarlo: nueva transacción, se relee el `Estado` actual de la solicitud;
si ya no es `Pendiente` → `Rechazada(SolicitudYaResuelta)` (alguien ganó la carrera); si sigue
`Pendiente` → se repropaga la excepción original (no es una carrera de resolución, es otra falla de
motor).

`SolicitudDelListado` gana `int? ResueltoPorId` y `string? MotivoDeRechazo` (más `FechaResolucion` si
la pantalla lo necesita — a confirmar en el Block 5, no bloquea este bloque). Valida AC-05/AC-06.

**Los literales nuevos**, en `ErroresDeSolicitud`:
```
MotivoDeRechazoObligatorio = "Indicá el motivo del rechazo"
SolicitudYaResuelta        = "Esta solicitud ya fue resuelta"
```

**Input validation**
- `motivo`: no vacío/blanco cuando `aprobar == false`. Sin cota de longitud explícita en el PRD; se
  fija `nvarchar(1000)` en la migración del Block 1, generoso para texto libre sin ser ilimitado.

**Error handling**
- Motivo vacío en un rechazo → `Rechazada`, no excepción (AC-03).
- Solicitud ya resuelta → `Rechazada`, no excepción (AC-04), por los dos caminos (directo y
  reintento).
- Sin autorización → se propaga la excepción de `PermisosService`, no se degrada a `Rechazada`
  (distinto tipo de fallo: acceso, no validación).
- Conflicto de serialización que no es una carrera de resolución → se propaga sin convertir (R-16).
- Fallo de persistencia → rollback y se propaga.

**Required tests**
- [ ] `Un_manager_aprueba_una_solicitud_pendiente_de_su_equipo` — camino feliz, valida AC-01.
- [ ] `Un_designado_aprueba_una_solicitud_pendiente_del_equipo_de_su_manager`.
- [ ] `Un_manager_rechaza_una_solicitud_pendiente_indicando_un_motivo` — valida AC-02.
- [ ] `Rechazar_sin_motivo_se_impide_con_el_mensaje_de_AC_03` — camino triste.
- [ ] `Resolver_una_solicitud_ya_aprobada_se_impide_con_el_mensaje_de_AC_04` — camino triste.
- [ ] `Resolver_una_solicitud_ya_rechazada_se_impide_con_el_mensaje_de_AC_04` — camino triste.
- [ ] `Un_empleado_sin_relacion_no_puede_resolver_y_la_excepcion_se_propaga` — camino triste, valida
  AC-08 en el camino de escritura.
- [ ] `La_resolucion_registra_quien_y_cuando` — valida AC-05.
- [ ] `El_empleado_ve_quien_resolvio_y_el_motivo_en_su_listado` — valida AC-06, vía
  `ListarPropiasAsync`.
- [ ] `Dos_resoluciones_concurrentes_de_la_misma_solicitud_una_gana_y_la_otra_ve_ya_resuelta` — contra
  la instancia real, `Task.WhenAll`. Valida el mecanismo de R-25.
- [ ] `Un_conflicto_de_serializacion_que_no_es_una_carrera_de_resolucion_se_propaga` — inyección de
  excepción, mismo patrón que FEAT-001c.
- [ ] `El_motivo_se_muestra_como_texto_plano_sin_interpretarse` — mitigación de R-24, con un
  centinela (`<script>`) que debe aparecer literal, no ejecutado.
- [ ] `El_literal_del_motivo_obligatorio_coincide_con_prd_FEAT_002` (en
  `MensajesLiteralesDeFeat002Tests.cs`).
- [ ] `El_literal_de_solicitud_ya_resuelta_coincide_con_prd_FEAT_002` (ídem).
- [ ] `Un_fallo_al_guardar_la_resolucion_revierte_la_transaccion_y_se_propaga` — camino triste,
  mismo patrón que el equivalente de `CrearAsync` en FEAT-001b: tras la excepción, la base no
  conserva la resolución a medio aplicar.

**Completion criterion**
Los 15 tests pasan; `AltaDeSolicitudTests`, `TopeAnualEnElAltaTests` y
`SuperposicionDePeriodosTests` siguen verdes sin tocarlos; borrar la llamada a
`ExigirPoderResolverLasSolicitudesDeAsync` deja `Un_empleado_sin_relacion_no_puede_resolver...` en
rojo.

---

## Block 4 — Listado de pendientes del equipo

**Files**
- `src/GestionVacaciones.Data/Services/SolicitudesService.cs` (modificado) — nuevo método
  `ListarPendientesDelEquipoAsync` y su record de proyección.
- `tests/GestionVacaciones.Tests/Dominio/ListadoDelEquipoTests.cs` (nuevo).

**Logic**

```
public sealed record SolicitudPendienteDelEquipo(
    int Id, int EmpleadoId, string NombreDelEmpleado,
    DateOnly FechaInicio, DateOnly FechaFin, int DiasCorridos, DateTimeOffset FechaCreacion);

public async Task<IReadOnlyList<SolicitudPendienteDelEquipo>> ListarPendientesDelEquipoAsync(CancellationToken)
```

1. `var equipo = await _permisos.EmpleadosBajoAutoridadDeAsync(quienConsulta, cancelacion)` (Block 2).
2. Si `equipo` está vacío → `[]` sin consultar `Solicitud` (mismo criterio que
   `SaldoService.DeLosAniosAsync` con lista vacía: cada viaje a la base corresponde a una pregunta
   real).
3. `contexto.Solicitudes.Where(s => equipo.Contains(s.EmpleadoId) && s.Estado == Pendiente)`, unido a
   `Empleado` solo para `Nombre` (join explícito, no `.Include()`), proyectado directo a
   `SolicitudPendienteDelEquipo`. Mitigación de la parte de Information Disclosure de R-22.
4. Orden: por `FechaCreacion` ascendente (la más antigua primero — es la que más tiempo lleva
   esperando), a diferencia de "Mis Solicitudes" que ordena descendente.

**`NombreDelEmpleado` es la única vez que este campo de `Empleado` viaja fuera de la propia persona**
en toda la aplicación — necesario para que el listado sea utilizable (FR-05), y acotado
exclusivamente a quien ya tiene autoridad confirmada sobre ese empleado (Block 2 ya lo garantizó
antes de este punto).

**Input validation**

Sin input del usuario más allá de la identidad (que sale de `IEmpleadoActualProvider`).

**Error handling**
- Sin empleado seleccionado → se propaga desde el proveedor (mismo criterio que el resto del
  dominio).
- Fallo de persistencia → se propaga, sin `catch`.

**Required tests**
- [ ] `El_manager_ve_las_pendientes_de_su_equipo` — valida AC-07.
- [ ] `El_designado_ve_las_pendientes_del_equipo_de_su_manager`.
- [ ] `El_listado_no_incluye_aprobadas_ni_rechazadas` — camino triste, refuerza AC-07.
- [ ] `Quien_no_tiene_equipo_recibe_una_lista_vacia_sin_consultar_solicitudes` — con
  `FabricaQueNadieDebeUsar` sobre el paso 3, montado de forma que si se consulta `Solicitud` el test
  revienta.
- [ ] `El_listado_no_muestra_solicitudes_de_otro_equipo` — camino triste, valida FR-06 en este
  camino de lectura.
- [ ] `El_orden_es_por_fecha_de_creacion_ascendente` — la más antigua primero.
- [ ] `Un_fallo_de_persistencia_al_listar_el_equipo_se_propaga` — camino triste: el listado no
  degrada a una lista vacía ante un error real de la base (mismo criterio que AC-07 de FEAT-001b
  aplica a `SaldoService`).
- [ ] `Sin_identidad_el_listado_no_se_degrada_a_vacio` — camino triste: sin empleado seleccionado,
  se propaga la excepción del proveedor, no una lista vacía que se leería como "sin pendientes".

**Completion criterion**
Los 8 tests pasan; el listado nunca abre contexto cuando `EmpleadosBajoAutoridadDeAsync` devuelve
vacío.

---

## Block 5 — UI: pantalla de autorizaciones

**Files**
- `src/GestionVacaciones.Web/Components/Pages/Autorizaciones.razor` (nuevo) — `@page "/autorizaciones"`.
- `src/GestionVacaciones.Web/Components/Layout/MainLayout.razor` (modificado) — link condicional.
- `src/GestionVacaciones.Web/Components/Pages/MisSolicitudes.razor` (modificado) — muestra quién
  resolvió y el motivo en las solicitudes ya resueltas.
- `src/GestionVacaciones.Data/Services/SolicitudesService.cs` (modificado) — método
  `TieneEquipoACargoAsync` para la visibilidad del link (delegado en `PermisosService`
  internamente, o expuesto directo desde ahí — a decidir en la implementación sin cambiar el
  contrato: la fuente es siempre `PermisosService.EmpleadosBajoAutoridadDeAsync`, `Count > 0`).
- `tests/GestionVacaciones.Tests/Componentes/AutorizacionesTests.cs` (nuevo).
- `tests/GestionVacaciones.Tests/Componentes/MisSolicitudesTests.cs` (modificado).
- `tests/GestionVacaciones.Tests/Componentes/ComposicionDeLaPantalla.cs` (modificado) — registro de
  cualquier servicio nuevo, si el bloque termina agregando uno.

**Logic**

`Autorizaciones.razor`:
- Llama a `SolicitudesService.ListarPendientesDelEquipoAsync` (Block 4) al entrar.
- Cada fila: nombre del empleado, período, días corridos, y dos acciones: **Aprobar** (sin
  confirmación adicional) y **Rechazar** (abre un campo de motivo obligatorio antes de confirmar).
- Al resolver, llama a `SolicitudesService.ResolverAsync` (Block 3) y recarga el listado.
- Estados distinguibles: cargando, sin pendientes (lista vacía, no es un error), lista con filas,
  error. Mismo patrón de `data-estado`/`data-testid` que `MisSolicitudes.razor`.

`MainLayout.razor`:
- El link a `/autorizaciones` solo se renderiza si `TieneEquipoACargoAsync` (o el método que exponga
  esa pregunta) devuelve `true`. La decisión sale del dominio, nunca de una consulta propia del
  componente — mitigación explícita del hallazgo del arch-auditor en PLAN, reforzada por
  `ComponentesSinAccesoADatosTests`.

`MisSolicitudes.razor`:
- Cuando una solicitud está `Aprobada` o `Rechazada`, muestra quién la resolvió y, si fue rechazo,
  el motivo — como texto (`@texto`), nunca `MarkupString` (R-24).

**Input validation**

El campo de motivo en `Autorizaciones.razor` no permite confirmar el rechazo si está vacío
(comodidad del cliente); el rechazo real de AC-03 lo decide el servidor, no este chequeo (R-10, sin
excepción para este bloque).

**Error handling**
- Fallo al resolver (por ejemplo, otra persona ya la resolvió) → se muestra el mensaje literal que
  devuelve `ResolverAsync`, mismo patrón que `FormularioDeAlta.razor` con `ResultadoDelAlta`.
- Fallo al cargar el listado → estado de error distinguible, sin ocultar el resto de la pantalla.

**Required tests**
- [ ] `El_manager_ve_el_listado_de_pendientes_de_su_equipo`.
- [ ] `Aprobar_una_solicitud_la_saca_del_listado`.
- [ ] `Rechazar_sin_motivo_no_permite_confirmar` — camino triste del lado del cliente.
- [ ] `Rechazar_con_motivo_la_saca_del_listado_con_el_motivo_guardado`.
- [ ] `Sin_solicitudes_pendientes_se_ve_un_estado_vacio_no_un_error`.
- [ ] `El_link_de_autorizaciones_no_aparece_para_quien_no_tiene_equipo` — valida la visibilidad
  condicional.
- [ ] `El_link_de_autorizaciones_aparece_para_un_manager`.
- [ ] `Mis_solicitudes_muestra_quien_resolvio_y_el_motivo_del_rechazo`.
- [ ] `Ningun_razor_nombra_TimeProvider` — extiende `ComponentesSinAccesoADatosTests` a los archivos
  nuevos.
- [ ] `Si_resolver_falla_se_muestra_el_mensaje_sin_romper_la_pantalla` — camino triste: el error de
  `ResolverAsync` (por ejemplo, "ya fue resuelta" por una carrera) se muestra sin tumbar el resto de
  la pantalla de autorizaciones.
- [ ] `Si_el_listado_falla_se_ve_un_estado_de_error_distinguible` — camino triste, mismo criterio
  que el cuarto estado de `SaldoDelEmpleado.razor` en FEAT-001b: nunca una lista vacía disfrazada de
  "sin pendientes".

**Completion criterion**
Los 11 tests pasan; `ComponentesSinAccesoADatosTests` sigue verde con los archivos nuevos dentro de
su escaneo; el componente nuevo está registrado en `ComposicionDeLaPantalla.cs` si corresponde.

---

## Final verification

Cuando los cinco bloques estén hechos:

| AC | Verificado por |
|---|---|
| AC-01 | Block 3 — `Un_manager_aprueba_una_solicitud_pendiente_de_su_equipo` (+1 designado) |
| AC-02 | Block 3 — `Un_manager_rechaza_una_solicitud_pendiente_indicando_un_motivo` |
| AC-03 | Block 3 — `Rechazar_sin_motivo_se_impide_con_el_mensaje_de_AC_03` |
| AC-04 | Block 3 — `Resolver_una_solicitud_ya_aprobada/rechazada_se_impide...` (+2, directo y reintento) |
| AC-05 | Block 3 — `La_resolucion_registra_quien_y_cuando` |
| AC-06 | Block 3 — `El_empleado_ve_quien_resolvio_y_el_motivo_en_su_listado` · Block 5 —
  `Mis_solicitudes_muestra_quien_resolvio_y_el_motivo_del_rechazo` |
| AC-07 | Block 4 — `El_manager_ve_las_pendientes_de_su_equipo` (+2) |
| AC-08 | Block 2 — `Un_empleado_sin_relacion_no_puede_ver/resolver...` (+2) · Block 3 —
  `Un_empleado_sin_relacion_no_puede_resolver_y_la_excepcion_se_propaga` |

Y además:

- **`dotnet test src/GestionVacaciones.slnx` en verde**, con los 284 tests de FEAT-001a+b+c más los
  50 de este ticket, y **0 advertencias** (`TreatWarningsAsErrors` activo).
- Cobertura ≥ 80% en líneas, ramas y funciones sobre el código nuevo (NFR-01).
- `PermisosService` sigue siendo la única sede de la decisión de acceso — ningún otro archivo de
  `src/` construye `AccesoASolicitudesDenegadoException` (extensión del escaneo existente a
  `ResolverAsync` y al listado del equipo).
- Un empleado nunca puede resolver su propia solicitud, verificado explícitamente (R-22).
- El motivo de rechazo se renderiza siempre como texto plano (R-24).
- Los guardarraíles estructurales de FEAT-001a/b/c siguen vigentes: `IDbContextFactory` y nunca
  `AddDbContext`, sin `MarkupString`, sin reloj propio en ningún `.razor`, la UI sin reglas propias,
  los literales atados al PRD, los diagnósticos sin PII.
- SAST PASSED sobre el delta.
