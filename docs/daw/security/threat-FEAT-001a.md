# Modelo de amenazas — FEAT-001a

| Campo | Valor |
|---|---|
| Ticket | FEAT-001a |
| Tier | FEATURE |
| Fecha | 2026-08-01 |
| PRD | `docs/daw/prd/prd-FEAT-001a.md` |
| Diseño analizado | 6 bloques, Blazor Server + MudBlazor + EF Core 10 + SQL Server 2022 |

> **Premisa que domina todo el análisis:** este sub-ticket construye una aplicación **sin
> autenticación y sin autorización entre empleados**. PRD-001 RF-01 (OAuth), RF-09 y AC-11 (HTTP 403)
> están fuera de alcance por decisión del usuario. La identidad se elige desde un desplegable, sin
> credencial. Todo lo que sigue parte de ese hecho, no lo descubre.

---

## 1. Clasificación de los datos (F-TM-05)

| Dato | Clasificación | Dónde vive |
|---|---|---|
| Nombre del empleado | **PII** | `Empleado.Nombre`, listado del selector |
| Correo corporativo | **PII** | `Empleado.Correo`, sembrado por `SeedDatos` |
| Relación manager / designado | **PII** (organigrama) | autorreferencias en `Empleado` |
| Período de licencia (fechas de ausencia) | **PII** | `Solicitud.FechaInicio`, `FechaFin` |
| Cadena de conexión a SQL Server | **Credenciales** | user-secrets (dev), `VACACIONES_CONNECTION` (resto) |
| Estado y días de una solicitud | Interno | `Solicitud.Estado`, `DiasCorridos` |

No se manejan datos financieros ni de salud. **Las fechas de ausencia son PII sensible en la
práctica**: revelan cuándo una persona identificada no está en su domicilio habitual ni en su
trabajo.

### Cifrado exigido (F-TM-07)

| Trayecto | Exigencia |
|---|---|
| Navegador → host | HTTPS con TLS 1.2 o superior (PRD-001 RNF-01). El circuito Blazor Server viaja sobre WSS, heredando el mismo canal. |
| Host → SQL Server | `Encrypt=True;TrustServerCertificate=False` en la cadena de conexión. Sin esto, la PII viaja en claro por la red del host. |
| En reposo | Cifrado del volumen o TDE de SQL Server. **Fuera del control de este ticket** — queda como riesgo aceptado R-09. |
| Credenciales | Nunca en el repositorio (es público). Solo user-secrets o variable de entorno. |

---

## 2. Fronteras de confianza (F-TM-02)

| # | Frontera | Qué la cruza | Nivel de confianza |
|---|---|---|---|
| **TB-1** | Navegador → circuito Blazor Server | Fechas del formulario, selección de empleado, eventos de UI sobre SignalR | No confiable → confiable |
| **TB-2** | Host → SQL Server 2022 | Consultas EF Core, credenciales de conexión | Confiable → confiable, sobre red no confiable |
| **TB-3** | Host de desarrollo → daemon de Docker | Testcontainers levantando `mssql/server:2022` | Solo desarrollo. El socket de Docker es equivalente a root en la máquina. |
| **TB-4** | Repositorio público → configuración | `appsettings*.json`, `.gitignore`, `launchSettings.json` | Todo lo versionado es público por definición |
| **TB-5** | Empleado A → datos del empleado B | `SolicitudesService`, `PermisosService`, `EmpleadosService` | **Frontera declarada pero NO aplicada en este ticket.** Es el centro del riesgo R-01. |

---

## 3. Análisis STRIDE por componente (F-TM-01)

### C1 — Host y composición (`Program.cs`, bloque 1)

| STRIDE | Hallazgo |
|---|---|
| **S** | El registro condicionado a `IsDevelopment()` es la **única** barrera entre el selector de identidad y producción. Una variable de entorno la anula. → **R-01** |
| **T** | `appsettings.json` versionado en repo público puede recibir una cadena con credenciales por descuido. → **R-02** |
| **R** | Sin identidad autenticada, ninguna acción registrada es atribuible. → **R-05** |
| **I** | `DetailedErrors` y la página de excepciones del desarrollador exponen trazas y configuración si el entorno es Development. → **R-01** |
| **D** | Sin límite de circuitos, cada conexión reserva memoria del servidor. → **R-06** |
| **E** | Elegir empleado desde el desplegable **es** elevación de privilegio, por diseño en desarrollo. → **R-01** |

### C2 — Identidad (`IEmpleadoActualProvider`, bloque 4)

| STRIDE | Hallazgo |
|---|---|
| **S** | Suplantación total y trivial: el desplegable no pide credencial. Aceptable en desarrollo, catastrófico fuera. → **R-01** |
| **T** | `EmpleadoActualDesarrollo` es scoped por circuito; como singleton, un usuario le cambiaría el empleado a todos. Mitigado en el diseño. |
| **R** | La identidad elegida no es verificable, así que nada de lo que hace es no repudiable. → **R-05** |
| **I** | El selector necesita la nómina completa: nombres y correos de todos. → **R-04** |
| **D** | Sin impacto. |
| **E** | Cualquiera es cualquiera. → **R-01** |

### C3 — Persistencia (`VacacionesDbContext`, bloque 2)

| STRIDE | Hallazgo |
|---|---|
| **S** | La app se autentica contra SQL Server con la cadena de conexión. Si se filtra, se suplanta a la aplicación entera. → **R-02** |
| **T** | Las tres check constraints (`FechaFin >= FechaInicio`, `DiasCorridos > 0`, `DiasCorridos = DATEDIFF + 1`) son defensa en profundidad real: un bug en C# no puede corromper la invariante. **Mitigación ya presente en el diseño.** |
| **R** | `FechaCreacion` da trazabilidad temporal, pero sin identidad autenticada no prueba autoría. → **R-05** |
| **I** | EF Core parametriza todas las consultas: sin superficie de inyección SQL, siempre que no aparezca SQL crudo. → **R-08** |
| **D** | El índice `(EmpleadoId, FechaCreacion)` evita el escaneo completo en el listado. Mitigación presente. |
| **E** | El usuario de base debe tener permisos mínimos, no `db_owner`. → **R-07** |

### C4 — Semilla (`SeedDatos`, bloque 3)

| STRIDE | Hallazgo |
|---|---|
| **S** | Los empleados sembrados son identidades utilizables por el selector. En la base equivocada, son cuentas fantasma. → **R-03** |
| **T** | Escribe filas en `Empleados`. Si apunta a la base v1 o a producción, contamina datos reales. → **R-03** |
| **R** | Sin registro de qué base se sembró ni cuándo. → **R-03** |
| **I** | Los datos sembrados son ficticios: sin exposición de PII real. |
| **D** | Se ejecuta una vez al arrancar, solo si la tabla está vacía. Sin impacto. |
| **E** | Sin impacto directo. |

### C5 — Dominio (`SolicitudesService`, `PermisosService`, bloque 5)

| STRIDE | Hallazgo |
|---|---|
| **S** | Consume la identidad de C2; hereda su debilidad. → **R-01** |
| **T** | Las fechas llegan de TB-1 y **deben validarse en el servidor**, no solo en el formulario. Validar únicamente en la UI deja la regla esquivable. → **R-10** |
| **R** | Ver R-05. |
| **I** | `PermisosService` como única sede de la decisión de visibilidad es la mitigación estructural de TB-5: cuando llegue OAuth, hay un solo lugar que endurecer. **Presente en el diseño.** |
| **D** | Una solicitud de 10 años son 3.650 días corridos calculados sin costo apreciable. Sin impacto. |
| **E** | `PermisosService` decide; sin autenticación real, decide sobre una identidad no verificada. → **R-01** |

### C6 — Interfaz (`.razor`, bloque 6)

| STRIDE | Hallazgo |
|---|---|
| **S** | Sin superficie propia. |
| **T** | La validación del formulario es conveniencia, no control. → **R-10** |
| **R** | Sin superficie propia. |
| **I** | Razor y MudBlazor escapan la salida por defecto. El riesgo aparece solo con `MarkupString`. → **R-08** |
| **D** | Cada circuito mantiene estado en el servidor. → **R-06** |
| **E** | El selector es el vector; ver R-01. |

### C7 — Infraestructura de tests (Testcontainers, TB-3)

| STRIDE | Hallazgo |
|---|---|
| **S/T/I/E** | Testcontainers requiere acceso al socket de Docker, que es equivalente a root en la máquina de desarrollo. Es una dependencia de desarrollo, nunca de producción. → **R-11** |
| **R** | Sin impacto. |
| **D** | La imagen `mssql/server:2022` consume memoria durante la corrida. Sin impacto en producción. |

---

## 4. Riesgos, con mitigación o aceptación formal (F-TM-03)

### 🔴 R-01 — CRITICAL · Despliegue con `ASPNETCORE_ENVIRONMENT=Development`

**STRIDE:** Spoofing + Elevation of Privilege + Information Disclosure
**Probabilidad:** Media · **Impacto:** Critical

Una sola variable de entorno mal puesta convierte el guardarraíl en nada: se registra
`EmpleadoActualDesarrollo`, aparece el desplegable, cualquiera es cualquier empleado, `SeedDatos`
escribe la nómina ficticia y la página de excepciones del desarrollador expone trazas y
configuración. El diseño confía en **una única condición booleana** para separar desarrollo de
producción, lo que contradice el principio de defensa en profundidad.

**Mitigaciones — entran en la spec:**

1. **Doble condición.** `EmpleadoActualDesarrollo` se registra solo si `IsDevelopment()` **y**
   además la clave `Vacaciones:PermitirIdentidadDeDesarrollo` vale `true`. Ausente la clave, se
   registra `EmpleadoActualNoConfigurado`. Dos condiciones independientes, y la de por defecto es la
   segura (*secure by default*).
2. **Fallo al arrancar, no al primer uso.** Verificación en `Program.cs`: si el entorno no es
   `Development` y el proveedor resuelto no es `EmpleadoActualNoConfigurado`, se lanza excepción
   **en el arranque**. Una app que no levanta es un incidente visible; una que levanta y suplanta,
   no.
3. **`DetailedErrors = false`** y sin página de excepciones del desarrollador fuera de
   `Development` (F-SAST-09).
4. Test de composición que verifique ambos caminos: con entorno `Production` el contenedor resuelve
   el proveedor que lanza; con `Development` y sin la clave, también.

### 🟠 R-02 — HIGH · Credenciales de conexión en un repositorio público

**STRIDE:** Tampering + Information Disclosure · **Probabilidad:** Media · **Impacto:** High

El repo es público y contiene `appsettings.json`. El modo de fallo por defecto —escribir la cadena
ahí para que "funcione"— publica credenciales de SQL Server de forma permanente: el historial de git
las conserva aunque después se borren.

**Mitigaciones — entran en la spec:**

1. `appsettings.json` versionado **sin** ninguna cadena de conexión.
2. Precedencia implementada y testeada: `VACACIONES_CONNECTION` gana sobre la configuración; en
   desarrollo, user-secrets con `ConnectionStrings:Vacaciones`.
3. `.gitignore` incorpora `appsettings.*.local.json`, `*.user` y `.vs/`, **por append**, sin tocar
   el bloque gestionado por DAW.
4. Test que falle si algún `appsettings*.json` versionado contiene `Password`, `User ID` o `pwd=`.
5. La cadena exige `Encrypt=True;TrustServerCertificate=False` (F-TM-07).

### 🟠 R-03 — HIGH · `SeedDatos` contra la base equivocada

**STRIDE:** Tampering + Spoofing · **Probabilidad:** Media · **Impacto:** High

`AGENTS.md` marca como cicatriz el cruce entre `GestionVacaciones` (v1) y `GestionVacacionesV2`. Si
el entorno es `Development` pero la cadena apunta a la v1 o a una base real, la semilla escribe
cuatro empleados ficticios en datos de verdad — y esos empleados son identidades que el selector
acepta.

**Mitigaciones — entran en la spec:**

1. `SeedDatos` aborta si el catálogo de la conexión no es `GestionVacacionesV2`.
2. Registra en log el nombre de la base **antes** de escribir.
3. Se mantiene la condición de sembrar solo si `Empleados` está vacía.

### 🟠 R-04 — HIGH · La nómina completa expuesta en el selector

**STRIDE:** Information Disclosure · **Probabilidad:** Alta · **Impacto:** Medium-High

`EmpleadosService` devuelve nombre y correo de toda la plantilla para poblar el desplegable. En
desarrollo son datos sembrados y ficticios; con datos reales sería un volcado de PII a cualquiera
que abra la aplicación.

**Mitigación:** el servicio de nómina se registra bajo la misma doble condición de R-01, de modo que
no exista fuera de desarrollo. Queda ligado al mismo guardarraíl, no a uno propio.

### 🟠 R-05 — HIGH · Ninguna acción es atribuible (riesgo aceptado)

**STRIDE:** Repudiation · **Probabilidad:** Alta · **Impacto:** High

Sin autenticación, `Solicitud.EmpleadoId` registra a quién se **atribuye** la solicitud, no quién la
creó. Cualquier registro es negable. No hay mitigación técnica posible dentro del alcance de este
ticket: la solución es RF-01, que está explícitamente fuera.

**Riesgo aceptado (F-TM-04):**

| Campo | Valor |
|---|---|
| Aceptado por | El usuario propietario del proyecto — aceptación explícita el 2026-08-01 |
| Justificación | RF-01 (OAuth) está fuera del alcance de FEAT-001a por decisión de división del ticket. Sin identidad autenticada no existe no repudio, y construir uno provisional sería trabajo desechable. La aplicación no se despliega hasta que exista RF-01. |
| Condiciones de revisión | Se revisa al implementar PRD-001 RF-01, y en cualquier caso antes del primer despliegue fuera de una máquina de desarrollo. Fecha límite de revisión: 2027-02-01. |
| Control compensatorio | `EmpleadoActualNoConfigurado` impide arrancar fuera de `Development`; `FechaCreacion` deja rastro temporal; el PRD padre declara que FEAT-001a no se despliega solo. |

### 🟠 R-06 — HIGH · Agotamiento de recursos por circuitos Blazor sin autenticar

**STRIDE:** Denial of Service · **Probabilidad:** Media · **Impacto:** Medium

Blazor Server mantiene estado en el servidor por cada circuito. Sin autenticación ni límites, abrir
conexiones en masa agota la memoria. NFR-01 exige soportar 50 usuarios concurrentes, lo que fija el
orden de magnitud esperado, no un techo.

**Mitigación:** límite explícito de circuitos y de tamaño de mensaje en la configuración del host;
`DisconnectedCircuitMaxRetained` acotado. La defensa real es no exponer la aplicación a una red no
confiable mientras no exista RF-01.

### 🟡 R-07 — MEDIUM · Usuario de base con permisos excesivos

**STRIDE:** Elevation of Privilege · **Probabilidad:** Media · **Impacto:** Medium

El camino cómodo es conectarse con un usuario `db_owner` o `sa`. Con ese permiso, cualquier fallo de
la aplicación se convierte en control total de la base.

**Mitigación:** documentar en la spec que el usuario de aplicación necesita solo `SELECT`, `INSERT`,
`UPDATE` sobre las tablas del esquema, y que las migraciones se aplican con una cuenta distinta y de
mayor privilegio, no con la de la aplicación.

### 🟡 R-08 — MEDIUM · XSS e inyección SQL residuales

**STRIDE:** Tampering + Information Disclosure · **Probabilidad:** Baja · **Impacto:** High

Razor y MudBlazor escapan la salida por defecto, y EF Core parametriza. Ambas defensas se pierden
con una sola línea: `MarkupString` con entrada de usuario, o `FromSqlRaw` concatenando.

**Mitigación:** la spec prohíbe explícitamente `MarkupString` con datos de usuario y todo SQL crudo
concatenado. SAST lo verifica en CODE (F-SAST-02, F-SAST-06).

### 🟡 R-09 — MEDIUM · PII sin cifrado en reposo (riesgo aceptado)

**STRIDE:** Information Disclosure · **Probabilidad:** Baja · **Impacto:** High

Nombres, correos y períodos de ausencia se almacenan en claro en SQL Server. Activar TDE o cifrar el
volumen es configuración de infraestructura, fuera del alcance de un ticket de aplicación.

**Riesgo aceptado (F-TM-04):**

| Campo | Valor |
|---|---|
| Aceptado por | El usuario propietario del proyecto — aceptación explícita el 2026-08-01 |
| Justificación | El cifrado en reposo es configuración del motor y del host, no del código de la aplicación. En desarrollo la base contiene únicamente datos sembrados y ficticios. |
| Condiciones de revisión | Antes del primer despliegue con datos de empleados reales. Fecha límite de revisión: 2027-02-01. |
| Control compensatorio | Cifrado en tránsito exigido en ambos trayectos (TLS al navegador, `Encrypt=True` a SQL Server). |

### 🟡 R-10 — MEDIUM · Validación de fechas solo en el formulario

**STRIDE:** Tampering · **Probabilidad:** Media · **Impacto:** Medium

AC-02 y AC-03 se enuncian como "impedir el envío y mostrar el mensaje", lo que invita a resolverlos
deshabilitando un botón. Un cliente manipulado o un evento fuera de orden esquivaría la regla.

**Mitigación:** la validación vive en `SolicitudesService` (servidor) y el formulario **consume** su
resultado en lugar de reimplementar la comparación. Regla de `security.instructions.md`: la
validación del lado servidor es obligatoria.

### 🟢 R-11 — LOW · Socket de Docker expuesto por Testcontainers

**STRIDE:** Elevation of Privilege · **Probabilidad:** Baja · **Impacto:** Medium

Testcontainers necesita hablar con el daemon de Docker, cuyo socket equivale a root en la máquina.

**Mitigación:** es dependencia exclusiva de desarrollo y test. Debe referenciarse solo desde
`GestionVacaciones.Tests`, nunca desde `Web` ni `Data`, para que no pueda llegar a un artefacto de
publicación.

### 🟢 R-12 — LOW · PII en los registros de log

**STRIDE:** Information Disclosure · **Probabilidad:** Media · **Impacto:** Low

Loguear el empleado actual para depurar escribe nombre y correo en los logs (F-SAST-10).

**Mitigación:** los logs registran `EmpleadoId`, nunca nombre ni correo.

### Cadena de suministro (W-TM-01)

Siete dependencias nuevas: MudBlazor, `Microsoft.EntityFrameworkCore.SqlServer`,
`Microsoft.EntityFrameworkCore.Design`, el conjunto de xUnit, bUnit, `coverlet.collector` y
Testcontainers. Las cuatro últimas son exclusivas del proyecto de tests.
`Microsoft.EntityFrameworkCore.Design` se referencia con `PrivateAssets="all"` para que no fluya al
publicado. La spec debe fijar versiones exactas: `AGENTS.md` prohíbe mezclar versiones y
`TreatWarningsAsErrors` convierte cualquier advertencia de compatibilidad en un build roto.

---

## 5. Mitigaciones que la spec debe incorporar

1. Doble condición para el proveedor de desarrollo: `IsDevelopment()` **y**
   `Vacaciones:PermitirIdentidadDeDesarrollo=true`; por defecto se registra el que lanza.
2. Verificación en el arranque que hace fallar la aplicación si el proveedor de desarrollo quedara
   activo fuera de `Development`.
3. `DetailedErrors=false` y sin página de excepciones del desarrollador fuera de `Development`.
4. `appsettings.json` versionado sin cadena de conexión; precedencia de `VACACIONES_CONNECTION`
   implementada y testeada.
5. `.gitignore` con `appsettings.*.local.json`, `*.user`, `.vs/`, agregados por append.
6. Test que falle si un `appsettings*.json` versionado contiene credenciales.
7. `Encrypt=True;TrustServerCertificate=False` exigido en la cadena de conexión.
8. `SeedDatos` aborta si el catálogo no es `GestionVacacionesV2`, y registra la base antes de
   escribir.
9. `EmpleadosService` registrado bajo la misma doble condición que el proveedor de desarrollo.
10. Límite de circuitos Blazor y de tamaño de mensaje configurados en el host.
11. Documentar que el usuario de base de la aplicación no lleva `db_owner`.
12. Prohibición explícita de `MarkupString` con entrada de usuario y de SQL crudo concatenado.
13. La validación de fechas vive en el servicio; el formulario consume su resultado.
14. Testcontainers referenciado únicamente desde el proyecto de tests.
15. Los logs registran `EmpleadoId`, nunca nombre ni correo.
16. Versiones exactas de las siete dependencias; `EFCore.Design` con `PrivateAssets="all"`.

---

## 6. Resumen

| Severidad | Cantidad | Estado |
|---|---|---|
| 🔴 Critical | 1 | Mitigado en el diseño (R-01) |
| 🟠 High | 5 | 3 mitigados (R-02, R-03, R-04), 1 mitigado parcialmente (R-06), 1 riesgo aceptado (R-05) |
| 🟡 Medium | 4 | 3 mitigados (R-07, R-08, R-10), 1 riesgo aceptado (R-09) |
| 🟢 Low | 2 | Mitigados (R-11, R-12) |

**Los dos riesgos aceptados (R-05 y R-09) fueron confirmados explícitamente por el usuario el
2026-08-01**, con los tres campos que exige F-TM-04: quién acepta, justificación y condiciones de
revisión. El modelo está completo.

**Veredicto: PASSED.** Todo riesgo Critical y High tiene mitigación incorporada a la spec o
aceptación formal registrada.
