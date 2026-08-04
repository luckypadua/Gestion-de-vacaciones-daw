# PRD FEAT-001b: Imputación de días por año calendario, tope anual y saldo

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| Tracker | none |
| Date | 2026-08-01 |
| PRD loops | 1 |

> **Sub-ticket `b` de FEAT-001** (índice en `prd-FEAT-001.md`), que a su vez recorta
> `docs/daw/prd/PRD.md` (PRD-001). Los identificadores `RF-xx` / `AC-xx` citados entre paréntesis
> son los del PRD-001; los `FR-xx` / `AC-xx` de este archivo son los de este sub-ticket.

## Context and Problem

Con FEAT-001a un empleado ya registra solicitudes y las ve listadas, pero nada lo detiene: puede
pedir 300 días. El tope anual de 14 días se sigue controlando a ojo, que es el problema que el
producto vino a resolver, y el empleado no tiene forma de saber cuántos días le quedan sin
preguntarle a RRHH.

Este sub-ticket entrega la regla que convierte el registro en control: el tope anual, el saldo que
lo hace visible, y la imputación por año calendario que hace que ambos signifiquen lo mismo.

**El caso que obliga a decidir:** un período del 28 de diciembre al 5 de enero cae en dos años. El
PRD-001 fija un tope "del año en curso" (RF-03) y un reinicio del saldo cada 1 de enero (RF-12),
pero no dice contra qué año cuenta un período que cae en los dos. La decisión tomada en DEFINE es
imputar a cada año los días que le pertenecen, por ser la lectura fiel a RF-12; el precio es que el
mensaje de saldo insuficiente deja de poder expresarse con un único número.

**Persona:** el **Empleado**, que necesita saber cuántos días le quedan y que el sistema no lo deje
pedir más de los que tiene.

## Goals

- Que el tope anual lo haga cumplir el sistema y no una persona.
- Que el empleado vea su saldo sin depender de RRHH.
- Que un período a caballo de dos años consuma de cada año lo que le corresponde.
- Que el tope y la regla de cálculo vivan en un solo lugar, para poder adaptarlos si cambia la
  normativa.

## Functional Requirements

- FR-01: El sistema debe imputar los días de una solicitud al año calendario al que pertenece cada
  fecha del período. *(decisión del ticket, coherente con PRD-001 RF-12)*
- FR-02: El sistema debe validar que los días solicitados, sumados a los días tomados, aprobados y
  pendientes del empleado, no superen el tope de 14 días en ninguno de los años calendario
  afectados por el período. *(PRD-001 RF-03)*
- FR-03: El sistema debe calcular el saldo de días disponibles de un empleado en un año calendario
  como 14 menos la suma de los días tomados, aprobados y pendientes imputados a ese año.
  *(PRD-001 RF-10)*
- FR-04: El sistema debe mostrar al empleado los días utilizados o reservados y los días disponibles
  del año en curso. *(PRD-001 RF-11, AC-10.3)*

## Non-Functional Requirements

- NFR-01: El tope anual de 14 días y las reglas de cálculo del saldo deben residir en 1 único punto
  del código: cambiar el tope debe requerir modificar exactamente 1 declaración.
- NFR-02: La cobertura de líneas, ramas y funciones sobre el código nuevo debe ser mayor o igual al
  80%.
- NFR-03: Validar el tope al enviar una solicitud y mostrar el saldo deben responder en menos de 3
  segundos en el percentil 95 (p95), bajo una carga concurrente de al menos 50 usuarios.
  *(PRD-001 RNF-04)*
- NFR-04: El saldo mostrado al empleado y el saldo usado para bloquear una solicitud deben provenir
  de 1 única función de cálculo: 0 implementaciones alternativas.

## Acceptance Criteria

- AC-01: WHEN el período de una solicitud abarca fechas de dos años calendario, THE sistema SHALL
  imputar a cada año únicamente los días del período que pertenecen a ese año. Ejemplo: del
  28-dic-2026 al 5-ene-2027 son 9 días corridos, imputados como 4 días a 2026 y 5 días a 2027.
  *(FR-01)*
- AC-02: IF los días solicitados, sumados a los días tomados, aprobados y pendientes del empleado,
  superan 14 en un único año calendario, THEN THE sistema SHALL impedir el envío y mostrar el
  mensaje "No dispones de días suficientes. Tu saldo actual es de X días", donde X es el saldo de
  ese año. *(FR-02)*
- AC-03: IF el período abarca dos años calendario y supera el tope de 14 días en alguno de ellos,
  THEN THE sistema SHALL impedir el envío y mostrar el mensaje desglosado "No dispones de días
  suficientes. Tu saldo actual es de X días en {año1} y de Y días en {año2}". *(FR-02)*
- AC-04: THE sistema SHALL calcular el saldo de un empleado en un año calendario como 14 menos la
  suma de los días tomados, aprobados y pendientes imputados a ese año. *(FR-03)*
- AC-05: WHEN el empleado entra a la pantalla de sus solicitudes, THE sistema SHALL mostrar los días
  utilizados o reservados y los días disponibles del año en curso. *(FR-04)*

## Out of Scope

- **La validación de superposición entre períodos.** La entrega FEAT-001c.
- **El andamiaje, el modelo de datos y la identidad del empleado.** Los entrega FEAT-001a, que es
  requisito de este sub-ticket.
- **PRD-001 RF-12 y AC-12 (reinicio del saldo el 1 de enero).** FR-01 y FR-03 dejan el cálculo
  imputado por año, que es lo que hace posible ese reinicio, pero verificar el cambio de año no
  entra aquí.
- **Ampliar el tope anual o aplicar reglas según convenios colectivos.** Fuera del alcance del
  producto.
- **Prorrateo del cupo anual según la fecha de ingreso.** Fuera del alcance del producto.
- **Arrastre de días no utilizados al año siguiente.** Fuera del alcance del producto.
- **PRD-001 RF-05, RF-06 y RF-07.x (aprobar, rechazar y notificar).** Tickets futuros.

## Risks and Mitigations

- **El saldo mostrado no coincide con el que bloqueó la solicitud**, porque la imputación por año se
  aplica en un cálculo y no en el otro → mitigación: NFR-01 y NFR-04 confinan tope y cálculo a un
  único punto consumido por ambos caminos, y AC-01 fija el ejemplo numérico que los tests deben
  reproducir.
- **El caso del cruce de año no se prueba** por ser infrecuente, y falla recién el 28 de diciembre
  → mitigación: AC-01 y AC-03 lo describen con fechas concretas, lo que obliga a un test que no
  depende de la fecha real de ejecución.
- **El tope queda escrito como el número 14 disperso por el código** y cambiar la normativa obliga a
  buscarlo → mitigación: NFR-01 exige una única declaración.
- **Las solicitudes pendientes no reservan saldo**, permitiendo sobreasignación mientras esperan
  resolución → mitigación: FR-02 y FR-03 cuentan explícitamente los días pendientes, igual que los
  aprobados.
- **El cálculo depende de la fecha del sistema** y los tests se vuelven frágiles en fin de año →
  mitigación: el año en curso y la fecha actual se obtienen de una abstracción de tiempo
  sustituible en los tests.

## Dependencies

- **FEAT-001a** (`prd-FEAT-001a.md`) — aporta la entidad `Solicitud`, el servicio de dominio, la
  pantalla del empleado y el proveedor de identidad sobre los que este sub-ticket agrega la regla.
  Es requisito previo.
- **`docs/daw/prd/PRD.md` (PRD-001)** — PRD de producto. El texto literal de AC-02 proviene de su
  criterio AC-05; el tope de 14 días, de su RF-03. AC-03 es ese mismo mensaje desglosado por año,
  para el caso de dos años que el PRD-001 no contempla: por eso conserva su redacción palabra por
  palabra hasta «Tu saldo actual es de X días».
- **`docs/daw/prd/prd-FEAT-001.md`** — PRD padre e índice de la división.
- **SQL Server 2022 + Entity Framework Core 10** — consulta de los días ya imputados por año.
  Declarado en `AGENTS.md` → Stack.
- **Blazor Server + MudBlazor** — visualización del saldo y de los mensajes de bloqueo. Declarado en
  `AGENTS.md` → Stack.
