# VERIFY FEAT-002 — Aprobación y rechazo de solicitudes por el manager

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-002 |
| Tier | FEATURE |
| PRD | `docs/daw/prd/prd-FEAT-002.md` |
| Spec | `docs/daw/specs/spec-FEAT-002.md` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-002.md` |
| SAST | `docs/daw/security/sast-FEAT-002.md` (PASSED en CODE) |
| Rama | `feat/FEAT-002-aprobacion-manager` |
| Diff verificado | `6d681b0..HEAD` (5 commits + SAST) |

## Ronda 1 — 2026-08-07

**Veredicto: BLOCKED** (1 FAIL, 2 WARN)

Suite ejecutada de forma independiente por el verificador (no tomada del historial):
`dotnet test src/GestionVacaciones.slnx --collect:"XPlat Code Coverage"` → **334/334 passed, 0
failed, 0 skipped, 32s.** Cobertura global: 95.45% líneas / 92.16% ramas. Build limpio
(`--no-incremental`): 0 warnings, 0 errors (`TreatWarningsAsErrors` activo).

### Trazabilidad PRD → Código → Tests (8 AC)

| AC | Verificado por | Resultado |
|---|---|---|
| AC-01 (aprobar Pendiente) | `ResolucionDeSolicitudTests.Un_manager_aprueba_una_solicitud_pendiente_de_su_equipo` (+designado) | ✅ |
| AC-02 (rechazar con motivo) | `Un_manager_rechaza_una_solicitud_pendiente_indicando_un_motivo` | ✅ |
| AC-03 (rechazo sin motivo, mensaje exacto) | `Rechazar_sin_motivo_se_impide_con_el_mensaje_de_AC_03` (Theory) + `MensajesLiteralesDeFeat002Tests` (contra el PRD versionado) | ✅ |
| AC-04 (no resolver algo no Pendiente) | Camino directo + `Dos_resoluciones_concurrentes_de_la_misma_solicitud_una_gana_y_la_otra_ve_ya_resuelta` (real, `Task.WhenAll`, sin try/catch) + `Un_conflicto_de_serializacion_que_no_es_una_carrera_de_resolucion_se_propaga` | ✅ |
| AC-05 (quién y cuándo) | `La_resolucion_registra_quien_y_cuando` | ✅ |
| AC-06 (empleado ve quién resolvió y el motivo) | `El_empleado_ve_quien_resolvio_y_el_motivo_en_su_listado` + `MisSolicitudesTests.Mis_solicitudes_muestra_quien_resolvio_y_el_motivo_del_rechazo` + `El_motivo_se_muestra_como_texto_plano_sin_interpretarse` | ✅ |
| AC-07 (listado de pendientes del equipo) | `ListadoDelEquipoTests` (8 tests) + `AutorizacionesTests.El_manager_ve_el_listado_de_pendientes_de_su_equipo` | ✅ |
| AC-08 (restricción de visualización y resolución) | `PermisosDeVisibilidadTests` + `AutorizacionDeResolucionTests.Un_empleado_no_puede_resolver_su_propia_solicitud` (anomalía `ManagerId==Id` sembrada a propósito) | ✅ |

### Spec: 5 bloques

| Bloque | Tests requeridos | Resultado |
|---|---|---|
| 1 — Esquema | 4/4 | ✅ |
| 2 — PermisosService (organigrama) | 12/12 | ✅ |
| 3 — ResolverAsync | 15/15 | ✅ |
| 4 — Listado del equipo | 8/8 | ✅ |
| 5 — UI: pantalla de autorizaciones | 11/11 por nombre exacto | ⚠️ ver hallazgo FAIL — dos ramas de manejo de error añadidas por este bloque quedan sin ejecutar |

### Trazabilidad transversal del ticket

- ✅ `PermisosService` única sede — `Ningun_otro_archivo_de_src_decide_negar_la_visibilidad` escanea TODO `src/**/*.cs`/`*.razor`: 3 apariciones, las 3 en `PermisosService.cs`.
- ✅ Autoaprobación imposible — verificado con la anomalía `ManagerId==Id`.
- ✅ Motivo de rechazo como texto plano en todos los lugares — `grep -rn MarkupString src/` → 0 resultados en todo el proyecto.
- ✅ `IDbContextFactory` siempre, `AddDbContext` nunca.
- ✅ Sin reloj propio en ningún `.razor` — `ComponentesSinAccesoADatosTests` escanea TODO `src/GestionVacaciones.Web/**/*.razor`.
- ✅ Literales atados al PRD — `MensajesLiteralesDeFeat002Tests` compara contra `prd-FEAT-002.md` versionado.
- ✅ Diagnósticos sin PII — `DiagnosticoSinPiiTests` actualizado y verde, aunque no extendido a los 2 tipos nuevos (ver WARN).

### Cobertura de archivos nuevos/modificados (medida, no asumida)

| Archivo | Cobertura |
|---|---|
| `VacacionesDbContext.cs` | 100.0% |
| `ErroresDeSolicitud.cs` | 100.0% |
| `PermisosService.cs` | 100.0% |
| `SolicitudesService.cs` | 97.0% (321/331 líneas) |
| Migración `ColumnasDeResolucion` (+Designer) | 100.0% |
| `ListadoDeSolicitudes.razor` | 100.0% |
| `Autorizaciones.razor` | 92.2% (83/90 líneas) |
| `Solicitud.cs` | 83.3% (10/12 — navigation properties, patrón preexistente) |
| **`MainLayout.razor`** | **72.2% (13/18) — por debajo del 80% que exige NFR-01** |

### Hallazgos

**❌ FAIL — NFR-01 incumplido en `MainLayout.razor` (código 100% nuevo de este ticket)**

`src/GestionVacaciones.Web/Components/Layout/MainLayout.razor:54-71`. El bloque `@code` completo
(try/catch alrededor de `Solicitudes.TieneEquipoACargoAsync()`) lo agrega este ticket entero. El
`catch (Exception)` (líneas 60-66) — que oculta el link y deja el resto del layout en pie ante
cualquier fallo — tiene **0 ejecuciones** en los 334 tests. Cobertura del archivo: 72.2%, por debajo
del 80% de NFR-01. `AutorizacionesTests.cs` solo ejercita `MainLayout` con el camino feliz (con
equipo / sin equipo), nunca con una fábrica que falle — el mismo patrón (`FabricaQueNadieDebeUsar`)
ya existe en el mismo archivo de test y se usa para `Autorizaciones.razor` tres líneas más abajo, así
que no es una limitación técnica: es un caso que no se escribió. Dado que `MainLayout` envuelve
`@Body` —toda la aplicación—, una regresión futura en ese catch rompería la pantalla completa para
cualquier usuario, y hoy nada lo detectaría. Precedente directo en este mismo proyecto: F-VER-04 de
FEAT-001a bloqueó VERIFY exactamente por este patrón ("rama de producción sin ninguna ejecución").

**⚠️ WARN — camino triste de `Autorizaciones.ResolverAsync` sin ejercer**

El `catch (Exception)` genérico de `Autorizaciones.razor:286-291`, que fija
`_mensajeDeResolucion = MensajeDeFalloAlResolver`, no lo dispara ningún test.
`Si_resolver_falla_se_muestra_el_mensaje_sin_romper_la_pantalla` ejercita el camino de rechazo de
negocio (`ResolverAsync` devuelve `FueResuelta=false`), no una excepción real. El archivo en conjunto
sigue por encima del 80% (92.2%), no rompe NFR-01, pero es el mismo patrón de gap.

**⚠️ WARN — dos `ToString()` de diagnóstico nuevos sin la cobertura que el proyecto exige para ese
patrón**

`ResultadoDeLaResolucion.ToString()` (`SolicitudesService.cs:173-176`) y
`SolicitudPendienteDelEquipo.ToString()` (líneas 249-250), documentados como mitigación de R-12, no
aparecen en `DiagnosticoSinPiiTests.cs`. Cobertura real: 0% en ambos. El segundo es más sensible:
`SolicitudPendienteDelEquipo` lleva `NombreDelEmpleado` (PII real) entre sus propiedades.

**ℹ️ Nota menor, no bloqueante** — `Solicitud.Empleado`/`Solicitud.ResueltoPor` (navigation
properties) sin cobertura de getter/setter; patrón preexistente heredado de `Empleado`, no una
regresión de este ticket.

### Calidad

- ✅ Sin código muerto (`grep TODO/FIXME`: 0 resultados en los 4 archivos de dominio nuevos).
- ✅ Sin imports sin usar (build limpio con `TreatWarningsAsErrors`).
- ✅ Sin tests frágiles (`TiempoFijo` en todos los tests de concurrencia/tiempo, sin IDs mágicos).
- ✅ SAST — PASSED (ya verificado en CODE, 0 Critical/High/Medium, 1 LOW no bloqueante).

### Corrección sugerida

1. Agregar en `AutorizacionesTests.cs` un test de `MainLayout` con `FabricaQueNadieDebeUsar` (mismo
   patrón ya usado tres líneas abajo en el mismo archivo) que confirme: el link no aparece y el resto
   del layout (`@Body`) se sigue renderizando cuando `TieneEquipoACargoAsync` falla.
2. (Recomendado, no bloqueante) Un test análogo para el `catch(Exception)` de
   `Autorizaciones.ResolverAsync`, asertando `MensajeDeFalloAlResolver`.
3. (Recomendado, no bloqueante) Extender `DiagnosticoSinPiiTests.cs` con un caso para
   `ResultadoDeLaResolucion.ToString()` y otro para `SolicitudPendienteDelEquipo.ToString()`.

**Acción:** bucle correctivo VERIFY → CODE. Corregir el punto 1 (bloqueante) antes de reintentar el
cierre; los puntos 2 y 3 quedan a criterio del usuario para esta misma vuelta.

---

## Ronda 2 — 2026-08-07

**Veredicto: PASSED** (0 FAIL, 0 WARN bloqueante)

**Contexto.** Ronda 2 verifica el ticket completo tras el bucle correctivo que cerró el único FAIL
de Ronda 1 (`MainLayout.razor` 72.2% < 80%, NFR-01) y los 2 WARN no bloqueantes (camino triste de
`Autorizaciones.ResolverAsync`, dos `ToString()` de diagnóstico sin cobertura). El commit `71c33b4`
agrega 4 tests, sin tocar ningún archivo de producción; ya fue revisado en un ciclo focalizado por
`daw-module-verifier` + `daw-arch-auditor` (ambos PASSED). Esta ronda repite el protocolo completo
sobre el ticket entero, no solo el delta, apoyándose en la Ronda 1 donde ya está confirmado y
re-verificando de cero lo que cambió.

Suite ejecutada de forma independiente (no tomada del historial), tras `dotnet build-server
shutdown` (no hicieron falta reintentos por locks de MSBuild en esta corrida):
`dotnet test src/GestionVacaciones.slnx --collect:"XPlat Code Coverage"` → **338/338 passed, 0
failed, 0 skipped, 27s.** Ningún test de integración salió por `SaltearSiNoEstaDisponible()`: la
instancia SQL2022 estuvo disponible y los 0 skipped lo confirman. Build limpio con `-t:Rebuild`
(fuerza recompilación completa, equivalente a `--no-incremental` que esta versión del SDK no
admite como switch de `dotnet test`): **0 Warning(s), 0 Error(s)** (`TreatWarningsAsErrors` activo).

### 1. Trazabilidad PRD → Código → Tests (8 AC) — confirmación tras el fix

| AC | Verificado por | Resultado |
|---|---|---|
| AC-01 (aprobar Pendiente) | `ResolucionDeSolicitudTests.Un_manager_aprueba_una_solicitud_pendiente_de_su_equipo` (+designado) — verifica estado, `ResueltoPorId`, `FechaResolucion` releídos de la base | ✅ |
| AC-02 (rechazar con motivo) | `Un_manager_rechaza_una_solicitud_pendiente_indicando_un_motivo` | ✅ |
| AC-03 (rechazo sin motivo, mensaje exacto) | `Rechazar_sin_motivo_se_impide_con_el_mensaje_de_AC_03` + `MensajesLiteralesDeFeat002Tests` (lee el PRD versionado, no una copia) | ✅ |
| AC-04 (no resolver algo no Pendiente) | Camino directo + `Dos_resoluciones_concurrentes_de_la_misma_solicitud_una_gana_y_la_otra_ve_ya_resuelta` (`Task.WhenAll` real, sin try/catch oculto) | ✅ |
| AC-05 (quién y cuándo) | `La_resolucion_registra_quien_y_cuando` | ✅ |
| AC-06 (empleado ve quién resolvió y el motivo) | `El_empleado_ve_quien_resolvio_y_el_motivo_en_su_listado` + `MisSolicitudesTests.Mis_solicitudes_muestra_quien_resolvio_y_el_motivo_del_rechazo` | ✅ |
| AC-07 (listado de pendientes del equipo) | `ListadoDelEquipoTests` (8 tests) + `AutorizacionesTests.El_manager_ve_el_listado_de_pendientes_de_su_equipo` | ✅ |
| AC-08 (restricción de visualización y resolución) | `PermisosDeVisibilidadTests` + `AutorizacionDeResolucionTests.Un_empleado_no_puede_resolver_su_propia_solicitud` (anomalía `ManagerId==Id` sembrada) | ✅ |

Nada de lo confirmado en Ronda 1 se rompió: los 334 tests originales siguen en verde y ninguno de
los 4 tests nuevos toca producción, por lo que no hay superficie nueva de regresión sobre los AC.

### 2. Los 3 hallazgos de Ronda 1 — cierre verificado, no de oídas

Cobertura recalculada por este verificador contra el XML de `coverage.cobertura.xml` de esta misma
corrida (no la cifra reportada por el implementador ni por revisiones previas):

| Archivo | Ronda 1 | Ronda 2 (medido ahora) | Estado |
|---|---|---|---|
| `MainLayout.razor` | 72.2% (13/18 líneas) | **100.0% (36/36 líneas, branch 100%)** | ✅ FAIL cerrado |
| `Autorizaciones.razor` | 92.2% (83/90) | **97.8% (176/180)** | ✅ WARN cerrado |
| `SolicitudesService.cs` (contiene los dos `ToString()`) | 97.0% (321/331) | **99.1% (656/662** sobre el archivo completo tras sumar las líneas de los bloques previos**)** | ✅ WARN cerrado |

Confirmación puntual, no solo agregada:
- `MainLayout.razor:54-71` — el `catch (Exception)` alrededor de `TieneEquipoACargoAsync()` ahora se
  ejecuta: `AutorizacionesTests.Si_TieneEquipoACargoAsync_falla_el_link_no_aparece_y_el_resto_del_layout_sigue_en_pie`
  usa `FabricaQueNadieDebeUsar` (mismo patrón que el resto del archivo) y asegura dos cosas a la vez —
  el link no aparece y `@Body` se sigue renderizando —, no solo que el catch se disparó.
- `Autorizaciones.razor` — `Si_resolver_lanza_una_excepcion_real_se_muestra_el_mensaje_generico_sin_romper_la_pantalla`
  borra la fila de la base entre el render y el clic para forzar una `ArgumentException` real desde
  `IntentarResolverAsync` (no el rechazo de negocio ya cubierto por el test hermano), y verifica el
  mensaje genérico más que el resto de la pantalla sigue en pie.
- `ResultadoDeLaResolucion.ToString()` / `SolicitudPendienteDelEquipo.ToString()` —
  `DiagnosticoSinPiiTests` verifica ambas direcciones: el diagnóstico sigue siendo útil (los IDs
  aparecen) y no filtra PII (`NombreDelEmpleado` ausente del `ToString()`, dígitos ausentes alrededor
  del literal de rechazo). Confirmé por lectura de `SolicitudesService.cs` que ambos `ToString()`
  están escritos a mano (no autogenerados por el `record`), por lo que el test sí podía fallar si el
  override se hubiera omitido o hubiera incluido el nombre.

**TDD evidence de este bucle correctivo.** El caso es atípico: el implementador declaró 0 archivos de
producción modificados — las 3 ramas ya eran correctas, solo faltaba ejercitarlas. No aplica el
criterio estándar de "N tests fallando antes de escribir código de producción" porque no se escribió
código de producción nuevo en este commit. Lo que sí es verificable, y lo verifiqué: (a) Ronda 1 midió
0 ejecuciones exactas en esas mismas líneas antes del commit `71c33b4` — evidencia independiente, no
la palabra del implementador, de que el gap era real; (b) los 4 tests nuevos, leídos línea por línea,
efectivamente ejercitan esas ramas (fábrica que revienta para el catch de `MainLayout`, fila borrada
para forzar la excepción real en `Autorizaciones`, valores sembrados para los dos `ToString()`) y no
podrían pasar por otro camino — no son un `Assert.True(true)` disfrazado; (c) la cobertura post-commit,
medida por mí en esta misma ronda, confirma el salto exacto que el commit reclama (72.2%→100%,
92.2%→97.8%). No hay discrepancia entre lo declarado y lo que hay en el disco.

### 3. Regresión completa

- **338/338 passed, 0 failed, 0 skipped** (ejecución propia de este verificador).
- Build (`dotnet build -t:Rebuild`, fuerza recompilación total): **0 Warning(s), 0 Error(s)**.
- Cobertura global: **96.29% líneas / 93.28% ramas** (subió desde 95.45%/92.16% de Ronda 1, consistente
  con 4 tests nuevos que no agregan líneas de producción).

### 4. Trazabilidad transversal — confirmación rápida

- ✅ `PermisosService` única sede de `AccesoASolicitudesDenegadoException` — `grep -rn "new
  AccesoASolicitudesDenegadoException" src/` → 3 apariciones, las 3 en
  `src/GestionVacaciones.Data/Services/PermisosService.cs` (líneas 155, 212, 277).
- ✅ Sin `MarkupString` — `grep -rn MarkupString src/` → 0 resultados.
- ✅ Sin reloj propio en ningún `.razor` — `grep` por `TimeProvider`/`DateTime.Today`/`DateTime.Now`
  sobre `src/GestionVacaciones.Web/Components/**/*.razor` → 0 resultados.
- ✅ Literales atados al PRD — `MensajesLiteralesDeFeat002Tests` lee
  `docs/daw/prd/prd-FEAT-002.md` versionado (no una copia del directorio de salida) y busca el literal
  entrecomillado exacto; confirmado por lectura del archivo de test.

### 5. Calidad general

- ✅ Sin código muerto — `grep -n "TODO\|FIXME"` sobre los archivos `src/` tocados por el ticket
  (`git diff --name-only 6d681b0..HEAD -- 'src/*'`): 0 resultados relevantes (los únicos `#pragma
  warning disable 612, 618` están en `*.Designer.cs`/`ModelSnapshot.cs`, generados automáticamente
  por `dotnet ef migrations add`, no código escrito a mano).
- ✅ Sin imports sin usar — build limpio con `TreatWarningsAsErrors` activo (`CS8019`/análogos
  romperían el build).
- ✅ Sin tests frágiles — los 4 tests nuevos usan `TiempoFijo`/`FabricaQueNadieDebeUsar` (mismos
  helpers que el resto de la suite), sin IDs mágicos ni `Thread.Sleep`.
- ✅ Coverage: 96.29% líneas / 93.28% ramas global; archivos del ticket todos ≥ 83.3% (el único bajo
  90% es `Solicitud.cs` al 83.3%, navigation properties preexistentes, ya señalado como no bloqueante
  en Ronda 1 y sin cambios en esta ronda).
- ✅ SAST — PASSED, ejecución 2 sobre el delta del bucle correctivo (`docs/daw/security/sast-FEAT-002.md`),
  15 categorías limpias, 1 LOW no bloqueante sin cambios (motivo de rechazo sin `MaxLength` explícito
  del lado cliente/servicio — solo defendido por la constraint de `nvarchar(1000)`; no es una
  vulnerabilidad, queda igual que en Ronda 1).

### Hallazgos de Ronda 2

Ninguno nuevo. Los 3 de Ronda 1 quedan cerrados con evidencia medida por este verificador, no
heredada del implementador ni de la revisión focalizada previa.

### Veredicto

**PASSED.** Los 8 AC del PRD siguen cubiertos con test real, los 5 bloques de la spec completos, el
FAIL bloqueante de Ronda 1 (NFR-01 en `MainLayout.razor`) cerrado y verificado con cobertura medida
en esta misma ronda (72.2% → 100%), los 2 WARN no bloqueantes también cerrados, 338/338 tests en
verde, 0 warnings de build, SAST PASSED, y la trazabilidad transversal del ticket (sede única de
denegación de acceso, sin `MarkupString`, sin reloj propio, literales atados al PRD) confirmada de
nuevo. Listo para pasar a RELEASE.

**FAILs: 0 | WARNs: 0 | PASSes: 16**
