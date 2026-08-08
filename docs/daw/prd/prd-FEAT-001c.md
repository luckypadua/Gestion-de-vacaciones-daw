# PRD FEAT-001c: No superposición de períodos

| Field | Value |
|-------|-------|
| Ticket | FEAT-001c |
| Tracker | none |
| Date | 2026-08-01 |
| PRD loops | 0 |

> **Sub-ticket `c` de FEAT-001** (índice en `prd-FEAT-001.md`), que a su vez recorta
> `docs/daw/prd/PRD.md` (PRD-001). Los identificadores `RF-xx` / `AC-xx` citados entre paréntesis
> son los del PRD-001; los `FR-xx` / `AC-xx` de este archivo son los de este sub-ticket.

## Context and Problem

Con FEAT-001a el empleado registra solicitudes y con FEAT-001b no puede pedir más días de los que
tiene. Falta la tercera regla del PRD-001: nada impide todavía que el mismo empleado tenga dos
períodos solapados. Hoy eso se detecta a ojo, o no se detecta.

La superposición no es un problema de saldo — dos solicitudes solapadas pueden sumar menos de 14
días y ser igualmente inválidas: describen a la misma persona ausente dos veces por el mismo motivo
en las mismas fechas, y la planificación de recursos que el producto persigue deja de tener sentido.

Este sub-ticket es una única regla de negocio sobre un esqueleto que ya existe. Su dificultad no
está en el caso obvio sino en los bordes: qué pasa con períodos contiguos y qué pasa con las
solicitudes ya rechazadas.

**Persona:** el **Empleado**, que no debe poder reservar dos veces las mismas fechas.

## Goals

- Que un empleado no pueda tener dos solicitudes vigentes sobre las mismas fechas.
- Que las solicitudes rechazadas no bloqueen fechas que quedaron libres.
- Que dos períodos consecutivos sigan siendo posibles.

## Functional Requirements

- FR-01: El sistema debe impedir la creación de una solicitud cuyo período se superponga, total o
  parcialmente, con otra solicitud del mismo empleado en estado "Pendiente" o "Aprobada".
  *(PRD-001 RF-04)*
- FR-02: El sistema debe verificar la superposición y persistir la solicitud dentro de una única
  operación atómica, de modo que dos envíos simultáneos del mismo empleado no puedan producir dos
  períodos solapados. *(refuerzo de PRD-001 RF-04)*

## Non-Functional Requirements

- NFR-01: La verificación de superposición y la inserción de la solicitud deben ocurrir dentro de 1
  única transacción: 0 ventanas entre la lectura y la escritura en las que otra solicitud del mismo
  empleado pueda insertarse.
- NFR-02: La cobertura de líneas, ramas y funciones sobre el código nuevo debe ser mayor o igual al
  80%.
- NFR-03: La verificación de superposición al enviar una solicitud debe responder en menos de 3
  segundos en el percentil 95 (p95), bajo una carga concurrente de al menos 50 usuarios.
  *(PRD-001 RNF-04)*
- NFR-04: La consulta de solicitudes solapadas debe apoyarse en 1 índice sobre el empleado y las
  fechas del período, para no degradarse con el crecimiento del historial.

## Acceptance Criteria

- AC-01: IF el período de la nueva solicitud se superpone, total o parcialmente, con el de otra
  solicitud del mismo empleado en estado "Pendiente" o "Aprobada", THEN THE sistema SHALL impedir la
  creación y mostrar el mensaje "Ya tenés una solicitud que se superpone con estas fechas".
  *(FR-01)*
- AC-02: WHEN el período de la nueva solicitud comienza el día siguiente a la fecha de fin de otra
  solicitud del mismo empleado, THE sistema SHALL permitir la creación. *(FR-01)*
- AC-03: WHILE la única solicitud del empleado que coincide en fechas está en estado "Rechazada",
  WHEN el empleado envía la nueva solicitud, THE sistema SHALL permitir la creación. *(FR-01)*
- AC-04: IF dos solicitudes del mismo empleado con períodos solapados se envían de forma
  concurrente, THEN THE sistema SHALL persistir exactamente 1 de las dos y rechazar la otra con el
  mensaje de superposición. *(FR-02)*

## Out of Scope

- **La superposición entre solicitudes de empleados distintos.** Dos personas del mismo equipo
  ausentes a la vez es una decisión del manager, no una invariante del sistema.
- **El andamiaje, el modelo de datos y la identidad del empleado.** Los entrega FEAT-001a, que es
  requisito de este sub-ticket.
- **El tope anual y el saldo.** Los entrega FEAT-001b.
- **PRD-001 RF-05 y RF-06 (aprobar y rechazar).** Este sub-ticket lee el estado "Aprobada" y el
  estado "Rechazada", pero no los produce: los entrega el ticket de aprobación.
- **Cancelar o editar una solicitud enviada** para liberar fechas. Fuera del alcance del producto.
- **Avisar al empleado de cuál es la solicitud que se solapa.** El mensaje de AC-01 es el literal
  fijado por el PRD-001 y no incluye ese detalle.

## Risks and Mitigations

- **Los estados "Aprobada" y "Rechazada" no se pueden producir todavía**, porque el flujo de
  aprobación está fuera de alcance, y AC-01 y AC-03 quedan sin verificar → mitigación: los tests
  siembran esas filas directamente en el repositorio, sin pasar por la interfaz.
- **El borde de la contigüidad se implementa mal** y dos períodos consecutivos se rechazan como
  solapados, o dos períodos que comparten un día se aceptan → mitigación: AC-02 fija el caso
  contiguo de forma explícita, en lugar de dejarlo a la interpretación del operador de comparación.
- **Dos envíos simultáneos del mismo empleado en dos pestañas** esquivan la validación por leerse
  antes de que ninguno haya escrito → mitigación: FR-02 y NFR-01 exigen lectura y escritura en una
  sola transacción, y AC-04 lo verifica.
- **La consulta de solapamiento recorre todo el historial** del empleado y se degrada con los años
  → mitigación: NFR-04 exige el índice por empleado y fechas.

## Dependencies

- **FEAT-001a** (`prd-FEAT-001a.md`) — aporta la entidad `Solicitud` con su período y su estado, el
  servicio de dominio y la pantalla de alta sobre los que este sub-ticket agrega la regla. Es
  requisito previo.
- **`docs/daw/prd/PRD.md` (PRD-001)** — PRD de producto. El texto literal de AC-01 proviene de su
  criterio AC-06.
- **`docs/daw/prd/prd-FEAT-001.md`** — PRD padre e índice de la división.
- **SQL Server 2022 + Entity Framework Core 10** — consulta de solapamiento, índice de NFR-04 y
  transacción de NFR-01. Declarado en `AGENTS.md` → Stack.
- **Blazor Server + MudBlazor** — presentación del mensaje de bloqueo. Declarado en `AGENTS.md` →
  Stack.
