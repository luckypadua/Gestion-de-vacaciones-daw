# AGENTS.md — project context

> **DAW template.** Fill in the `[...]` with what is true of YOUR project and delete what does not
> apply. This file describes **the project**; **the process** is DAW's job (phases, gates, when to
> test, when to commit). Do not mix the two: process rules written here compete with the pipeline's.
>
> It is **tool-agnostic on purpose**: Claude Code reads it through the import in `CLAUDE.md`, Codex
> CLI, Copilot CLI, Cursor and OpenCode read it directly, and Gemini CLI gets it through
> `GEMINI.md`. The same file serves whichever tool you open the repo with — which is the point:
> porting the pipeline to another tool must not mean rewriting what your project is.

---

## Language

**Always respond in the language the user writes in.** Write every artifact you produce — PRDs,
specs, ADRs, reports, commit messages, status lines — in that same language, regardless of the
language these instructions are written in.

If this project has a fixed working language, state it here and use it instead:

> Working language: `Español — escribir todos los artefactos (PRDs, specs, ADRs, informes, mensajes de commit, status lines) en español`

---

## What this project is

Sistema web de gestión de vacaciones del personal: el empleado solicita sus días, consulta su saldo
del año en curso y el historial de sus solicitudes; su manager —o el designado en quien delega—
aprueba o rechaza esas solicitudes con trazabilidad completa.

**Reference PRD:** `docs/daw/prd/PRD.md`

---

## Stack

**This is the only place the stack lives.** DAW reads it from here and generates no derived file.
Fill it in even if the repo is empty: without a stack there is nothing to plan or implement against.

If the repo already has code and this section is empty, DAW will detect the stack from your config
files and **propose the text for you to paste here**. You always confirm it.

| Field | Value |
|-------|-------|
| Language | C# 14 |
| Runtime | .NET 10 LTS (`net10.0`) |
| Framework | Blazor Server + MudBlazor |
| Database | SQL Server 2022 + Entity Framework Core 10 · instancia local `NTKLUCIANOE\SQL2022` · base **`GestionVacacionesV2`** |
| Test runner | `dotnet test src/GestionVacaciones.slnx` (proyecto `tests/GestionVacaciones.Tests/`) |
| Linter / formatter | Analizadores de .NET vía `Directory.Build.props`: nullable habilitado + `TreatWarningsAsErrors` |
| Package manager | NuGet (`dotnet restore`) |

**Restricciones del stack:**

- Mantener siempre esta combinación de versiones. **No mezclar** versiones distintas de .NET, EF Core
  o SQL Server.
- La cadena de conexión vive **fuera del repositorio**: user-secrets (`ConnectionStrings:Vacaciones`)
  en desarrollo, variable de entorno `VACACIONES_CONNECTION` fuera de desarrollo (tiene precedencia).
- Desde WSL, `NTKLUCIANOE\SQL2022` no resuelve: hay que conectarse por la IP del host Windows
  (`ip route show default | awk '{print $3}'`), con TCP/IP habilitado y puerto estático 1433. Esa IP
  cambia entre reinicios en modo NAT.

---

## Architecture conventions

**DAW validates your code against this section** during the CODE phase, via `daw-validate-arch`.
Leave it empty and that validation has nothing to compare against, so it stops being worth running.

- **Folder structure:**
  - `src/GestionVacaciones.slnx` — solución (formato `.slnx`, el default de .NET 10)
  - `src/GestionVacaciones.Data/` — dominio y persistencia
    - `Entidades/` — `Empleado`, `Solicitud`, `EstadoSolicitud`
    - `Migrations/` — migraciones de EF Core; **son ellas** las que definen el esquema
    - `Services/` — reglas de negocio y única vía de acceso a datos: `SolicitudesService`,
      `SaldoService`, `PermisosService`, `EmpleadosService`, `CalculadorDeDiasCorridos`,
      `ImputacionPorAnio`, `TopeAnual`, `EstadosDeSolicitud`, `ErroresDeSolicitud`,
      `IEmpleadoActualProvider`
    - raíz — `VacacionesDbContext`, `VacacionesDbContextFactory` (tiempo de diseño), `SeedDatos`
  - `src/GestionVacaciones.Web/` — frontend Blazor + MudBlazor y punto de arranque
    - `Components/` — `App.razor`, `Routes.razor`, `_Imports.razor`
      - `Layout/` — `MainLayout.razor`
      - `Pages/` — páginas enrutadas (`MisSolicitudes.razor`, ruta `/`)
      - `Solicitudes/` — componentes del alta y del listado
      - `Identidad/` — componentes de la elección del empleado actual
    - `Configuracion/` — resolución de la cadena de conexión
    - `Identidad/` — implementaciones de `IEmpleadoActualProvider` y su verificación de arranque
    - `wwwroot/`, `Properties/`
  - `tests/GestionVacaciones.Tests/` — proyecto de test único, con una subcarpeta por preocupación:
    `Andamiaje/`, `Persistencia/`, `Identidad/`, `Dominio/`, `Componentes/`. El test vive **junto a
    la preocupación que verifica**, no junto al tipo que la implementa.
  - `Directory.Build.props` — propiedades comunes (`net10.0`, C# 14, nullable, `TreatWarningsAsErrors`).
    Vive en la **raíz** y no en `src/`: desde `src/` el proyecto de `tests/` no lo heredaría.
- **Layer separation:** la UI Blazor no consulta la base directamente; pasa siempre por los servicios
  de `GestionVacaciones.Data/Services/`. Quién puede ver o resolver las solicitudes de quién se
  decide **solo** en `PermisosService`, en ningún otro lugar.
- **Acceso a datos:** siempre con `IDbContextFactory<VacacionesDbContext>`, **nunca** con
  `AddDbContext`. En Blazor Server un `DbContext` scoped vive todo el circuito y dos componentes que
  consultan a la vez lo usan concurrentemente: EF lanza *"A second operation was started on this
  context instance"*. Cada operación abre y cierra el suyo.
- **Invariantes de dominio:** se refuerzan en la base con check constraints, no solo en C# — un
  rechazo sin motivo o un período invertido deben ser imposibles de persistir.
- **Punto único de reglas:** el tope anual de días y las reglas de cálculo del saldo viven en un
  único punto, para poder adaptarlos si cambia la normativa.
- **Esquema:** lo definen las migraciones de EF Core. La data de desarrollo se carga con `SeedDatos`,
  no con `HasData` (las auto-referencias manager/designado son circulares y `HasData` no puede
  ordenar los `INSERT`).
- **Error handling:** nada de catch silencioso. Los mensajes de validación que ve el usuario son los
  literales definidos en los criterios de aceptación del PRD.
- **Naming:** convenciones estándar de C#/.NET — tipos, archivos `.cs` y componentes `.razor` en
  PascalCase; miembros privados en `_camelCase`.
- **Dependencies:** no incorporar librerías nuevas sin justificarlo en la spec; el UI se construye
  con MudBlazor, no con otro sistema de componentes. Las versiones se fijan **exactas**: con
  `TreatWarningsAsErrors` activo, cualquier advertencia de compatibilidad rompe el build.

  **9 referencias, 8 paquetes** (`Microsoft.EntityFrameworkCore.Design` se referencia dos veces):

  | Paquete | Versión | Proyecto | Para qué |
  |---|---|---|---|
  | `MudBlazor` | 9.7.0 | Web | El sistema de componentes del UI. No hay otro. |
  | `Microsoft.EntityFrameworkCore.SqlServer` | 10.0.10 | Data | Proveedor de SQL Server 2022. |
  | `Microsoft.EntityFrameworkCore.Design` | 10.0.10 | Data y Web, con `PrivateAssets="all"` | `dotnet ef migrations`. `dotnet ef` la exige también en el proyecto de **arranque**. |
  | `xunit.v3` | 3.2.2 | Tests | Framework de test. |
  | `Microsoft.NET.Test.Sdk` | 18.8.1 | Tests | Requerido por el runner. |
  | `xunit.runner.visualstudio` | 3.1.5 | Tests | Descubrimiento de tests. |
  | `bunit` | 2.8.6 | Tests | Renderiza componentes Blazor en memoria. **No abre navegador**: la compatibilidad entre navegadores se verifica a mano. |
  | `coverlet.collector` | 10.0.1 | Tests | Sin él, `dotnet test --collect:"XPlat Code Coverage"` no emite ningún número de cobertura. |

  La herramienta global `dotnet-ef` tiene que estar en **10.0.10 o superior**: con la 10.0.9 las
  migraciones no se generan contra estos paquetes.

---

## Code conventions

- Nullable reference types habilitado: corregir todas las advertencias de nulabilidad, no silenciarlas.
- `TreatWarningsAsErrors` está activo — cualquier advertencia rompe el build.
- Componentes Blazor: respetar la arquitectura de componentes y los estilos definidos con MudBlazor.
- Autenticación y autorización por roles: no deshabilitar validaciones de seguridad (TLS,
  autenticación, roles) ni siquiera temporalmente para probar.
- Documentar las funcionalidades nuevas y mantener actualizado este `AGENTS.md`.

---

## What NOT to do in this project

This section is worth its weight in gold: it is where the scars go, the things that already went
wrong once.

- **Nunca** almacenar credenciales en el código fuente ni en el repositorio: el repo es público.
- **Nunca** modificar la base de datos directamente sin usar migraciones.
- **No mezclar la base v1 con la v2:** `GestionVacaciones` pertenece a la v1 y tiene su propio
  historial de migraciones. La base de la v2 es `GestionVacacionesV2`.
- **Nunca** registrar el `DbContext` con `AddDbContext` para acceso a datos (ver el porqué en
  *Architecture conventions*).
- **No** usar `HasData` para la data de desarrollo: las relaciones manager/designado son circulares.
- **Nunca** declarar en `appsettings.Development.json` una clave que habilite algo. Ese archivo **viaja
  en el artefacto publicado**, y solo se carga cuando el entorno ya es `Development`: una clave ahí no
  es una segunda condición, es una consecuencia de la primera. Ya pasó una vez —la clave que habilita
  el sustituto de identidad sin credencial— y convertía un `ASPNETCORE_ENVIRONMENT` mal puesto en una
  suplantación completa. Las claves de desarrollo van en `Properties/launchSettings.json`, que está
  versionado y **no** se publica.
- **No** dar por independientes dos condiciones sin comprobar de dónde sale cada una. Si las dos las
  aporta la configuración, las controla quien controla el entorno de ejecución: son una sola.
- **Nunca** usar `MarkupString` en el proyecto Web: es la vía por la que entra HTML sin escapar. La
  prohibición es total, no «solo con entrada de usuario»: distinguir cuál es cuál exige leer el
  código, y esa lectura es la que no ocurre en la revisión apurada.
- **No** darle reloj propio a un componente `.razor` —ni `TimeProvider` inyectado ni `DateTime.Today`—
  ni reimplementar en el marcado una regla que valida el servidor. Un formulario que decide por su
  cuenta si un período sirve deja la regla esquivable desde el cliente y divergente del servicio.
  Mostrar el resultado de una regla es tarea de la interfaz; decidirla, no.
- **No** distinguir «sin identidad», «sin datos» y «error» solo en el código: las tres se ven
  parecidas en pantalla y significan cosas opuestas. Una lista vacía devuelta ante un fallo le dice
  al empleado «no enviaste solicitudes» mientras la base está caída.
- **No** deshabilitar validaciones de seguridad (TLS, autenticación, roles).
- **No** mezclar versiones distintas de .NET, EF Core o SQL Server.
- **Nunca** usar `dotnet run` en producción: se publica con `dotnet publish -c Release`, con HTTPS y
  certificados TLS, detrás de un proxy inverso (IIS o Nginx).
- **No** dar por buena una migración fallida: revertirla (`dotnet ef migrations remove` o
  `dotnet ef database update <MigracionAnterior>`), corregirla y volver a aplicarla.

---

## Domain glossary

The terms specific to your product, so the agent uses them correctly instead of inventing synonyms.

- **Empleado:** persona que solicita vacaciones y consulta su saldo, el estado y el historial de sus
  propias solicitudes.
- **Manager:** persona que aprueba o rechaza las solicitudes de los empleados a su cargo.
- **Designado:** persona en la que el manager delega la aprobación/rechazo cuando no está disponible;
  sobre ese equipo tiene exactamente las mismas capacidades que el manager.
- **Solicitud:** pedido de vacaciones de un empleado, con período (fecha de inicio y fecha de fin) y
  cantidad de días corridos. Una vez enviada no se puede editar ni cancelar.
- **Estado:** el de una solicitud — `Pendiente`, `Aprobada` o `Rechazada`. Un rechazo exige siempre un
  motivo.
- **Días corridos:** los días del período se cuentan corridos (calendario), no hábiles.
- **Tope anual:** 14 días por empleado y por año calendario. Sin prorrateo por fecha de ingreso y sin
  arrastre al año siguiente.
- **Saldo:** días disponibles del año en curso = 14 − (días tomados + aprobados + **pendientes**).
  Las solicitudes pendientes reservan saldo. Se reinicia a 14 el 1 de enero.
- **Superposición:** coincidencia total o parcial de fechas con otra solicitud del mismo empleado en
  estado Pendiente o Aprobada; el sistema la impide.
- **Historial:** registro de las solicitudes de un empleado y de quién y cuándo las resolvió, con el
  motivo en caso de rechazo.

---

> ℹ️ **What does NOT belong in this file, because DAW provides it:** the order work happens in, when
> the spec gets written, when tests run, when to commit, what it takes to move between phases. All
> of that lives in `.daw/` and applies on its own.

<!-- BEGIN DAW (managed by DAW — do not edit by hand) -->
# DAW — Dilux Agentic Workflow

This repo uses **DAW**: an agent-driven development pipeline with the phases
`CLASSIFY → DEFINE → PLAN → CODE → VERIFY → RELEASE`.

Before answering, read `.daw/orchestrator.md` and run its Boot Sequence. It is a strict state
machine: it decides what you are allowed to do based on the phase recorded in `.daw-state.json`.

The project's own context — stack, architecture, domain — is elsewhere in this file. It lives here,
in `AGENTS.md`, and not in any one tool's file, on purpose: it is tool-agnostic and comes along
unchanged when the pipeline is ported to another agent.
<!-- END DAW -->
