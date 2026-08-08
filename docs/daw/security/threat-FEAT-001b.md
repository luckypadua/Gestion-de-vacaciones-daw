# Modelo de amenazas — FEAT-001b

| Campo | Valor |
|---|---|
| Ticket | FEAT-001b |
| Spec | `docs/daw/specs/spec-FEAT-001b.md` |
| PRD | `docs/daw/prd/prd-FEAT-001b.md` |
| Modelo previo | `docs/daw/security/threat-FEAT-001a.md` — sus riesgos R-01 a R-12 **siguen vigentes** |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §3 |
| Fecha | 2026-08-04 |
| Resultado | **PASSED** |

> **Numeración continua.** Los componentes siguen desde C7 y los riesgos desde R-12, que es donde
> quedó FEAT-001a. Los riesgos se acumulan en el producto, no se reinician con cada ticket: un R-01
> que apareciera dos veces con dos significados distintos volvería inútil cualquier referencia
> cruzada.

---

## 1. Clasificación de los datos (F-TM-05)

| Dato | Clasificación | Novedad de este ticket |
|---|---|---|
| Días disponibles y utilizados de un empleado | **PII** — dato personal laboral | **Nuevo.** No existía como valor calculado ni mostrado |
| `EmpleadoId` asociado a un saldo | **PII** — identifica a la persona | Ya existía; ahora se combina con el saldo |
| Período de una solicitud (`FechaInicio`, `FechaFin`) | **PII** — revela ausencias de una persona | Ya existía (FEAT-001a) |
| Estado de una solicitud | **PII** | Ya existía |
| El tope anual (14) | **Público** — es política de la empresa, no dato de nadie | Nuevo, y deliberadamente no sensible |

**El saldo es PII, y es la novedad que ordena todo este análisis.** «A esta persona le quedan 2 días»
dice algo sobre esa persona: cuánto se ausentó y cuánto puede ausentarse. No es un dato de la
aplicación, es un dato suyo.

### Cifrado exigido (F-TM-07)

| Tramo | Exigencia |
|---|---|
| Navegador → servidor | HTTPS, ya impuesto por `UseHttpsRedirection` y HSTS fuera de `Development` (canalización de FEAT-001a) |
| Host → SQL Server | `Encrypt=True;TrustServerCertificate=False` en la cadena de conexión, sin cambios respecto de FEAT-001a |
| En reposo | **Sin superficie nueva.** El saldo es un valor **derivado**: se calcula y se muestra, no se persiste. No hay columna nueva que cifrar. R-09 de FEAT-001a (PII sin cifrado en reposo, riesgo aceptado) no se agrava |

---

## 2. Fronteras de confianza (F-TM-02)

| # | Frontera | Qué la cruza | Nivel de confianza |
|---|---|---|---|
| **TB-1** a **TB-4** | Las de FEAT-001a | — | Sin cambios |
| **TB-5** | Empleado A → datos del empleado B | Ahora también **el saldo**, vía `SaldoService` | **La frontera se ensancha.** Era listado y ahora es listado + saldo. Sigue aplicándose en `PermisosService`, sede única |
| **TB-6** | Navegador → servidor: el período que el componente del saldo envía | `SaldoDelEmpleado.razor` → `SaldoService` | **Nueva.** El cliente elige *por qué años pregunta*. Es entrada de usuario aunque no lo parezca |

**TB-6 es la frontera nueva de este ticket y merece decirse en voz alta.** Hasta ahora el cliente
solo enviaba un período para crear una solicitud. Con FR-05 el cliente además **pide el saldo de un
conjunto de años**, y un conjunto de años elegido por quien está del otro lado es entrada de usuario
con todas las letras: puede ser enorme, puede ser absurdo, y puede no tener ninguna relación con el
período que se está tipeando.

---

## 3. Análisis STRIDE por componente (F-TM-01)

### C8 — `ImputacionPorAnio` (Block 1)

| | Análisis |
|---|---|
| **S** | Sin identidad involucrada: función pura sobre tres valores. Sin superficie |
| **T** | Sin estado que alterar. `static` sin campos: no hay nada que dos llamadores compartan |
| **R** | No registra ni decide nada atribuible. Sin superficie |
| **I** | No lee ni devuelve datos de nadie: recibe fechas y devuelve un entero |
| **D** | **Sí.** `AniosAbarcados` sobre un período de siglos construye una lista proporcional al rango. Ver **R-15** |
| **E** | Sin privilegios que escalar |

### C9 — `SaldoService` (Block 2)

| | Análisis |
|---|---|
| **S** | El sujeto sale de `IEmpleadoActualProvider`, la sede única de identidad. **No hay parámetro de empleado en ninguna firma**, y esa ausencia es una decisión de seguridad, no de estilo. Ver **R-13** |
| **T** | Solo lee. Sin escritura, no hay estado que corromper |
| **R** | El cálculo no deja rastro. Si un empleado es bloqueado, nada registra contra qué saldo se lo comparó. Ver **R-18** |
| **I** | **La superficie principal del ticket.** Devuelve PII nueva, y esa PII entra en mensajes y potencialmente en logs. Ver **R-13** y **R-14** |
| **D** | La consulta filtra por empleado, estado y rango; el índice del Block 5 la sostiene. Un rango de años arbitrario la degradaría: ver **R-15** |
| **E** | La decisión de visibilidad se le pregunta a `PermisosService`, no se toma acá |

### C10 — `SolicitudesService.CrearAsync` modificado (Block 3)

| | Análisis |
|---|---|
| **S** | Sin cambios: el autor sigue saliendo del proveedor de identidad, antes de mirar las fechas |
| **T** | **La regla nueva es justamente una defensa de integridad**: impide que un empleado se asigne más días de los que le corresponden. Sin transacción, dos envíos simultáneos la esquivan. Ver **R-17** |
| **R** | Un rechazo por tope no queda registrado. Ver **R-18** |
| **I** | El mensaje de rechazo lleva el saldo del empleado y llega a su pantalla — a la suya, que es donde corresponde. El problema es a dónde más llega. Ver **R-14** |
| **D** | La transacción serializable introduce contención donde antes no había ninguna. Ver **R-16** |
| **E** | El tope es la primera regla del producto que **limita** a un empleado. Esquivarla es, en términos de negocio, escalar un privilegio: ver **R-17** |

### C11 — `SaldoDelEmpleado.razor` (Block 4)

| | Análisis |
|---|---|
| **S** | No resuelve identidad. No puede: el token `TimeProvider` está prohibido en todo `.razor` y el sujeto lo pone el servidor |
| **T** | No escribe |
| **R** | No decide nada |
| **I** | Muestra PII **de quien está mirando**, en su propio circuito. Sin `MarkupString`: los números se renderizan escapados como cualquier otro contenido |
| **D** | **Sí, y es TB-6.** El componente pide saldos por año y el período que los determina viene del formulario. Ver **R-15** |
| **E** | No decide la regla: el botón de enviar sigue habilitándose con «están las dos fechas». Mostrar el saldo no lo convierte en juez |

### C12 — Índice y migración (Block 5)

| | Análisis |
|---|---|
| **S** | Sin superficie |
| **T** | Una migración a medio aplicar deja el esquema divergente del modelo. Ver **R-19** |
| **R** | El historial de migraciones **es** el registro del esquema; por eso no se usa `IF NOT EXISTS` |
| **I** | Un índice no expone datos nuevos: reordena el acceso a los que ya están |
| **D** | Al contrario: **el índice es la mitigación** de la degradación de la consulta del saldo |
| **E** | Sin privilegios involucrados. El usuario de base sigue con los permisos de R-07 |

---

## 4. Riesgos, con mitigación o aceptación formal (F-TM-03)

### 🟠 R-13 — HIGH · El saldo de otro empleado, alcanzable por parámetro

**Categoría STRIDE:** Information Disclosure · **Probabilidad:** Media · **Impacto:** Alto

La forma natural de escribir un servicio de saldo es `SaldoDeAsync(int empleadoId, int anio)`. Con
esa firma, cualquier circuito puede preguntar por cualquiera, y la única defensa queda siendo que
todos los llamadores se acuerden de validar antes — que es exactamente la referencia directa a objeto
insegura de siempre. En una aplicación donde el manager va a poder ver a su equipo (ticket futuro),
la tentación de agregar el parámetro «para reusar» es concreta.

**Mitigación (se incorpora a la spec):**

1. **Ninguna firma pública de `SaldoService` acepta un identificador de empleado.** El sujeto sale
   siempre de `IEmpleadoActualProvider`. El día que el manager necesite ver a su equipo, esa
   capacidad se agrega **con** su comprobación en `PermisosService`, no destapando un parámetro.
2. `ExigirPoderVerLasSolicitudesDe` se invoca antes de toda consulta, igual que ya hace
   `ListarPropiasAsync` (`SolicitudesService.cs:301`).
3. Un test verifica que la superficie pública del servicio no expone ninguna sobrecarga con
   `empleadoId`.

### 🟠 R-14 — HIGH · El saldo se filtra por diagnósticos

**Categoría STRIDE:** Information Disclosure · **Probabilidad:** Media · **Impacto:** Alto

El mensaje de AC-02 lleva el saldo. Viaja dentro de `ResultadoDelAlta.MensajeDeError`, y
`ResultadoDelAlta.ToString()` (`SolicitudesService.cs:81-84`) es lo que termina en un log. El
guardarraíl que existe hoy —`DiagnosticoSinPiiTests.cs:174-179`— lee las constantes de
`ErroresDeSolicitud` **por reflexión**, así que ve `"…de {0} días"` y lo aprueba: **el mensaje
compuesto, con el número real, no pasa por ninguna comprobación**. Es el hallazgo #5 del impact scan
y es un agujero de verdad, no teórico: los otros tres defectos de este proyecto con esta forma los
encontraron un `publish`, la cobertura y una mutación, no una lectura.

**Mitigación (se incorpora a la spec):**

1. Los mensajes se componen en **un único lugar**, `ErroresDeSolicitud.ComponerSaldoInsuficiente`,
   y no con `string.Format` disperso.
2. Un test ejecuta el compositor con centinelas y verifica que la salida no contiene nombre, legajo
   ni `EmpleadoId`.
3. **`ResultadoDelAlta.ToString()` no incluye `MensajeDeError`** cuando este proviene del tope. Un
   día concreto de saldo, junto al `EmpleadoId` que ya se registra, identifica a la persona y dice
   cuánto se ausentó.

### 🟠 R-15 — HIGH · Agotamiento por un rango de años arbitrario

**Categoría STRIDE:** Denial of Service · **Probabilidad:** Media · **Impacto:** Medio-Alto

TB-6: el cliente decide por qué años pregunta. `DateOnly` admite hasta el año 9999, así que un
período del año 1 al 9999 hace que `AniosAbarcados` construya una lista de casi diez mil enteros y
que la consulta del saldo abarque un rango que ningún índice acota. Repetido desde varios circuitos
—y los circuitos de Blazor Server **no requieren autenticación** en este producto, que es R-06 de
FEAT-001a— degrada el servidor sin necesidad de credenciales.

**Mitigación (se incorpora a la spec):** el atajo ya especificado en el Block 3 deja de ser una
optimización y pasa a ser un control. **Un período que abarca más de dos años calendario se rechaza
en memoria, sin consultar la base**, porque contiene al menos un año íntegro y 365 días no caben en
un tope de 14. `SaldoService.DeLosAniosAsync` aplica el mismo límite: **como máximo 2 años por
llamada**, y más que eso es `ArgumentOutOfRangeException`, no una consulta cara.

### 🟡 R-16 — MEDIUM · Contención por la transacción serializable

**Categoría STRIDE:** Denial of Service · **Probabilidad:** Baja · **Impacto:** Medio

La mitigación de R-17 introduce el problema: `CrearAsync` pasa de no tener transacción a tener una
serializable. Bajo carga, eso es bloqueo, y en el peor caso deadlocks. NFR-03 pide p95 < 3 s con 50
concurrentes.

**Mitigación (se incorpora a la spec):**

1. La transacción envuelve **solo** leer-el-saldo-y-guardar, no la resolución de identidad ni las
   validaciones de fecha, que ocurren antes y sin tocar la base.
2. El índice del Block 5 mantiene la lectura corta, que es lo que acota la ventana de bloqueo.
3. **El conflicto de serialización se propaga y no se reintenta en bucle.** Un reintento automático
   ante contención amplifica la carga en lugar de aliviarla.

### 🟡 R-17 — MEDIUM · Superar el tope por envíos concurrentes

**Categoría STRIDE:** Tampering / Elevation of Privilege · **Probabilidad:** Media · **Impacto:** Medio

Dos pestañas del mismo empleado envían a la vez: las dos leen el mismo saldo, las dos lo encuentran
suficiente, y las dos se persisten. El resultado es alguien con más de 14 días, que es precisamente
lo que el ticket viene a impedir. **El tope anual no es expresable como check constraint de fila**,
así que la base no puede atraparlo como sí atrapa un período invertido: es la primera invariante de
este producto que la base no puede sostener sola.

**Mitigación (se incorpora a la spec):** transacción con nivel de aislamiento serializable alrededor
de leer-el-saldo-y-guardar, y un test de dos altas concurrentes contra la instancia real que verifica
que la suma no pasa de 14.

### 🟡 R-18 — MEDIUM · Un bloqueo por tope no deja rastro

**Categoría STRIDE:** Repudiation · **Probabilidad:** Alta · **Impacto:** Bajo-Medio

R-05 de FEAT-001a ya registró que ninguna acción es atribuible en este producto. Este ticket agrega
la primera **decisión** del sistema contra un empleado: negarle una solicitud. Si mañana alguien
pregunta «¿por qué no me dejó pedir esos días?», no hay nada que responder.

**Mitigación (se incorpora a la spec):** el rechazo por tope se registra en el log a nivel
information con `EmpleadoId`, el año afectado y los días solicitados — **nunca el mensaje compuesto**
(R-14). Es trazabilidad parcial y no un registro de auditoría; el registro completo sigue siendo
parte de R-05, que continúa aceptado con las condiciones de revisión que fijó FEAT-001a.

### 🟢 R-19 — LOW · Migración a medio aplicar

**Categoría STRIDE:** Tampering · **Probabilidad:** Baja · **Impacto:** Bajo

Una migración que falla a mitad deja el esquema divergente del modelo. `AGENTS.md` ya lo prohíbe
explícitamente en *What NOT to do*.

**Mitigación:** el criterio de cierre del Block 5 exige que la migración **aplique y revierta**
limpiamente, con un test que lo comprueba. Sin `IF NOT EXISTS`.

### Cadena de suministro (W-TM-01)

**Sin superficie nueva.** Este ticket **no agrega ninguna dependencia**: las 8 declaradas en
`AGENTS.md` siguen siendo 8. Todo lo que se construye usa `System.*`, EF Core y MudBlazor, ya
presentes. Un `.csproj` tocado en este ticket es motivo para revisar por qué.

---

## 5. Mitigaciones que la spec debe incorporar

1. **R-13** — ninguna firma pública de `SaldoService` acepta `empleadoId`; el sujeto sale del
   proveedor de identidad. Test que verifica la superficie pública.
2. **R-13** — `ExigirPoderVerLasSolicitudesDe` antes de toda consulta de saldo.
3. **R-14** — compositor único de los mensajes, con test de centinelas.
4. **R-14** — `ResultadoDelAlta.ToString()` no incluye el mensaje cuando lleva un saldo.
5. **R-15** — `DeLosAniosAsync` acepta **como máximo 2 años**; más es
   `ArgumentOutOfRangeException`.
6. **R-15** — período de más de dos años rechazado en memoria, sin consultar.
7. **R-16** — la transacción envuelve solo leer-y-guardar; sin reintento automático.
8. **R-17** — transacción serializable + test de concurrencia.
9. **R-18** — log del rechazo por tope con `EmpleadoId` y año, sin el mensaje compuesto.
10. **R-19** — la migración aplica y revierte, con test.

---

## 6. Resumen

| | |
|---|---|
| Superficies de ataque identificadas | 5 componentes (C8–C12) |
| Fronteras de confianza | TB-5 ensanchada, **TB-6 nueva** |
| Riesgos | 🔴 0 · 🟠 3 · 🟡 3 · 🟢 1 |
| Riesgos aceptados formalmente | **0 nuevos.** R-05 y R-09 de FEAT-001a siguen aceptados con sus condiciones |
| Dependencias nuevas | 0 |
| Mitigaciones a incorporar | 10 |

**Resultado: PASSED.** Los tres riesgos HIGH tienen mitigación concreta, y ninguna exige aceptación
formal, así que no hay nada que firmar. Lo que este ticket enseña sobre sí mismo cabe en una línea:
**el saldo es un dato personal, y la primera regla que le dice «no» a un empleado es también la
primera que la base de datos no puede sostener sola.**
