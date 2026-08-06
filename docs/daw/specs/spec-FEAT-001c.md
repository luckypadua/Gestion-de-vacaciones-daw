# Spec FEAT-001c: No superposición de períodos

| Field | Value |
|-------|-------|
| Ticket | FEAT-001c |
| PRD | `docs/daw/prd/prd-FEAT-001c.md` |
| Tier | FEATURE |
| Date | 2026-08-06 |
| Spec loops | 0 |

## Summary

`SolicitudesService.CrearAsync` gana una tercera regla, después de las dos de fecha y antes del
tope: ningún empleado puede tener dos solicitudes vigentes (`Pendiente` o `Aprobada`) sobre fechas
que se tocan. La consulta que lo decide tiene la misma forma que la que `SaldoService` ya ejecuta
—mismo `EmpleadoId`, mismo filtro de estado, mismo patrón de solapamiento contra un rango de
fechas— y por eso se apoya en el mismo índice que el Bloque 5 de FEAT-001b ya creó: **este ticket no
agrega ninguna migración**. Los dos estados que bloquean pasan de ser un detalle privado de
`SaldoService` a una fuente compartida (`EstadosDeSolicitud.Vigentes`), para que "qué estados
cuentan" se responda en un único lugar y no en dos literales que puedan divergir. Bajo concurrencia
real, un conflicto de serialización del motor se resuelve con un reintento único y dirigido —vuelve
a preguntar solo por la superposición, nunca en bucle— para poder cumplir el AC-04 literal (rechazo
con mensaje, no una excepción cruda) sin reintroducir el reintento en bucle que R-16 de FEAT-001b ya
descartó.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 (impedir la creación si el período se superpone) | Block 1 |
| FR-02 (verificación y persistencia en una única operación atómica) | Block 1 |
| NFR-01 (verificación e inserción en 1 única transacción) | Strategy: reutiliza la transacción `Serializable` que `CrearAsync` ya abre desde el Bloque 3 de FEAT-001b; la consulta de solapamiento corre sobre la misma conexión, antes del `SaveChangesAsync` |
| NFR-02 (cobertura ≥ 80%) | Strategy: gate `daw-test` con `--collect:"XPlat Code Coverage"`; el proyecto viene del 95%+ y no baja |
| NFR-03 (p95 < 3s con 50 concurrentes) | Strategy: misma justificación que NFR-03 de FEAT-001b — la consulta de solapamiento tiene la forma exacta que ya se apoya en `IX_Solicitud_EmpleadoId_Estado_FechaInicio`, así que hereda la misma condición estructural. La medición sigue diferida a un ticket de performance propio |
| NFR-04 (1 índice sobre empleado y fechas del período) | Strategy: **reutiliza `IX_Solicitud_EmpleadoId_Estado_FechaInicio`** (Bloque 5, FEAT-001b) — no se crea ningún índice nuevo. La consulta de solapamiento (`EmpleadoId == autor && Estado ∈ {Pendiente, Aprobada} && FechaInicio <= nuevoFin && FechaFin >= nuevoInicio`) tiene la misma forma que la que `SaldoService.DeLosAniosAsync` ya ejecuta contra ese índice (mismo *seek* por `EmpleadoId`+`Estado`+rango de `FechaInicio`, mismo residual sobre `FechaFin`) |

**AC → tests** está en el bloque; el mapa completo se recompone en *Final verification*.

## Dependencies between blocks

Un solo bloque. No hay dependencias entre bloques porque no hay más de uno.

---

## Block 1 — La superposición bloquea el alta

**Files**
- `src/GestionVacaciones.Data/Services/EstadosDeSolicitud.cs` (nuevo) — fuente única de qué estados
  de una solicitud están vigentes.
- `src/GestionVacaciones.Data/Services/SaldoService.cs` (modificado) — `_estadosQueConsumenSaldo`
  se reemplaza por `EstadosDeSolicitud.Vigentes`; se borra el campo privado.
- `src/GestionVacaciones.Data/Services/SolicitudesService.cs` (modificado) — la consulta de
  solapamiento, el rechazo, el log de R-20, y el reintento dirigido ante conflicto de
  serialización, todos dentro de `CrearAsync`.
- `src/GestionVacaciones.Data/Services/ErroresDeSolicitud.cs` (modificado) — el literal de AC-01.
- `src/GestionVacaciones.Data/VacacionesDbContext.cs` (modificado, solo comentario) — el XML-doc de
  `IndiceDelSaldo` pasa a explicar que sirve dos consultas, no una.
- `tests/GestionVacaciones.Tests/Dominio/SuperposicionDePeriodosTests.cs` (nuevo).
- `tests/GestionVacaciones.Tests/Dominio/MensajesLiteralesDeFeat001cTests.cs` (nuevo) — hermano de
  `MensajesLiteralesDeFeat001bTests.cs`, apunta a `prd-FEAT-001c.md`.

**Logic**

`EstadosDeSolicitud.cs`:
```
public static class EstadosDeSolicitud
{
    public static readonly IReadOnlyList<EstadoSolicitud> Vigentes =
        [EstadoSolicitud.Pendiente, EstadoSolicitud.Aprobada];
}
```
Mismo patrón que `TopeAnual.cs`: un archivo dedicado en `Data/Services/`, sin dueño operativo, del
que `SaldoService` y `SolicitudesService` consumen por igual. `SaldoService.DeLosAniosAsync` cambia
`_estadosQueConsumenSaldo.Contains(...)` por `EstadosDeSolicitud.Vigentes.Contains(...)` — sin
cambiar su comportamiento observable, así que ningún test de `SaldoDelAnioTests` ni de
`PuntoUnicoDelTopeTests` debería romperse por este cambio.

`SolicitudesService.CrearAsync` — el orden de validación queda: fecha de inicio → fecha de fin →
atajo de más de 2 años (sin tocar la base, sin cambios) → abrir transacción → lectura *fence*
existente (sin tocar, línea a línea igual que hoy) → **consulta de solapamiento (nueva)** → tope
(sin cambios de lógica, solo se corre después). La superposición se valida **antes** que el tope:
es un problema más fundamental (ya tenés esas fechas reservadas) y más barato de comprobar (no
necesita `SaldoService`).

**La consulta de solapamiento**, sobre el mismo `contexto`/transacción que ya usa la lectura *fence*:
```
await contexto.Solicitudes
    .AsNoTracking()
    .Where(s => s.EmpleadoId == autor
        && EstadosDeSolicitud.Vigentes.Contains(s.Estado)
        && s.FechaInicio <= fechaFin
        && s.FechaFin >= fechaInicio)
    .AnyAsync(cancelacion)
```
Si devuelve `true` → `await transaccion.RollbackAsync(cancelacion)` (mismo patrón que ya usa el
rechazo por tope, línea 378 actual) → log de R-20 → `ResultadoDelAlta.Rechazada(ErroresDeSolicitud.SuperposicionDePeriodo)`.
**No** `RechazadaPorSaldoInsuficiente`: este mensaje no lleva ningún dato de la persona, así que
`ToString()` no necesita omitirlo (a diferencia de R-14).

**El reintento dirigido (R-16, R-20, C14 del modelo de amenazas).** El bloque completo —desde
`BeginTransactionAsync` hasta `CommitAsync`— queda envuelto en un único punto de captura para el
conflicto de serialización de SQL Server (`SqlException` con `Number == 1205`, la misma condición
que R-16 de FEAT-001b ya nombra como "deadlock víctima"). Al capturarlo:
1. Se abre una transacción **nueva** (la anterior ya se deshizo: el conflicto la invalidó).
2. Se repite **solo** la consulta de solapamiento de arriba, nunca el resto del flujo.
3. Si ahora encuentra una fila (la que ganó la carrera ya está committeada y visible) →
   mismo camino de rechazo de arriba: log de R-20 marcando que vino del reintento, y
   `Rechazada(SuperposicionDePeriodo)`.
4. Si no encuentra nada → el conflicto no era una superposición; se repropaga la excepción
   original, sin convertir (mismo criterio que R-16: un fallo de motor no se disfraza de rechazo
   de validación).

Esto **no** es el reintento en bucle que R-16 prohíbe para el tope: es un reintento único, dirigido
a una sola pregunta ("¿esto era una superposición?"), y solo se dispara ante ese código de error
específico — nunca ante un fallo de persistencia genérico, que se sigue propagando sin ningún
intento de conversión.

**El log de R-20.** `_registro.LogInformation(...)` en el punto de rechazo (los dos caminos: directo
y por reintento), con `EmpleadoId` y un indicador de cuál de los dos caminos disparó el rechazo —
nunca fechas, nunca el identificador de la otra solicitud (fuera de alcance del PRD). Mismo nivel y
mismo criterio de qué se loguea que R-18 de FEAT-001b.

**Input validation**

Sin cambios: las fechas siguen llegando como dos `DateOnly` ya validadas por las dos comprobaciones
existentes (inicio no anterior a hoy, fin no anterior a inicio) antes de llegar a este punto.

**Error handling**
- Superposición detectada → `Rechazada(SuperposicionDePeriodo)`, no excepción — mismo criterio que
  el resto de los rechazos de validación de este método.
- Conflicto de serialización que SÍ es una superposición real → se convierte en el mismo rechazo,
  vía el reintento dirigido.
- Conflicto de serialización que NO es una superposición (u otro fallo de motor cualquiera) → se
  propaga tal cual, sin `catch` que lo enmascare — mismo criterio que R-16.
- Fallo de persistencia dentro de la transacción → rollback y se propaga. Sin cambios respecto del
  comportamiento ya existente para el tope: este bloque no le agrega ningún caso nuevo, y sigue
  cubierto por `TopeAnualEnElAltaTests.Un_fallo_al_guardar_revierte_la_transaccion_y_se_propaga`
  (FEAT-001b), que no se toca.

**Required tests**
- [ ] `Un_periodo_totalmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01` — el nuevo período
  contiene por completo a uno existente. Valida AC-01.
- [ ] `Un_periodo_parcialmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01` — `Theory` con los
  dos sentidos: el nuevo empieza antes y termina dentro, el nuevo empieza dentro y termina después.
  Valida AC-01.
- [ ] `Un_periodo_contiguo_el_dia_siguiente_se_acepta` — el nuevo empieza el día después de que
  termina el existente. Valida AC-02, el literal del PRD.
- [ ] `Un_periodo_contiguo_el_dia_anterior_se_acepta` — el espejo de AC-02: el nuevo termina el día
  antes de que empiece el existente. Camino feliz simétrico, no pedido literalmente por el PRD pero
  necesario para no dejar el borde solo probado de un lado.
- [ ] `Una_solicitud_rechazada_no_bloquea_las_mismas_fechas` — la única solicitud que coincide en
  fechas está en `Rechazada`; la nueva se acepta. Valida AC-03.
- [ ] `Una_solicitud_pendiente_bloquea_igual_que_una_aprobada` — `Theory` sobre los dos estados de
  `EstadosDeSolicitud.Vigentes`. Refuerza FR-01.
- [ ] `Un_periodo_que_no_se_superpone_con_nada_se_acepta` — camino feliz sin solicitudes previas.
- [ ] `Dos_altas_concurrentes_de_periodos_solapados_persisten_exactamente_una` — dos `CrearAsync` en
  paralelo contra la instancia real, con períodos que se solapan entre sí; exactamente una queda
  creada y la otra vuelve `Rechazada(SuperposicionDePeriodo)` (nunca una excepción sin capturar).
  Valida AC-04 y ejercita el reintento dirigido de punta a punta.
- [ ] `La_superposicion_se_rechaza_sin_consultar_el_saldo` — camino triste de orden: `SaldoService`
  se arma con `FabricaQueNadieDebeUsar` (mismo patrón que `TopeAnualEnElAltaTests.ServicioPara`); si
  hay superposición, el test revienta en cuanto `DeLosAniosAsync` abriera contexto, así que pasar
  confirma que nunca se llega a preguntarle al saldo.
- [ ] `El_conflicto_de_serializacion_de_una_superposicion_real_se_convierte_en_rechazo` — se inyecta
  la excepción (mismo patrón que
  `TopeAnualEnElAltaTests.Una_excepcion_de_base_que_no_es_de_validacion_se_propaga_sin_convertirse_en_rechazo`,
  con un interceptor de EF Core) contra un escenario donde la fila que causó el conflicto sí existe
  al momento del reintento. El resultado es `Rechazada(SuperposicionDePeriodo)`, no la excepción.
- [ ] `El_conflicto_de_serializacion_que_no_es_superposicion_se_propaga` — mismo mecanismo de
  inyección, pero sin ninguna fila que superponga al reintentar: la excepción original sale sin
  convertirse.
- [ ] `El_rechazo_por_superposicion_queda_registrado` — el log de R-20 tiene `EmpleadoId` y no tiene
  fechas ni el identificador de la otra solicitud. Mitigación de R-20.
- [ ] `El_literal_del_rechazo_coincide_con_prd_FEAT_001c` (en `MensajesLiteralesDeFeat001cTests.cs`)
  — lee `docs/daw/prd/prd-FEAT-001c.md`, extrae el entrecomillado de AC-01 y lo compara contra
  `ErroresDeSolicitud.SuperposicionDePeriodo`. Mismo patrón que
  `MensajesLiteralesDeFeat001bTests.Los_literales_coinciden_con_prd_FEAT_001b`.

**Completion criterion**

Los 13 tests pasan; `AltaDeSolicitudTests.cs` y `TopeAnualEnElAltaTests.cs` siguen verdes sin
tocarlos; borrar la consulta de solapamiento deja
`Un_periodo_totalmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01` en rojo; borrar el reintento
dirigido (dejando que la excepción de conflicto se propague siempre) deja
`El_conflicto_de_serializacion_de_una_superposicion_real_se_convierte_en_rechazo` en rojo.

---

## Final verification

Cuando el bloque esté hecho:

| AC | Verificado por |
|---|---|
| AC-01 | `Un_periodo_totalmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01` (+1 parcial) |
| AC-02 | `Un_periodo_contiguo_el_dia_siguiente_se_acepta` (+1 espejo) |
| AC-03 | `Una_solicitud_rechazada_no_bloquea_las_mismas_fechas` |
| AC-04 | `Dos_altas_concurrentes_de_periodos_solapados_persisten_exactamente_una` (+2 tests de inyección del reintento) |

Y además:

- **`dotnet test src/GestionVacaciones.slnx` en verde**, con los 269 tests de FEAT-001a+b más los 13
  de este ticket, y **0 advertencias** (`TreatWarningsAsErrors` activo).
- Cobertura ≥ 80% en líneas, ramas y funciones sobre el código nuevo (NFR-02).
- `EstadosDeSolicitud.Vigentes` es la única fuente de "qué estados bloquean/consumen saldo"; ni
  `SaldoService` ni `SolicitudesService` repiten el par `{Pendiente, Aprobada}` como literal propio.
- Ningún índice ni migración nueva — `IX_Solicitud_EmpleadoId_Estado_FechaInicio` sigue siendo el
  único índice que sirve tanto al saldo como a la superposición.
- SAST PASSED sobre el delta.
- Los guardarraíles estructurales de FEAT-001a/b siguen vigentes: `IDbContextFactory` y nunca
  `AddDbContext`, sin `MarkupString`, la UI sin reglas propias, `PermisosService` como sede única,
  sin `catch` silencioso, los literales atados al PRD, los diagnósticos sin PII.
