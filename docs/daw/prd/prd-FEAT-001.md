# Parent PRD: Solicitar vacaciones — alta de solicitud con validación de tope anual y superposición, y saldo del año en curso

| Metric | Value |
|--------|-------|
| Ticket | FEAT-001 |
| Date | 2026-08-01 |
| Status | Split — los 3 sub-tickets completos (a, b, c); integración pendiente de mergear los 3 PRs en cadena |

## Sub-tickets

| Sub-ticket | Title | PRD | Dependencies | Status | Integration |
|---|---|---|---|---|---|
| FEAT-001a | Andamiaje, identidad del empleado y alta de solicitud con validación de fechas y listado propio | `prd-FEAT-001a.md` | none | done | [PR #1](https://github.com/luckypadua/Gestion-de-vacaciones-daw/pull/1) (draft) — se integra al mergearlo |
| FEAT-001b | Imputación de días por año calendario, tope anual y saldo | `prd-FEAT-001b.md` | depends on a | done | [PR #2](https://github.com/luckypadua/Gestion-de-vacaciones-daw/pull/2) (draft, contra `feat/FEAT-001a-andamiaje-alta-solicitud`) — rama dejada; FEAT-001c parte de su punta |
| FEAT-001c | No superposición de períodos | `prd-FEAT-001c.md` | depends on a | done | [PR #3](https://github.com/luckypadua/Gestion-de-vacaciones-daw/pull/3) (draft, contra `feat/FEAT-001b-imputacion-tope-saldo`) — se integra cuando se mergeen los 3 PRs en cadena (#1 → #2 → #3) |

> **Mientras el PR #1 no se mergee, `master` no tiene nada de FEAT-001a.** La rama de FEAT-001b sale
> entonces de `feat/FEAT-001a-andamiaje-alta-solicitud`, no de `master`: `b` depende del modelo, del
> servicio de dominio y de la pantalla que entrega `a`.

## Suggested implementation order

a → b → c

`b` y `c` son independientes entre sí: ambos solo necesitan el modelo, el servicio de dominio y la
pantalla que entrega `a`. El orden entre ellos es indistinto.

## Original context

Las solicitudes de vacaciones se gestionan hoy de forma manual: nadie sabe con certeza cuántos días
le quedan a cada empleado en el año, dos períodos del mismo empleado pueden solaparse sin que nada
lo detecte, y el tope anual se controla a ojo.

FEAT-001 nació como el primer ticket ejecutable del sistema, recortado de `docs/daw/prd/PRD.md`
(PRD-001): registrar una solicitud y ver el saldo, con las validaciones que hacen que ese saldo
signifique algo. El repositorio no contenía código .NET, así que arrastraba además el andamiaje
completo — solución, proyectos, `DbContext` y migración inicial.

El control de alcance de la fase DEFINE lo marcó como demasiado grande para un solo ticket: **13
criterios de aceptación** frente a un umbral de 5–7, repartidos en tres módulos
(`GestionVacaciones.Data`, `GestionVacaciones.Web` y `GestionVacaciones.Tests`). Se dividió en tres
sub-tickets, cada uno con su pipeline completo.

**El corte:** `a` es la rebanada vertical que se sostiene sola — el empleado crea una solicitud con
fechas válidas y la ve en su listado. `b` y `c` agregan una regla de negocio cada una sobre un
esqueleto que ya existe.

> ⚠️ **Orden de despliegue.** `a` acepta cualquier cantidad de días y cualquier solapamiento: sus
> reglas de negocio son justamente las que entregan `b` y `c`. No debe llegar a producción sin
> ambos.

## Decisiones cerradas con el usuario

Se tomaron en DEFINE del ticket padre y aplican a los tres sub-tickets:

1. **Identidad sin OAuth:** un único `IEmpleadoActualProvider`. En `Development`, selector sobre la
   nómina sembrada; fuera de `Development`, la implementación lanza excepción en lugar de devolver
   un empleado por defecto. Motivo: el tope y la superposición son reglas por empleado y hay que
   poder verificarlas con varias personas, sin que el sustituto pueda llegar a producción en
   silencio. → FEAT-001a
2. **Listado:** entra solo "mis solicitudes". Los datos de quién autorizó o rechazó (PRD-001
   AC-10.4) quedan para el ticket de aprobación, porque sin ese flujo estarían siempre vacíos.
   → FEAT-001a
3. **Notificaciones fuera de alcance.** PRD-001 RF-07.4 exige los dos canales de forma obligatoria y
   el proveedor de correo sigue declarado como "a confirmar". No se deja abstracción preparada.
   → ticket futuro
4. **Período que cruza el año calendario:** los días se imputan al año al que pertenece cada fecha
   (28-dic a 5-ene = 4 días a un año y 5 al siguiente), y el tope se valida contra cada año
   afectado. Es la lectura fiel a PRD-001 RF-12. → FEAT-001b
5. **Mensaje de saldo insuficiente:** se mantiene el texto literal de PRD-001 AC-05 en el caso
   normal, y se desglosa por año cuando la solicitud abarca dos. → FEAT-001b

## Trazabilidad contra PRD-001

| PRD-001 | Sub-ticket |
|---|---|
| RF-02 (registrar solicitud, días corridos, fechas válidas) | FEAT-001a |
| RF-08 parcial (listado propio: AC-10.1, AC-10.2) | FEAT-001a |
| RF-01 sustituido por el proveedor de identidad | FEAT-001a |
| RF-03 (tope anual de 14 días) | FEAT-001b |
| RF-10, RF-11 (cálculo y visualización del saldo) | FEAT-001b |
| RF-12 parcial (imputación por año que lo hace posible) | FEAT-001b |
| RF-04 (no superposición) | FEAT-001c |
| RF-05, RF-06 (aprobar y rechazar) | fuera de FEAT-001 |
| RF-07.x (notificaciones) | fuera de FEAT-001 |
| RF-09, AC-11 (403 entre empleados) | fuera de FEAT-001 |
