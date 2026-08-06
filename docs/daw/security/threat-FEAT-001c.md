# Modelo de amenazas — FEAT-001c

| Campo | Valor |
|---|---|
| Ticket | FEAT-001c |
| Spec | `docs/daw/specs/spec-FEAT-001c.md` |
| PRD | `docs/daw/prd/prd-FEAT-001c.md` |
| Modelo previo | `docs/daw/security/threat-FEAT-001b.md` — sus riesgos R-01 a R-19 **siguen vigentes** |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §3 |
| Fecha | 2026-08-06 |
| Resultado | **PASSED** |

> **Numeración continua.** Los componentes siguen desde C13 y los riesgos desde R-20, que es donde
> quedó FEAT-001b.

---

## 1. Clasificación de los datos (F-TM-05)

| Dato | Clasificación | Novedad de este ticket |
|---|---|---|
| Período de la solicitud que se está creando (`FechaInicio`, `FechaFin`) | **PII** — ya clasificado en FEAT-001a | Sin cambios |
| Períodos de otras solicitudes del mismo empleado, leídos para comparar solapamiento | **PII** — revela ausencias de esa misma persona | **Nuevo dato leído**, pero nunca sale del servidor: no viaja a la interfaz, no se loguea, no aparece en el mensaje de rechazo |
| El literal `SuperposicionDePeriodo` | **Público** — mensaje fijo del PRD, igual para cualquier empleado | Nuevo, deliberadamente no sensible |
| El conjunto `EstadosDeSolicitud.Vigentes` (`{Pendiente, Aprobada}`) | **Público** — regla de negocio, no dato de nadie | Nuevo, extraído de `SaldoService` |

**El dato nuevo que este ticket lee —los períodos de otras solicitudes del mismo empleado— nunca se
expone.** El PRD lo dice explícitamente en su "Out of Scope": *"Avisar al empleado de cuál es la
solicitud que se solapa. El mensaje de AC-01 es el literal fijado por el PRD-001 y no incluye ese
detalle."* El diseño lo cumple por construcción: la consulta de solapamiento solo se usa para decidir
`true`/`false`, sus resultados nunca se interpolan en el mensaje ni en ningún log.

### Cifrado exigido (F-TM-07)

| Tramo | Exigencia |
|---|---|
| Navegador → servidor | Sin cambios respecto de FEAT-001a/b |
| Host → SQL Server | Sin cambios respecto de FEAT-001a/b |
| En reposo | **Sin superficie nueva.** Este ticket no persiste ningún dato nuevo — ni columna, ni tabla. Es una regla de lectura-antes-de-escribir sobre datos que ya existían |

---

## 2. Fronteras de confianza (F-TM-02)

| # | Frontera | Qué la cruza | Nivel de confianza |
|---|---|---|---|
| **TB-1** a **TB-6** | Las de FEAT-001a/b | — | Sin cambios |

**Ninguna frontera nueva.** El período de la nueva solicitud ya cruzaba TB-1 (navegador → servidor)
desde FEAT-001a; este ticket no agrega ningún dato nuevo que el cliente envíe, ni ningún endpoint
nuevo. El único cliente de la superposición es `SolicitudesService.CrearAsync`, que ya existía.

---

## 3. Análisis STRIDE por componente (F-TM-01)

### C13 — `SolicitudesService.CrearAsync` modificado, otra vez (Bloque único)

| | Análisis |
|---|---|
| **S** | Sin superficie nueva. El autor sigue saliendo de `IEmpleadoActualProvider`; la consulta de solapamiento filtra `EmpleadoId == autor`, nunca un parámetro (mismo patrón que R-13 de FEAT-001b) |
| **T** | El cliente no puede alterar el resultado de la comparación de fechas: corre enteramente en el servidor, sobre datos ya persistidos. El botón de enviar del formulario sigue sin decidir nada (R-10, sin tocar) |
| **R** | **Ver R-20.** El rechazo por superposición no queda registrado en ningún log, a diferencia del rechazo por tope (R-18) |
| **I** | La consulta trae `FechaInicio`/`FechaFin` de otras solicitudes del mismo empleado, pero esos valores nunca salen del método: solo alimentan la comparación booleana. Confirmar con un test dedicado que el mensaje de rechazo no lleva fechas (ya lo exige la spec) |
| **D** | La consulta nueva tiene la misma forma que la que `SaldoService` ya ejecuta contra el mismo índice — no agrega un patrón de acceso nuevo que pueda degradarse distinto. **Ver R-21** sobre el mecanismo de reintento |
| **E** | Sin privilegios involucrados. Mismo usuario de base que el resto del dominio |

### C14 — El reintento dirigido ante conflicto de serialización (decisión de PLAN)

Componente nuevo de este ticket, sin equivalente en FEAT-001b: al capturar un conflicto de
serialización de SQL Server (error 1205) durante `CrearAsync`, se vuelve a ejecutar **solo** la
consulta de solapamiento, en una transacción nueva, para decidir si el conflicto correspondía a una
superposición real.

| | Análisis |
|---|---|
| **S** | Sin superficie — el disparador es un código de error del motor, no algo que el cliente controle |
| **T** | Sin superficie — el reintento no vuelve a evaluar ninguna entrada del usuario, solo relee el estado ya persistido |
| **R** | **Ver R-20** — si el reintento convierte la excepción en un rechazo prolijo, esa conversión también debería quedar registrada, para poder distinguir "se detectó de entrada" de "se detectó por reintento" si el patrón empieza a aparecer seguido (indicaría contención real que vale la pena investigar) |
| **I** | Sin superficie nueva — la segunda consulta es la misma consulta, con el mismo alcance |
| **D** | **Ver R-21** — es un reintento único y dirigido, no un bucle: acotado a un viaje extra a la base por conflicto, y nunca amplifica hacia otros empleados (el filtro sigue siendo `EmpleadoId == autor`) |
| **E** | Sin privilegios involucrados |

---

## 4. Riesgos identificados

### 🟡 R-20 — MEDIUM · El rechazo por superposición no queda registrado

**Categoría STRIDE:** Repudiation · **Probabilidad:** Alta (se dispara en cada rechazo por AC-01) ·
**Impacto:** Medio

A diferencia del rechazo por tope (R-18 de FEAT-001b, que registra `EmpleadoId`, año y días en un
log de nivel Information), el diseño original de este ticket no preveía ningún registro para el
rechazo por superposición. Es la primera decisión de bloqueo de este ticket y, con el mecanismo de
reintento (C14), hay además dos caminos distintos por los que se puede llegar al mismo rechazo —
detectado de entrada, o detectado tras un conflicto de serialización—, y sin log no hay forma de
distinguir cuál ocurrió ni con qué frecuencia.

**Mitigación:** un `_registro.LogInformation(...)` en el punto de rechazo, con `EmpleadoId` y un
indicador de si vino del camino directo o del reintento — nunca fechas, nunca el identificador de la
otra solicitud (que el PRD marca fuera de alcance). Mismo nivel y mismo criterio de qué se loguea que
R-18. **Se fold ea en la spec.**

### 🟢 R-21 — LOW · El reintento dirigido, bajo envío hostil sostenido

**Categoría STRIDE:** Denial of Service · **Probabilidad:** Baja · **Impacto:** Bajo

Un empleado que enviara solicitudes solapadas de forma repetida y deliberada podría disparar el
mecanismo de reintento de C14 muchas veces, agregando un viaje extra a la base por cada conflicto de
serialización real. **Aceptado sin mitigación adicional** por tres razones: (1) es un reintento único,
no un bucle — no hay amplificación por evento; (2) el filtro sigue siendo `EmpleadoId == autor`, así
que el efecto queda acotado a las propias transacciones del atacante, nunca a las de otro empleado; y
(3) el mismo patrón (transacción serializable sin reintento en bucle) ya se aceptó para el tope en
R-16 de FEAT-001b, con el mismo razonamiento de que reintentar en bucle amplificaría la carga bajo
contención en vez de aliviarla — acá el reintento único es la versión más conservadora posible de esa
misma regla.

**Revisar si:** el log de R-20 muestra que el camino de reintento se dispara con una frecuencia que
sugiera abuso deliberado, no solo contención legítima.

---

## 5. Riesgos de FEAT-001a/b confirmados sin cambios

1. **R-10** — la validación vive en el servidor; el botón de enviar no decide.
2. **R-13** — ninguna firma pública acepta un `empleadoId`; el sujeto sale siempre del proveedor de
   identidad. La consulta de solapamiento no rompe este patrón.
3. **R-16** — los conflictos de motor no se convierten en rechazo sin verificación (el reintento de
   C14 es exactamente esa verificación: confirma que el conflicto ERA una superposición antes de
   convertirlo).

---

## 6. Resumen

```
┌─────────────────────────────────────────────────────────┐
│  /daw-threat-modeling FEAT-001c — PASSED                 │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Attack surfaces identified: 2 (C13, C14)                │
│  Trust boundaries declared: 0 nuevas (las 6 de a/b)      │
│                                                          │
│  Risks:                                                  │
│    🟡 MEDIUM: R-20 — rechazo por superposición sin log   │
│       — Mitigation: log Information con EmpleadoId y     │
│         camino (directo/reintento), foldeado en la spec  │
│    🟢 LOW: R-21 — reintento bajo envío hostil sostenido  │
│       — Aceptado: reintento único, acotado al propio     │
│         empleado, mismo razonamiento que R-16             │
│                                                          │
│  Mitigations to fold into the spec:                      │
│    1. Log de R-20 en el punto de rechazo por             │
│       superposición (ambos caminos)                       │
│                                                          │
│  ─────────────────────────────────────────────────────   │
│  Risks: C:0 H:0 M:1 L:1                                   │
│  Report: docs/daw/security/threat-FEAT-001c.md            │
└─────────────────────────────────────────────────────────┘
```
