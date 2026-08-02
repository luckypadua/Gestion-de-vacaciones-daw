# Spec FEAT-001a: Andamiaje, identidad del empleado y alta de solicitud con validación de fechas y listado propio

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| PRD | `docs/daw/prd/prd-FEAT-001a.md` |
| Threat model | `docs/daw/security/threat-FEAT-001a.md` |
| Tier | FEATURE |
| Date | 2026-08-01 |
| Spec loops | 2 |

## Summary

Se levanta desde cero la solución .NET 10 (`Directory.Build.props` en la raíz, tres proyectos, host
Blazor Server + MudBlazor) y sobre ella el modelo `Empleado`/`Solicitud` en SQL Server 2022 con EF
Core 10, accedido siempre por `IDbContextFactory`. La identidad del empleado —que no tiene OAuth en
este ticket— se resuelve con una única interfaz `IEmpleadoActualProvider` cuya implementación de
desarrollo exige **dos** condiciones independientes para activarse, y cuya variante productiva hace
fallar el arranque. Encima, tres servicios de dominio (`CalculadorDeDiasCorridos`,
`SolicitudesService`, `PermisosService`) implementan el alta con validación de fechas y el listado
propio, y cuatro componentes MudBlazor los consumen sin tocar nunca el `DbContext`.

Las invariantes del período y del estado se refuerzan con cuatro check constraints en la base,
verificadas contra un SQL Server 2022 **real** —la instancia del entorno de desarrollo, sobre una
base descartable con sufijo `_Test`— porque el proveedor InMemory ignora los check constraints y
haría pasar en verde un test que afirma lo contrario de lo que NFR-04 exige.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 (registrar solicitud en Pendiente) | Block 2 (modelo), Block 5 (alta) |
| FR-02 (calcular y mostrar días corridos) | Block 5 (cálculo), Block 6 (visualización) |
| FR-03 (rechazar período inválido) | Block 5 (validación en servidor), Block 6 (consumo del resultado) |
| FR-04 (listar solo las propias, desc. por fecha de creación) | Block 2 (índice), Block 5 (`PermisosService` + consulta), Block 6 (render) |
| FR-05 (proveedor único de identidad) | Block 4 |
| NFR-01 (p95 < 3 s, 50 concurrentes) | **Estrategia:** `IDbContextFactory` por operación evita el `DbContext` compartido que serializa el circuito, y el índice `(EmpleadoId, FechaCreacion)` del Block 2 evita el escaneo completo en el listado. La verificación de carga con 50 usuarios concurrentes **se difiere a un ticket de performance propio**: este ticket no despliega nada y no hay entorno donde medirla. Declarado explícitamente, no omitido. |
| NFR-02 (cobertura ≥ 80%) | **Estrategia:** `coverlet.collector` 10.0.1 referenciado desde el proyecto de tests + `dotnet test --collect:"XPlat Code Coverage"`. Sin ese paquete `dotnet test` no emite ningún número y F-VER-03 llega a VERIFY sin nada que medir. |
| NFR-03 (2 últimas versiones de Chrome, Edge y Firefox) | **Estrategia:** bUnit renderiza en memoria y **no abre un navegador**, así que no aporta evidencia aquí. Se declara **verificación manual** sobre los tres navegadores al cerrar el ticket. Automatizarlo exigiría Playwright, desproporcionado para este alcance. |
| NFR-04 (0 filas persistibles inválidas) | Block 2: **cuatro** check constraints (tres sobre el período, una sobre el estado), verificadas contra la **instancia SQL Server 2022 del entorno de desarrollo**, sobre una base descartable, con los tests marcados como categoría de integración |
| NFR-05 (`IDbContextFactory` al 100%, 0 `AddDbContext`) | Block 2 + test de composición del contenedor |
| NFR-06 (1 sola interfaz de identidad) | Block 4 + test de composición del contenedor |
| NFR-07 (0 advertencias) | Block 1, `TreatWarningsAsErrors` en el `Directory.Build.props` de la **raíz** |

**AC → test** (F-SPEC-02): AC-01 → B6-T1 · AC-02 → B5-T3 y B6-T2 · AC-03 → B5-T4 y B6-T3 ·
AC-04 → B5-T5 · AC-05 → B5-T7 y B6-T4 · AC-06 → B4-T1 y B4-T2 · AC-07 → B4-T3 y B6-T5.

## Dependencies between blocks

Cadena lineal estricta: **1 → 2 → 3 → 4 → 5 → 6**.

- **B2** necesita los proyectos y la cadena de conexión de **B1** (sin ella no se genera la migración).
- **B3** necesita las entidades y el `DbContext` de **B2**.
- **B4** necesita la nómina sembrada por **B3** para que el selector tenga qué mostrar.
- **B5** necesita el proveedor de identidad de **B4** para saber de quién es cada solicitud.
- **B6** necesita los servicios de **B5**; no accede a datos por ningún otro camino.

## Convenciones que esta spec fija

- **Ubicación de los tests:** `tests/GestionVacaciones.Tests/` con subcarpetas `Andamiaje/`,
  `Persistencia/`, `Identidad/`, `Dominio/` y `Componentes/`. Esto **contradice
  deliberadamente** `.daw/rules/testing.instructions.md`, que prescribe `tests/integration/` y
  `tests/e2e/`. Gana `AGENTS.md`, por ser el archivo del proyecto. Queda escrito para que
  `daw-validate-arch` no tenga que adivinar.
- **API contract:** **no aplica a ningún bloque.** Esta aplicación es Blazor Server: no expone
  endpoints HTTP propios: la interacción viaja por el circuito SignalR del framework. F-SPEC-07 no
  tiene objeto aquí.
- **Rollback (W-SPEC-03):** la migración de B2 es la **inicial**. No hay esquema previo al que
  volver: revertir es descartar la base `GestionVacacionesV2` y volver a aplicar. Declarado
  explícitamente, no omitido.
- **Logs:** se registra `EmpleadoId`. **Nunca** nombre ni correo (mitigación R-12, F-SAST-10).
- **SQL:** prohibido el SQL crudo concatenado en todo el ticket. Prohibido `MarkupString` con
  entrada de usuario (mitigación R-08).
- **Usuario de base de la aplicación:** solo `SELECT`, `INSERT`, `UPDATE` sobre las tablas del
  esquema. **Sin `db_owner` ni `sa`.** Las migraciones se aplican con una cuenta distinta y de mayor
  privilegio (mitigación R-07).

### Infraestructura de tests de integración

Los tests que verifican NFR-04 necesitan un motor SQL Server real: el proveedor InMemory **ignora
por completo los check constraints**, así que un test escrito contra él pasaría en verde afirmando
lo contrario de lo que NFR-04 exige. Corren contra la instancia del entorno de desarrollo, bajo las
reglas siguientes.

- **Base de datos de test: `GestionVacacionesV2_Test`**, creada por el fixture aplicando las
  migraciones. **Nunca** `GestionVacacionesV2` (la de la aplicación) ni `GestionVacaciones` (la v1,
  que `AGENTS.md` marca como cicatriz).
- **Guardarraíl estructural:** el fixture **aborta con excepción** si el `Initial Catalog` de la
  cadena de test no termina en `_Test`. Es el mismo patrón que `SeedDatos` aplica contra R-03, y
  hace **imposible** —no improbable— que un test destructivo alcance datos reales. Sin él, la regla
  #0 de `testing.instructions.md` dependería de la disciplina de quien escribe cada test; con él,
  depende del código.
- **La cadena de test se lee de la variable de entorno `VACACIONES_CONNECTION_TEST` o de
  user-secrets del proyecto de tests.** Nunca hardcodeada: desde WSL la instancia se alcanza por la
  IP del host de Windows, que **cambia entre reinicios** — `AGENTS.md` lo documenta como cicatriz.
- **Categoría de integración:** los tests que tocan la base llevan `Trait` de categoría. Si no hay
  cadena configurada, **se saltean con motivo explícito**, no fallan. Una suite roja por falta de
  entorno enseña a ignorar el rojo, que es peor que no tener el test.
- **Cada test crea sus propios datos y los limpia** (regla #0).

> **Limitación conocida, declarada y no omitida:** NFR-04 queda verificado **solo donde hay motor**.
> En una máquina sin acceso a la instancia, esos tests se saltean y la invariante no tiene evidencia.
> Cuando exista CI, no correrán ahí hasta que ese entorno tenga SQL Server disponible.

## Dependencias nuevas — 8 paquetes, versiones exactas

`AGENTS.md` prohíbe mezclar versiones y `TreatWarningsAsErrors` convierte cualquier advertencia de
compatibilidad en un build roto, así que se fijan exactas y verificadas contra NuGet.

| Paquete | Versión | Proyecto | Justificación |
|---|---|---|---|
| `MudBlazor` | 9.7.0 | Web | `AGENTS.md`: el UI se construye con MudBlazor, no con otro sistema. Declara `net10.0`. |
| `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.10 | Data | Stack declarado. Declara `net10.0`. |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.10 | Data, con `PrivateAssets="all"` | Sin ella no hay `dotnet ef migrations`. `PrivateAssets` evita que fluya al publicado. |
| `xunit.v3` | 3.2.2 | Tests | `AGENTS.md` fija `dotnet test` sin elegir framework. |
| `Microsoft.NET.Test.Sdk` | 18.8.1 | Tests | Requerido por el runner. |
| `xunit.runner.visualstudio` | 3.1.5 | Tests | Descubrimiento de tests. |
| `bunit` | 2.8.6 | Tests | Único camino para AC-01, AC-05 y AC-07 a nivel componente. **Verificado: declara `net10.0` explícitamente.** |
| `coverlet.collector` | 10.0.1 | Tests | Sin él no hay número de cobertura para NFR-02 ni F-VER-03. |
> **`Testcontainers.MsSql` se retiró en el `Spec loops 2`.** Estaba justificado como el único modo
> de verificar NFR-04 contra el motor real, pero el usuario decidió no depender de Docker y los
> tests pasan a la instancia SQL Server 2022 del entorno de desarrollo (ver "Infraestructura de
> tests de integración"). Con él desaparece además su grafo transitivo —`Docker.DotNet`,
> `BouncyCastle`, `SharpZipLib`— lo que **reduce** la superficie de cadena de suministro (W-TM-01) y
> elimina el riesgo R-11 del modelo de amenazas, que era el acceso al socket de Docker.
> El Bloque 1 se implementó y commiteó **con** esa referencia (`506383e`); el Bloque 2 la elimina.

**Acción previa obligatoria:** el `dotnet-ef` global instalado es **10.0.9** y los paquetes quedan en
**10.0.10**. Actualizar con `dotnet tool update --global dotnet-ef` antes del Block 2.

---

## Block 1 — Andamiaje, host Blazor y configuración

**Files**
- `global.json` (nuevo) — fija el SDK en 10.0.110 con `rollForward: latestFeature`
- `Directory.Build.props` (nuevo, **en la raíz del repositorio, no en `src/`**) — `net10.0`,
  `LangVersion` C# 14, `Nullable=enable`, `TreatWarningsAsErrors=true`. En `src/` el proyecto de
  `tests/` no lo heredaría y NFR-07 se cumpliría a medias.
- `src/GestionVacaciones.slnx` (nuevo) — formato `.slnx`, el default de .NET 10
- `src/GestionVacaciones.Data/GestionVacaciones.Data.csproj` (nuevo)
- `src/GestionVacaciones.Web/GestionVacaciones.Web.csproj` (nuevo) — `UserSecretsId`,
  `ProjectReference` → Data, `MudBlazor` 9.7.0
- `src/GestionVacaciones.Web/Program.cs` (nuevo) — composición del host
- `src/GestionVacaciones.Web/Components/App.razor` (nuevo)
- `src/GestionVacaciones.Web/Components/Routes.razor` (nuevo)
- `src/GestionVacaciones.Web/Components/_Imports.razor` (nuevo)
- `src/GestionVacaciones.Web/Components/Layout/MainLayout.razor` (nuevo) — `MudThemeProvider`,
  `MudLayout`
- `src/GestionVacaciones.Web/wwwroot/app.css` (nuevo) — CSS/JS de MudBlazor
- `src/GestionVacaciones.Web/appsettings.json` (nuevo) — **sin ninguna cadena de conexión**
- `src/GestionVacaciones.Web/appsettings.Development.json` (nuevo) — sin cadena; solo
  `Vacaciones:PermitirIdentidadDeDesarrollo`
- `src/GestionVacaciones.Web/Properties/launchSettings.json` (nuevo)
- `src/GestionVacaciones.Web/Configuracion/CadenaDeConexion.cs` (nuevo) — resuelve la cadena con
  precedencia
- `tests/GestionVacaciones.Tests/GestionVacaciones.Tests.csproj` (nuevo) — `ProjectReference` → Web
  y → Data; xUnit, bUnit, coverlet
- `.gitignore` (modificado) — **solo append**

> **Nota del `Spec loops 2`.** Este bloque ya está implementado y commiteado (`506383e`), y su
> `.csproj` incluye `Testcontainers.MsSql`, que era lo que la spec pedía entonces. La referencia
> **la elimina el Bloque 2**, junto con el cambio de infraestructura de tests. No se reescribe el
> Bloque 1 por eso: su trabajo está hecho y verificado.

**Logic**

Composición del host Blazor Server con MudBlazor (`AddMudServices()`). La resolución de la cadena de
conexión vive en `CadenaDeConexion` con una precedencia explícita y testeable:

1. Variable de entorno `VACACIONES_CONNECTION` — **gana siempre** si está presente y no vacía.
2. `ConnectionStrings:Vacaciones` de la configuración (user-secrets en desarrollo).
3. Si ninguna resuelve → excepción con mensaje accionable. **Nunca** un valor por defecto.

La cadena debe contener `Encrypt=True;TrustServerCertificate=False` (mitigación R-02/F-TM-07) y
apuntar al catálogo `GestionVacacionesV2`. Fuera de `Development`: `DetailedErrors=false`, sin página
de excepciones del desarrollador (F-SAST-09), y límites explícitos de circuito
(`DisconnectedCircuitMaxRetained`, `MaxBufferedUnacknowledgedRenderBatches`) como mitigación parcial
de R-06.

Al `.gitignore` se agregan por **append**: `bin/`, `obj/`, `*.user`, `.vs/`, `TestResults/`,
`artifacts/`, `*.mdf`, `*.ldf`, `appsettings.*.local.json`. **No se toca el bloque
`# BEGIN DAW … # END DAW`**: `.daw/scripts/session-boot.py` (`ensure_gitignore`) lo reañade en cada
escritura, y editarlo produce ruido permanente.

**Input validation**

`VACACIONES_CONNECTION` y `ConnectionStrings:Vacaciones` son entrada de configuración, no de usuario.
Validación: cadena no vacía, parseable por `SqlConnectionStringBuilder`, con `Initial Catalog` no
vacío. Una cadena malformada falla al arrancar, no en la primera consulta.

**Error handling**

| Error | Manejo |
|---|---|
| Ninguna fuente aporta cadena de conexión | Excepción al arrancar, con mensaje que nombra las dos fuentes posibles. Sin valor por defecto. |
| Cadena presente pero no parseable | Excepción al arrancar, nombrando la fuente que la aportó. Nunca se registra el valor: puede contener credenciales. |
| Cadena sin `Initial Catalog` | Excepción al arrancar. |

**Required tests**
- [ ] **B1-T1** — arranque del host: el `IServiceProvider` se construye y resuelve la raíz de
  servicios. *No* es "la solución compila": afirma algo que falla si la composición está rota.
- [ ] **B1-T2** — precedencia: con `VACACIONES_CONNECTION` y `ConnectionStrings:Vacaciones` ambas
  presentes y distintas, gana la variable de entorno.
- [ ] **B1-T3** — sad path: sin ninguna de las dos fuentes, arrancar lanza excepción.
- [ ] **B1-T4** — sad path: cadena presente pero no parseable → excepción, y el mensaje **no**
  contiene el valor de la cadena.
- [ ] **B1-T5** — sad path: cadena sin `Initial Catalog` → excepción.
- [ ] **B1-T6** — **guardarraíl de secretos (mitigación R-02):** falla si algún `appsettings*.json`
  versionado contiene `Password`, `User ID` o `pwd=`. El repositorio es público.

**Completion criterion**
`dotnet build src/GestionVacaciones.slnx` termina con **0 advertencias y 0 errores** con
`TreatWarningsAsErrors` activo, `dotnet test` ejecuta los 6 tests de este bloque en verde, y
`git status` no muestra `bin/` ni `obj/` como no rastreados.

---

## Block 2 — Modelo de dominio y persistencia

**Files**
- `src/GestionVacaciones.Data/Entidades/Empleado.cs` (nuevo)
- `src/GestionVacaciones.Data/Entidades/Solicitud.cs` (nuevo)
- `src/GestionVacaciones.Data/Entidades/EstadoSolicitud.cs` (nuevo) — enum
- `src/GestionVacaciones.Data/VacacionesDbContext.cs` (nuevo)
- `src/GestionVacaciones.Data/VacacionesDbContextFactory.cs` (nuevo) —
  `IDesignTimeDbContextFactory<VacacionesDbContext>`. **Es necesario:** con `AddDbContextFactory` y
  sin `AddDbContext`, `dotnet ef` no encuentra el contexto por el camino habitual del host.
- `src/GestionVacaciones.Data/Migrations/*` (nuevo) — migración inicial generada
- `src/GestionVacaciones.Data/GestionVacaciones.Data.csproj` (modificado) — EF Core SqlServer 10.0.10
  y Design 10.0.10 con `PrivateAssets="all"`
- `src/GestionVacaciones.Web/Program.cs` (modificado) — `AddDbContextFactory<VacacionesDbContext>`.
  **Nunca `AddDbContext`** (`AGENTS.md`, NFR-05)
- `tests/GestionVacaciones.Tests/Persistencia/BaseDeDatosDeTest.cs` (nuevo) — fixture de integración:
  guardarraíl del sufijo `_Test`, creación de la base aplicando migraciones, y limpieza
- `tests/GestionVacaciones.Tests/GestionVacaciones.Tests.csproj` (modificado) — **elimina**
  `Testcontainers.MsSql`, que el Bloque 1 había dejado referenciado

**Data model**

`Empleado`

| Campo | Tipo | Restricciones |
|---|---|---|
| `Id` | `int` | PK, identity |
| `Nombre` | `string` | no nulo, máx. 200 |
| `Correo` | `string` | no nulo, máx. 320, **único** |
| `ManagerId` | `int?` | nulo permitido, FK → `Empleado.Id`, `ON DELETE NO ACTION` |
| `DesignadoId` | `int?` | nulo permitido, FK → `Empleado.Id`, `ON DELETE NO ACTION` |

Las dos FK son autorreferencias; `NO ACTION` es obligatorio, porque en cascada SQL Server rechaza el
ciclo.

`Solicitud`

| Campo | Tipo | Restricciones |
|---|---|---|
| `Id` | `int` | PK, identity |
| `EmpleadoId` | `int` | no nulo, FK → `Empleado.Id` |
| `FechaInicio` | `DateOnly` | no nulo |
| `FechaFin` | `DateOnly` | no nulo |
| `DiasCorridos` | `int` | no nulo |
| `Estado` | `EstadoSolicitud` | no nulo, persistido como `int` |
| `FechaCreacion` | `DateTimeOffset` | no nulo |

**Check constraints — la exigencia de NFR-04:**

| Nombre | Condición |
|---|---|
| `CK_Solicitud_PeriodoCoherente` | `FechaFin >= FechaInicio` |
| `CK_Solicitud_DiasPositivos` | `DiasCorridos > 0` |
| `CK_Solicitud_DiasCoincidenConPeriodo` | `DiasCorridos = DATEDIFF(day, FechaInicio, FechaFin) + 1` |
| `CK_Solicitud_EstadoValido` | `Estado BETWEEN 0 AND 2` |

La tercera es la que impide la incoherencia entre lo que la interfaz muestra (AC-01) y lo que se
guarda (AC-04): sin ella se puede persistir 3-ene→5-ene con `DiasCorridos = 99`.

La cuarta cubre el hueco que dejan las otras tres: `Estado` se persiste como `int`, y sin ella nada
impide una fila con un valor fuera del rango de `EstadoSolicitud`. Las tres primeras protegen el
período; ninguna protegía el estado.

**Índice:** `IX_Solicitud_EmpleadoId_FechaCreacion` sobre `(EmpleadoId, FechaCreacion DESC)`. Es el
que sirve al listado de FR-04. **No** se crea el índice por fechas del período: sirve a la detección
de superposición, que pertenece a FEAT-001c y está fuera de alcance.

**Error handling**

| Error | Manejo |
|---|---|
| Violación de una check constraint | `DbUpdateException`. Es defensa en profundidad: si llega acá, la validación de B5 falló. Se registra `EmpleadoId` y el nombre de la constraint, nunca los datos personales. |
| Violación de unicidad de `Correo` | `DbUpdateException`. Solo alcanzable desde la semilla o carga externa. |
| `Estado` fuera del rango del enum | Rechazado por `CK_Solicitud_EstadoValido`. Alcanzable por carga externa o por un cast inválido en C#. |
| Base inalcanzable | La excepción de conexión se propaga. No se reintenta en silencio ni se degrada a un resultado vacío, que se leería como "no tenés solicitudes". |
| El catálogo de la cadena de test no termina en `_Test` | El fixture **aborta con excepción antes de abrir la conexión**. Nunca se ejecuta una operación destructiva contra un catálogo que no se declaró como de prueba. |
| No hay cadena de test configurada | Los tests de integración **se saltean** con motivo explícito. No fallan: una suite roja por falta de entorno enseña a ignorar el rojo. |
| La instancia de test es inalcanzable | La excepción de conexión se propaga con el nombre del catálogo destino, **nunca con la cadena**, que puede llevar credenciales. |

**Required tests** *(contra la instancia SQL Server 2022 del entorno, sobre `GestionVacacionesV2_Test`; ver "Infraestructura de tests de integración")*
- [ ] **B2-T1** — sad path: insertar con `FechaFin < FechaInicio` es rechazado por
  `CK_Solicitud_PeriodoCoherente` (NFR-04).
- [ ] **B2-T2** — sad path: insertar con `DiasCorridos = 0` es rechazado por
  `CK_Solicitud_DiasPositivos` (NFR-04).
- [ ] **B2-T3** — sad path: insertar 3-ene→5-ene con `DiasCorridos = 99` es rechazado por
  `CK_Solicitud_DiasCoincidenConPeriodo` (NFR-04).
- [ ] **B2-T4** — sad path: dos empleados con el mismo `Correo` → violación de unicidad.
- [ ] **B2-T5** — la migración crea `IX_Solicitud_EmpleadoId_FechaCreacion`.
- [ ] **B2-T6** — **composición (NFR-05):** el contenedor tiene
  `IDbContextFactory<VacacionesDbContext>` registrado y **ningún** descriptor de
  `VacacionesDbContext` directo.
- [ ] **B2-T7** — una solicitud válida se persiste y se relee con sus siete campos intactos.
- [ ] **B2-T8** — sad path: con la base inalcanzable, la excepción de conexión **se propaga** y la
  consulta **no** devuelve una colección vacía. Es el caso peligroso: degradar a lista vacía le
  diría al empleado "no tenés solicitudes" mientras la base está caída.
- [ ] **B2-T9** — sad path: insertar con `Estado = 99` es rechazado por `CK_Solicitud_EstadoValido`
  (NFR-04).
- [ ] **B2-T10** — sad path del fixture: con una cadena cuyo catálogo **no termina en `_Test`**, el
  fixture aborta **antes de abrir la conexión**.
- [ ] **B2-T11** — sad path del fixture: con una cadena que apunta a `GestionVacacionesV2` o a
  `GestionVacaciones` (la v1), el fixture aborta. Son los dos catálogos que `AGENTS.md` marca como
  intocables, y este test los nombra explícitamente en vez de confiar en la regla del sufijo.
- [ ] **B2-T12** — sin cadena de test configurada, los tests de integración **se saltean** con motivo
  explícito y la suite queda verde, no roja.
- [ ] **B2-T13** — sad path del fixture: con una cadena de test cuyo host no responde, la excepción
  se propaga y el mensaje **contiene el nombre del catálogo destino pero no la cadena**. Espeja lo
  que B1-T4 fija para la cadena de la aplicación: el diagnóstico tiene que ser accionable sin
  publicar credenciales.

**Completion criterion**
`dotnet ef migrations add InicialV2 -p src/GestionVacaciones.Data -s src/GestionVacaciones.Web`
genera la migración, `dotnet ef database update` la aplica sobre `GestionVacacionesV2`, y los 13
tests pasan contra la instancia SQL Server 2022 del entorno sobre `GestionVacacionesV2_Test`.
`Testcontainers.MsSql` ya no figura en ningún `.csproj`.

---

## Block 3 — Semilla de desarrollo

**Files**
- `src/GestionVacaciones.Data/SeedDatos.cs` (nuevo)
- `src/GestionVacaciones.Web/Program.cs` (modificado) — invocación solo en `Development`

**Logic**

Siembra en tiempo de ejecución —**no `HasData`**, porque las autorreferencias manager/designado son
circulares y `HasData` no puede ordenar los `INSERT`— cuatro empleados: **Ana** (manager), **Diego**
(designado de Ana), **Bruno** y **Carla**. Solo si la tabla `Empleados` está vacía.

**Mitigación R-03:** antes de escribir, comprueba que el `Initial Catalog` de la conexión sea
`GestionVacacionesV2`; si no lo es, **aborta** sin escribir nada y registra el nombre del catálogo
encontrado. `AGENTS.md` documenta el cruce v1/v2 como cicatriz: sembrar cuatro empleados ficticios
sobre la base v1 o sobre datos reales crea identidades fantasma que el selector aceptaría.

**Error handling**

| Error | Manejo |
|---|---|
| El catálogo no es `GestionVacacionesV2` | Aborta sin escribir. Log de nivel warning con el nombre del catálogo. La aplicación sigue arrancando. |
| La tabla `Empleados` ya tiene filas | No hace nada. Operación idempotente. |
| Fallo al persistir la nómina | La excepción se propaga: una semilla a medias deja el desarrollo en un estado que nadie puede diagnosticar. |

**Required tests**
- [ ] **B3-T1** — sobre base vacía siembra los 4 empleados, con Diego como designado de Ana.
- [ ] **B3-T2** — invocada dos veces, la segunda no escribe nada (idempotencia).
- [ ] **B3-T3** — sad path: con catálogo distinto de `GestionVacacionesV2`, aborta y la tabla queda
  vacía (mitigación R-03).
- [ ] **B3-T4** — sad path: si persistir falla, la excepción se propaga y no queda una nómina
  parcial.

**Completion criterion**
Arrancar en `Development` contra una `GestionVacacionesV2` vacía deja 4 empleados con sus relaciones;
arrancar una segunda vez no agrega ninguno; los 4 tests pasan.

---

## Block 4 — Identidad del empleado actual

**Files**
- `src/GestionVacaciones.Data/Services/IEmpleadoActualProvider.cs` (nuevo)
- `src/GestionVacaciones.Data/Services/EmpleadosService.cs` (nuevo) — lectura de la nómina. Existe
  porque la interfaz **no puede** tocar el `DbContext` (`AGENTS.md`), y sin él el selector no tendría
  otro camino.
- `src/GestionVacaciones.Web/Identidad/EmpleadoActualDesarrollo.cs` (nuevo) — **scoped**
- `src/GestionVacaciones.Web/Identidad/EmpleadoActualNoConfigurado.cs` (nuevo) — lanza excepción
- `src/GestionVacaciones.Web/Identidad/VerificacionDeIdentidad.cs` (nuevo) — comprobación de arranque
- `src/GestionVacaciones.Web/Program.cs` (modificado) — registro condicionado

**Logic**

Una única interfaz, `IEmpleadoActualProvider`, es la sede exclusiva de la resolución de identidad
(NFR-06). `EmpleadoActualDesarrollo` se registra **scoped**: en Blazor Server eso significa uno por
circuito. Como singleton, el empleado que elige una persona se lo cambiaría a todas las demás y AC-07
se rompería de una forma que ningún test unitario ve.

**Mitigación R-01 (CRITICAL) — dos condiciones independientes:**

`EmpleadoActualDesarrollo` y `EmpleadosService` se registran **solo si**
`IWebHostEnvironment.IsDevelopment()` **y además** `Vacaciones:PermitirIdentidadDeDesarrollo` vale
`true`. Ausente o falsa la clave, se registra `EmpleadoActualNoConfigurado`. El valor por defecto es
el seguro.

**Mitigación R-01 — fallo al arrancar, no al primer uso:** `VerificacionDeIdentidad` corre durante el
arranque: si el entorno **no** es `Development` y el proveedor resuelto no es
`EmpleadoActualNoConfigurado`, lanza excepción y la aplicación no levanta. Una app que no arranca es
un incidente visible; una que arranca y suplanta identidades, no.

> **Nota de diseño.** `EmpleadoActualNoConfigurado` es comportamiento productivo alojado en el
> proyecto de UI. Es válido mientras `Web` sea el único host. Si aparece un worker o un job por lotes,
> heredará la interfaz pero no este guardarraíl, y habrá que mover ambas implementaciones.

**Input validation**

La selección de empleado llega de TB-1 (navegador → circuito) y es **entrada no confiable**: el
identificador recibido debe existir en la nómina. Un `Id` inexistente se rechaza; no se confía en que
el desplegable solo ofrezca valores válidos.

**Error handling**

| Error | Manejo |
|---|---|
| Se resuelve `IEmpleadoActualProvider` fuera de `Development` | `InvalidOperationException` con mensaje que nombra RF-01 como la vía correcta (AC-06). |
| El entorno no es `Development` y quedó registrado el proveedor de desarrollo | Excepción **en el arranque** desde `VerificacionDeIdentidad`. La aplicación no levanta. |
| Se selecciona un `Id` de empleado inexistente | Se rechaza el cambio y se conserva el empleado anterior. No se persiste nada. |
| No hay ningún empleado seleccionado todavía | El proveedor lo comunica de forma explícita; los componentes muestran el selector en lugar de una lista vacía, que se leería como "no tenés solicitudes". |

**Required tests**
- [ ] **B4-T1** — sad path: con entorno `Production`, el contenedor resuelve
  `EmpleadoActualNoConfigurado` y usarlo lanza excepción (**AC-06**).
- [ ] **B4-T2** — sad path: con `Development` pero **sin**
  `Vacaciones:PermitirIdentidadDeDesarrollo`, también resuelve el que lanza (doble condición, R-01).
- [ ] **B4-T3** — con `Development` y la clave en `true`, el empleado elegido se usa como autor y
  como sujeto del listado (**AC-07**).
- [ ] **B4-T4** — sad path: con entorno `Production` y el proveedor de desarrollo forzado en el
  contenedor, el arranque falla (R-01, fallo temprano).
- [ ] **B4-T5** — `EmpleadoActualDesarrollo` está registrado como **scoped**: dos ámbitos distintos
  obtienen instancias distintas.
- [ ] **B4-T6** — sad path: seleccionar un `Id` inexistente se rechaza y conserva el anterior.
- [ ] **B4-T7** — **composición (NFR-06):** `IEmpleadoActualProvider` es la única fuente de identidad
  registrada.
- [ ] **B4-T8** — sad path: sin ningún empleado seleccionado todavía, el proveedor lo comunica de
  forma explícita y distinguible, en lugar de devolver `null` en silencio.

**Completion criterion**
Los 8 tests pasan; con `ASPNETCORE_ENVIRONMENT=Production` la aplicación no arranca con el proveedor
de desarrollo; con `Development` y la clave activa, el selector cambia el empleado por circuito.

---

## Block 5 — Reglas de dominio del alta y del listado

**Files**
- `src/GestionVacaciones.Data/Services/CalculadorDeDiasCorridos.cs` (nuevo) — nombre alineado al
  glosario de `AGENTS.md`: la unidad del dominio es "días corridos", no "días"
- `src/GestionVacaciones.Data/Services/SolicitudesService.cs` (nuevo) — crear y listar
- `src/GestionVacaciones.Data/Services/PermisosService.cs` (nuevo) — **única sede** de la decisión de
  visibilidad
- `src/GestionVacaciones.Data/Services/ErroresDeSolicitud.cs` (nuevo) — mensajes literales
- `src/GestionVacaciones.Web/Program.cs` (modificado) — registro de servicios y de `TimeProvider`

**Logic**

`CalculadorDeDiasCorridos` cuenta días **inclusivos**: del 3-ene al 5-ene son 3.

`SolicitudesService.Crear` valida en el **servidor** (mitigación R-10) y en este orden: fecha de
inicio no anterior a hoy; fecha de fin no anterior a la de inicio; y solo entonces persiste en estado
`Pendiente` con `FechaCreacion`. `SolicitudesService.ListarPropias` delega en `PermisosService` la
decisión de qué solicitudes puede ver quién, y ordena descendente por `FechaCreacion`.

`PermisosService` es la **única** sede de la decisión de visibilidad, como exige `AGENTS.md`. En este
ticket contiene una sola regla —un empleado ve sus propias solicitudes— y esa es exactamente la
razón de crearlo ahora: FEAT-001b, FEAT-001c y el ticket de aprobación van a copiar el patrón que se
siembre aquí.

**Fuente de tiempo:** `TimeProvider` inyectado, **nunca** `DateTime.Today` ni `DateTime.UtcNow`
directos. Viene en la BCL desde .NET 8, así que no suma dependencias, y sin él los tests de AC-02
dependen del reloj de la máquina y fallan al cambiar el día.

**Input validation**

| Entrada | Reglas |
|---|---|
| `FechaInicio` | `DateOnly` obligatoria. No anterior a "hoy" según `TimeProvider`. |
| `FechaFin` | `DateOnly` obligatoria. No anterior a `FechaInicio`. |
| Empleado autor | Proviene de `IEmpleadoActualProvider`, nunca de la petición. |

Fuera de alcance en este bloque, por pertenecer a otros sub-tickets: tope anual (FEAT-001b) y
superposición (FEAT-001c). Un período de duración arbitraria **se acepta** en FEAT-001a.

**Error handling**

| Error | Mensaje / manejo |
|---|---|
| Fecha de inicio anterior a hoy | `"La fecha de inicio no puede ser anterior a hoy"` — literal del PRD. No persiste. |
| Fecha de fin anterior a la de inicio | `"La fecha de fin no puede ser anterior a la fecha de inicio"` — literal del PRD. No persiste. |
| Sin empleado actual resuelto | Excepción propagada desde el proveedor; no se crea nada anónimo. |
| Se pide el listado de otro empleado | `PermisosService` lo niega. No se devuelve una lista vacía, que se confundiría con "no tiene solicitudes". |
| Fallo al persistir (constraint, conexión) | La excepción se propaga. La constraint que salta indica un bug de validación, no un error del usuario. |

**Required tests**
- [ ] **B5-T1** — días corridos inclusivos: 3-ene a 5-ene = 3.
- [ ] **B5-T2** — días corridos de un período de un solo día = 1.
- [ ] **B5-T3** — sad path: fecha de inicio anterior a hoy con `TimeProvider` fijo → mensaje literal,
  no persiste (**AC-02**).
- [ ] **B5-T4** — sad path: fecha de fin anterior a la de inicio → mensaje literal, no persiste
  (**AC-03**).
- [ ] **B5-T5** — una solicitud válida se persiste en `Pendiente` con período, días corridos y
  `FechaCreacion` (**AC-04**).
- [ ] **B5-T6** — sad path: sin empleado actual resuelto, crear lanza y no persiste.
- [ ] **B5-T7** — el listado devuelve **solo** las del empleado actual, orden descendente por
  `FechaCreacion`, cada una con su estado (**AC-05**).
- [ ] **B5-T8** — sad path: `PermisosService` niega el listado de otro empleado.
- [ ] **B5-T9** — sad path: un `DbUpdateException` de constraint se propaga y no se traga.

**Completion criterion**
Los 9 tests pasan; los tres mensajes literales coinciden **carácter por carácter** con el PRD; no
existe ninguna llamada a `DateTime.Today` ni `DateTime.UtcNow` en el proyecto Data.

---

## Block 6 — Interfaz Blazor + MudBlazor

**Files**
- `src/GestionVacaciones.Web/Components/Pages/MisSolicitudes.razor` (nuevo) — página, ruta `/`
- `src/GestionVacaciones.Web/Components/Solicitudes/FormularioDeAlta.razor` (nuevo)
- `src/GestionVacaciones.Web/Components/Solicitudes/ListadoDeSolicitudes.razor` (nuevo)
- `src/GestionVacaciones.Web/Components/Identidad/SelectorDeEmpleado.razor` (nuevo)
- `src/GestionVacaciones.Web/Components/Routes.razor` (modificado)
- `src/GestionVacaciones.Web/Components/Layout/MainLayout.razor` (modificado) — aloja el selector
- `AGENTS.md` (modificado) — se actualiza la estructura de carpetas declarada (`Data/Entidades/`,
  `Data/Services/`, `Web/Identidad/`, `Web/Configuracion/`, `Web/Components/Solicitudes/`,
  `Web/Components/Identidad/` y las subcarpetas de tests) y se listan las 9 dependencias nuevas.
  `AGENTS.md:107` lo exige, y sin esto `daw-validate-arch` auditaría contra una estructura que ya no
  describe el repositorio.

**Logic**

`FormularioDeAlta` usa `MudDatePicker` para el período y muestra los días corridos calculados por
`CalculadorDeDiasCorridos` **antes** de habilitar el envío (AC-01). **No reimplementa** ninguna
comparación de fechas: llama a `SolicitudesService` y muestra el mensaje que este devuelve
(mitigación R-10). Duplicar la regla en el `.razor` la volvería esquivable y divergente.

`ListadoDeSolicitudes` renderiza lo que devuelve `SolicitudesService.ListarPropias`, en el orden en
que llega. `SelectorDeEmpleado` se muestra solo cuando el proveedor de desarrollo está activo.

**Ningún componente inyecta `IDbContextFactory` ni `VacacionesDbContext`.** Todo pasa por los
servicios de B4 y B5.

**Input validation**

Las fechas se recogen con `MudDatePicker`, que restringe el formato. Esa restricción es
**conveniencia, no control**: la validación que decide es la del servidor (B5).

**Error handling**

| Error | Manejo |
|---|---|
| El servicio devuelve un error de validación | Se muestra el mensaje literal junto al formulario. El envío no ocurre. |
| El servicio lanza (base caída, identidad no resuelta) | Mensaje genérico al usuario y log con `EmpleadoId`. **Nunca** se muestra la traza (F-SAST-09) ni se registran nombre o correo (R-12). |
| No hay empleado seleccionado | Se muestra el selector, no un listado vacío. |
| El listado viene vacío | Estado vacío explícito: "todavía no enviaste solicitudes", distinguible de un fallo. |

**Required tests** *(bUnit)*
- [ ] **B6-T1** — con un período elegido, muestra los días corridos antes de habilitar el envío
  (**AC-01**).
- [ ] **B6-T2** — sad path: fecha de inicio anterior a hoy → muestra el mensaje literal y no invoca
  la creación (**AC-02**).
- [ ] **B6-T3** — sad path: fecha de fin anterior a la de inicio → mensaje literal, sin creación
  (**AC-03**).
- [ ] **B6-T4** — el listado renderiza en orden descendente por fecha de creación, con el estado de
  cada solicitud (**AC-05**).
- [ ] **B6-T5** — al cambiar el empleado en el selector, el listado pasa a mostrar las del nuevo
  (**AC-07**).
- [ ] **B6-T6** — sad path: si el servicio lanza, se muestra el mensaje genérico y **no** aparece la
  traza en el marcado renderizado.
- [ ] **B6-T7** — sad path: sin solicitudes, se muestra el estado vacío explícito, distinguible del
  error de B6-T6.
- [ ] **B6-T8** — ningún componente declara una inyección de `IDbContextFactory` ni de
  `VacacionesDbContext`.
- [ ] **B6-T9** — sad path: sin empleado seleccionado, se renderiza el **selector** y no el listado
  vacío. El resultado debe ser distinguible tanto del estado vacío de B6-T7 como del error de
  B6-T6: los tres se ven parecidos y significan cosas distintas.

**Completion criterion**
Los 9 tests pasan; `dotnet build` sigue con 0 advertencias; no existe ningún `MarkupString` con
entrada de usuario en el proyecto Web.

---

## Final verification

Al terminar los 6 bloques debe cumplirse:

1. `dotnet build src/GestionVacaciones.slnx` → **0 advertencias, 0 errores** (NFR-07).
2. `dotnet test src/GestionVacaciones.slnx --collect:"XPlat Code Coverage"` → **al menos 49 tests en
   verde** y cobertura de líneas, ramas y funciones **≥ 80%** sobre el código nuevo (NFR-02). El
   número es un piso, no una meta: el Bloque 1 cerró con 27 en vez de los 6 previstos, porque las
   revisiones exigieron fijar por test lo que el código ya hacía.
3. Los 7 AC del PRD tienen al menos un test que los valida y pasa.
4. Con `ASPNETCORE_ENVIRONMENT=Production` la aplicación **no arranca** con el proveedor de
   desarrollo (R-01).
5. Ningún `appsettings*.json` versionado contiene credenciales (R-02).
6. Las **cuatro** check constraints existen y rechazan filas inválidas contra SQL Server 2022 real
   (NFR-04): las tres del período y `CK_Solicitud_EstadoValido`, que rechaza un `Estado` fuera del
   rango del enum.
6b. **Ningún test de integración puede ejecutarse contra un catálogo que no termine en `_Test`.** El
   guardarraíl del fixture aborta antes de abrir la conexión, y B2-T10 y B2-T11 lo fijan. Es la
   condición que hace que la regla #0 de testing no dependa de la disciplina de quien escriba el
   próximo test.
7. `AGENTS.md` queda actualizado con las carpetas nuevas (`Data/Entidades/`, `Data/Services/`,
   `Web/Identidad/`, `Web/Configuracion/`, `Web/Components/Solicitudes/`, `Web/Components/Identidad/`
   y las subcarpetas de tests) y las 9 dependencias. **Es tarea del Block 6**, y `AGENTS.md:107` lo
   exige.

## Riesgos registrados, fuera del alcance de este ticket

- **CI de DAW en rojo.** `.github/workflows/verify.yml` y `mutations.yml` se disparan con cualquier
  cambio y ejecutan `scripts/lint_method.py`, `scripts/check_versions.py`,
  `scripts/verify_install.sh` y `scripts/mutate.py`, que **no existen** en este repositorio. Hoy no
  corren porque no hay remoto configurado; al conectar uno, todo PR nace en rojo. **Decisión del
  usuario: no tocarlos en FEAT-001a.** Ticket futuro, junto con el workflow que sí deberá compilar y
  testear la solución .NET.
- **`package.json` declara este repositorio como el paquete npm `daw`**, sin campo `files` ni
  `.npmignore`. Cualquier empaquetado incluiría también la aplicación .NET. No se toca en este
  ticket.
- **NFR-01 sin verificación de carga.** Diferido a un ticket de performance, como se declara en la
  tabla de cobertura.
- **NFR-03 por verificación manual.** bUnit no abre navegadores.
- **NFR-04 verificado solo donde hay motor.** Al pasar de Testcontainers a la instancia del entorno
  de desarrollo (`Spec loops 2`), la evidencia de las cuatro check constraints existe únicamente en
  máquinas con acceso a ese SQL Server. En cualquier otra, esos tests se saltean y la invariante
  queda sin verificar. El CI futuro necesitará SQL Server disponible, o volver a contenedores.
- **La IP del host de Windows cambia entre reinicios.** La cadena de test se toma de
  `VACACIONES_CONNECTION_TEST` o de user-secrets precisamente por eso; si los tests de integración
  empiezan a saltearse sin explicación, esa es la primera causa a revisar.
