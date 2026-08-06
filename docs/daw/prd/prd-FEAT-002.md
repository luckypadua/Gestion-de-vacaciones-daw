# PRD FEAT-002: Aprobación y rechazo de solicitudes por el manager

| Field | Value |
|-------|-------|
| Ticket | FEAT-002 |
| Tracker | none |
| Date | 2026-08-06 |
| PRD loops | 0 |

> Recorta `docs/daw/prd/PRD.md` (PRD-001): RF-05, RF-06, RF-09 y la parte de RF-08 que hoy no se
> puede verificar porque nada produce todavía los estados `Aprobada`/`Rechazada`. Los identificadores
> `RF-xx`/`AC-xx` citados entre paréntesis son los de PRD-001; los `FR-xx`/`AC-xx` de este archivo
> son los de este ticket.

## Context and Problem

Con FEAT-001a/b/c el empleado registra solicitudes, ve su saldo y no puede superponer fechas ni
pasarse del tope. Pero toda solicitud nace y permanece en `Pendiente` para siempre: nada en el
sistema puede aprobarla ni rechazarla. El manager —la persona que PRD-001 define como responsable de
esa decisión— no tiene ninguna pantalla, y el campo `Empleado.ManagerId`/`DesignadoId` que FEAT-001a
ya sembró no lo consume nadie todavía.

Sin este ticket, el sistema registra solicitudes que nunca se resuelven: es la mitad de un flujo.

**Personas:**
- **Manager:** aprueba o rechaza las solicitudes pendientes de las personas a su cargo.
- **Designado:** sobre el equipo del manager que lo delega, tiene exactamente las mismas
  capacidades que él.
- **Empleado:** ya puede solicitar (FEAT-001a); ahora además puede ver, en sus propias solicitudes
  resueltas, quién decidió y por qué.

## Goals

- Que una solicitud `Pendiente` pueda llegar a `Aprobada` o `Rechazada`, cerrando el flujo que hoy
  queda abierto para siempre.
- Que el manager (o su designado) tenga dónde encontrar el trabajo pendiente de su equipo, sin
  depender de que alguien se lo avise.
- Que quede registrado quién resolvió cada solicitud, cuándo, y con qué motivo si fue un rechazo.
- Que el acceso de escritura (aprobar/rechazar) respete la misma frontera de visibilidad que ya
  rige el acceso de lectura: nadie actúa sobre un equipo que no es el suyo.

## Functional Requirements

- FR-01: El sistema debe permitir que el manager (o su designado) apruebe una solicitud en estado
  "Pendiente" de un empleado a su cargo. *(PRD-001 RF-05)*
- FR-02: El sistema debe permitir que el manager (o su designado) rechace una solicitud en estado
  "Pendiente" de un empleado a su cargo, exigiendo un motivo no vacío. *(PRD-001 RF-06)*
- FR-03: El sistema debe impedir aprobar o rechazar una solicitud que no está en estado
  "Pendiente", con independencia de quién lo intente. *(refuerzo, decidido en DEFINE para el caso
  de dos resoluciones casi simultáneas o una pantalla desactualizada)*
- FR-04: El sistema debe registrar, al resolver una solicitud, quién la resolvió (el manager o el
  designado), cuándo, y el motivo cuando el resultado fue un rechazo. *(PRD-001 RF-08, AC-08.3,
  AC-09.3, AC-10.4)*
- FR-05: El sistema debe mostrarle al manager (o a su designado) el listado de las solicitudes en
  estado "Pendiente" de las personas a su cargo. *(necesario para que RF-01/FR-02 sean alcanzables
  sin las notificaciones de PRD-001 RF-07, diferidas a un ticket aparte)*
- FR-06: El sistema debe restringir tanto la visualización como la resolución de las solicitudes de
  un empleado únicamente a ese empleado, a su manager y al designado de su manager. *(PRD-001 RF-09)*

## Non-Functional Requirements

- NFR-01: La cobertura de líneas, ramas y funciones sobre el código nuevo debe ser mayor o igual al
  80%.
- NFR-02: Aprobar o rechazar una solicitud debe responder en menos de 3 segundos en el percentil 95
  (p95), bajo una carga concurrente de al menos 50 usuarios. *(PRD-001 RNF-04, extendido a estas dos
  operaciones)*
- NFR-03: La consulta del listado de pendientes del manager debe apoyarse en índices, para no
  escanear la tabla de empleados ni la de solicitudes completas al crecer la nómina o el historial.

## Acceptance Criteria

- AC-01: WHEN el manager (o su designado) aprueba una solicitud "Pendiente" de un empleado a su
  cargo, THE sistema SHALL cambiar su estado a "Aprobada". *(FR-01)*
- AC-02: WHEN el manager (o su designado) rechaza una solicitud "Pendiente" de un empleado a su
  cargo indicando un motivo, THE sistema SHALL cambiar su estado a "Rechazada" y guardar ese motivo.
  *(FR-02)*
- AC-03: IF el manager intenta rechazar una solicitud sin indicar un motivo, THEN THE sistema SHALL
  impedir el rechazo y mostrar el mensaje "Indicá el motivo del rechazo". *(FR-02)*
- AC-04: IF alguien intenta aprobar o rechazar una solicitud que ya no está en estado "Pendiente",
  THEN THE sistema SHALL impedir la acción y mostrar el mensaje "Esta solicitud ya fue resuelta".
  *(FR-03)*
- AC-05: WHEN una solicitud pasa a "Aprobada" o "Rechazada", THE sistema SHALL registrar quién la
  resolvió y en qué momento. *(FR-04)*
- AC-06: WHEN un empleado visualiza una solicitud propia ya resuelta, THE sistema SHALL mostrar
  quién la aprobó o rechazó y, si fue un rechazo, el motivo. *(FR-04)*
- AC-07: WHEN el manager (o su designado) visualiza la pantalla de autorizaciones, THE sistema
  SHALL mostrar las solicitudes en estado "Pendiente" de los empleados a su cargo. *(FR-05)*
- AC-08: IF un empleado sin relación de manager ni de designado sobre otro intenta ver o resolver
  una solicitud que no es propia, THEN THE sistema SHALL denegar el acceso. *(FR-06)*

## Out of Scope

- **Notificaciones** (PRD-001 RF-07.1 a RF-07.4, in-app y correo electrónico) — el proveedor de
  correo sigue "a confirmar" en las Dependencias de PRD-001; se resuelven en un ticket propio.
- **Editar o revertir** una resolución ya confirmada (deshacer una aprobación o un rechazo).
- **Delegar o cambiar quién es el manager o el designado** de un empleado — PRD-001 ya lo declara
  administrado externamente, y este ticket no lo toca.
- **Un historial de auditoría más allá de quién/cuándo/motivo** (por ejemplo, un registro de
  intentos fallidos o de cambios de decisión) — solo el resultado final de la resolución.
- **Reportes o métricas agregadas** del equipo (cuántos días tomó cada persona, tendencias, etc.).
- **Que un manager sea a la vez designado de otro manager y opere sobre dos equipos desde la misma
  pantalla en un único paso** — cada relación se resuelve por separado; no hay una vista combinada
  de "todos los equipos que administro".

## Risks and Mitigations

- **Rechazo sin motivo** → mitigación: AC-03, validado en el servidor (no en el cliente, mismo
  criterio que el resto del dominio).
- **Dos resoluciones casi simultáneas de la misma solicitud** (el manager y el designado actuando a
  la vez, o una pantalla desactualizada con doble clic) → mitigación: FR-03/AC-04, más una operación
  atómica de lectura-y-escritura análoga a la que FEAT-001b/c ya usan para el tope y la
  superposición.
- **Acceso indebido a solicitudes de un equipo ajeno**, ahora también en el camino de escritura →
  mitigación: FR-06/AC-08, extendiendo `PermisosService` — la sede única que PRD-001 y FEAT-001a ya
  establecieron — en vez de reimplementar el control en otro lugar.
- **El listado de pendientes se degrada con el crecimiento de la nómina o del historial** →
  mitigación: NFR-03, índices dedicados.

## Dependencies

- **FEAT-001a** (`prd-FEAT-001a.md`) — aporta `Empleado.ManagerId`/`DesignadoId`, la entidad
  `Solicitud`, `SolicitudesService` y `PermisosService` sobre los que este ticket agrega la
  resolución. Es requisito previo.
- **FEAT-001b y FEAT-001c** — no hay dependencia funcional directa (el tope y la superposición no
  cambian de comportamiento al aprobar o rechazar), pero el código convive en la misma rama:
  `SolicitudesService` ya tiene las dependencias que esos dos tickets le agregaron.
- **`docs/daw/prd/PRD.md` (PRD-001)** — PRD de producto. RF-05, RF-06, RF-09 y la parte verificable
  de RF-08 provienen de ahí.
- **`docs/daw/prd/prd-FEAT-001.md`** — PRD padre e índice de la división original de FEAT-001 (no
  incluye a FEAT-002, que es un ticket nuevo, no un cuarto sub-ticket).
- **SQL Server 2022 + Entity Framework Core 10** — nuevas columnas para registrar quién resolvió,
  cuándo y el motivo; nuevos índices de NFR-03. Declarado en `AGENTS.md` → Stack.
- **Blazor Server + MudBlazor** — la pantalla nueva de autorizaciones del manager. Declarado en
  `AGENTS.md` → Stack.
