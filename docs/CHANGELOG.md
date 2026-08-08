# Changelog — Gestión de vacaciones

Registro de los cambios de la **aplicación**. El formato sigue
[Keep a Changelog](https://keepachangelog.com/es/1.1.0/) y las versiones,
[Versionado Semántico](https://semver.org/lang/es/).

> **Por qué este archivo y no el `CHANGELOG.md` de la raíz.** Aquel versiona **DAW**, el método
> con el que se construye esta aplicación: sus fases, sus gates y las herramientas que soporta.
> Son dos productos con ritmos distintos, y un archivo que promete versionar uno no debería
> contener el historial del otro.

---

## [Unreleased]

### Added

- **FEAT-001a — Alta de solicitudes de vacaciones, con validación de fechas y listado propio.**
  Un empleado puede registrar una solicitud indicando el período, ver los días corridos que
  abarca antes de enviarla, y consultar el historial de sus propias solicitudes con el estado de
  cada una.

  - **Solución .NET 10** con Blazor Server + MudBlazor: `Directory.Build.props` en la raíz con
    nullable habilitado y `TreatWarningsAsErrors`, y la cadena de conexión resuelta **fuera del
    repositorio** —variable de entorno o user-secrets—, nunca con un valor por defecto.
  - **Modelo `Empleado`/`Solicitud`** en SQL Server 2022 con EF Core 10, accedido siempre por
    `IDbContextFactory`. Cuatro *check constraints* hacen **imposible persistir** un período
    invertido, cero días corridos, un conteo que no coincida con el período o un estado fuera del
    enum. Verificadas contra un motor real, porque el proveedor en memoria las ignora.
  - **Identidad del empleado actual** tras una única interfaz, con **triple guardarraíl**: entorno
    `Development`, una clave de configuración que no viaja en el artefacto publicado, y una
    condición de compilación que un binario de Release no puede sortear con variables de entorno.
    Fuera de desarrollo la aplicación **no arranca** con el proveedor de desarrollo.
  - **Reglas de dominio en un punto único:** el conteo de días corridos, la validación de fechas
    en el servidor y `PermisosService` como sede exclusiva de la decisión de quién ve las
    solicitudes de quién. El tiempo entra por `TimeProvider` inyectado.
  - **Interfaz** con MudBlazor donde «sin empleado seleccionado», «sin solicitudes» y «error» son
    tres estados distinguibles en pantalla, y no tres formas de mostrar lo mismo.

  198 tests, cobertura del 94,6% en líneas y 96,3% en funciones.

- **FEAT-001b — Imputación de días por año calendario, tope anual y saldo.** El tope de 14 días
  pasa de ser un número implícito a una regla que el sistema hace cumplir, y el empleado ve su
  saldo del año en curso —y el del otro año, cuando el período elegido cruza el 31 de
  diciembre— antes de enviar la solicitud.

  - **`ImputacionPorAnio`**, función pura y estática hermana de `CalculadorDeDiasCorridos`: reparte
    los días de un período entre los años calendario que abarca, delegando siempre el conteo en la
    misma fórmula que ya persiste `Solicitud.DiasCorridos`.
  - **`SaldoService`** calcula `SaldoDelAnio` (usados/reservados y disponibles) del año en curso o
    de hasta dos años consecutivos, filtrando en SQL y repartiendo por año en memoria; nunca se
    degrada a saldo cero ante un fallo de identidad, de permisos o de persistencia — un cero y un
    error se ven igual en pantalla y significan lo contrario.
  - **`TopeAnual.Dias = 14`** es la única declaración del tope en todo el código fuente, verificado
    por un escaneo estructural que rompe si aparece una segunda.
  - **El alta hace cumplir el tope:** `SolicitudesService.CrearAsync` valida el saldo de cada año
    afectado después de las validaciones de fecha existentes y antes de persistir, dentro de una
    transacción serializable que cierra la carrera entre dos envíos simultáneos del mismo empleado.
    Un período de más de dos años se rechaza sin consultar la base.
  - **El saldo en pantalla:** un componente nuevo, antes del formulario de alta, muestra el saldo
    del año en curso siempre y el del otro año cuando el período lo cruza, con un cuarto estado
    —sin ninguna cantidad— cuando el cálculo falla. El botón de enviar sigue habilitado solo con
    las dos fechas presentes: la interfaz muestra el resultado de la regla, nunca la decide.
  - **Índice `IX_Solicitud_EmpleadoId_Estado_FechaInicio`**, para que el cálculo del saldo no
    escanee las solicitudes del empleado completas.

  50 tests nuevos sobre los 198 de FEAT-001a (269 en total), cobertura del 95,5% en líneas y 92,8%
  en ramas sobre el proyecto, 100% sobre el dominio nuevo. SAST PASSED, 0 vulnerabilidades.

- **FEAT-001c — No superposición de períodos.** Un empleado no puede tener dos solicitudes vigentes
  sobre fechas que se tocan: la tercera y última regla de negocio de PRD-001 sobre el esqueleto que
  entregó FEAT-001a.

  - `SolicitudesService.CrearAsync` valida, después de las fechas y antes del tope, que el período no
    se superponga con otra solicitud `Pendiente` o `Aprobada` del mismo empleado. Las solicitudes
    `Rechazada` no bloquean fechas que quedaron libres, y dos períodos consecutivos siguen siendo
    posibles.
  - La consulta reutiliza el índice y la transacción serializable que ya existían: **esta entrega no
    agrega ninguna migración**.
  - Bajo dos envíos concurrentes con fechas solapadas, un conflicto de serialización del motor se
    resuelve con un reintento único y dirigido —repite solo la pregunta de solapamiento, nunca todo
    el flujo—, para que la persona que pierde la carrera reciba el mensaje de superposición y no una
    excepción cruda.
  - `EstadosDeSolicitud.Vigentes` pasa a ser la fuente única de qué estados de una solicitud cuentan
    —para el saldo y para la superposición por igual—, reemplazando un literal que antes vivía
    duplicado.

  15 tests nuevos sobre los 269 de FEAT-001a+b (284 en total), cobertura 97,97%-100% en líneas y
  100% en ramas sobre el código nuevo. SAST PASSED, 0 vulnerabilidades.

Con FEAT-001c cierran las tres reglas de negocio de PRD-001 sobre solicitudes de vacaciones: fechas
válidas, tope anual y no superposición.

- **FEAT-002 — Aprobación y rechazo de solicitudes por el manager.** El manager —o el designado en
  quien delega— aprueba o rechaza las solicitudes `Pendientes` de su equipo, con trazabilidad
  completa de quién resolvió, cuándo y por qué.

  - **`Solicitud` gana tres columnas** (`ResueltoPorId`, `FechaResolucion`, `MotivoDeRechazo`)
    respaldadas por una quinta *check constraint*: una fila `Pendiente` con datos de resolución, o
    una `Aprobada`/`Rechazada` sin ellos, es imposible de persistir.
  - **`PermisosService` gana el organigrama**, separando explícitamente dos preguntas que el PRD
    trata como relacionadas pero no son la misma: quién puede *ver* una solicitud ajena (el
    titular, su manager, el designado de su manager) y quién puede *resolverla* (manager o
    designado únicamente — la autoaprobación no está permitida, ni siquiera ante un dato de
    organigrama anómalo). Las consultas nunca traen nombre ni correo del equipo, solo los
    identificadores necesarios para decidir.
  - **`SolicitudesService.ResolverAsync`** aprueba o rechaza dentro de una transacción serializable,
    con el mismo reintento dirigido que FEAT-001c ya usa para su propia carrera de concurrencia: dos
    resoluciones simultáneas de la misma solicitud terminan siempre en una aplicada y la otra
    informada como ya resuelta, nunca en una excepción cruda.
  - **Listado de pendientes del equipo**, ordenado por antigüedad (la que más tiempo lleva
    esperando, primero), que no consulta la base cuando quien pregunta no tiene autoridad sobre
    nadie.
  - **Pantalla de autorizaciones**, alcanzable por un link que solo aparece para quien tiene equipo
    a cargo — la decisión sale siempre del dominio, nunca de una consulta propia del componente. El
    motivo de un rechazo se muestra siempre como texto plano, nunca interpretado.

  15 tests nuevos sobre los 284 de FEAT-001a+b+c (338 en total), cobertura del 96,29% en líneas y
  93,28% en ramas. SAST PASSED, 0 vulnerabilidades Critical/High/Medium (1 hallazgo LOW no
  bloqueante, documentado y sin corregir en esta entrega).

Con FEAT-002 quedan completos los tres requisitos funcionales de PRD-001 que no entraban en
FEAT-001: aprobar, rechazar y listar las solicitudes pendientes del equipo.

### Fixed

- **FIX-001 — El link "Autorizaciones" nunca llegaba a aparecer.** El menú preguntaba una única
  vez, al arrancar el circuito y antes de que el empleado eligiera identidad, si correspondía
  mostrarlo — y nunca volvía a preguntar. `IEmpleadoActualProvider` gana un evento que avisa cuando
  la identidad cambia, y el menú se suscribe para reevaluar la pregunta cuando corresponde, en vez
  de una sola vez al arrancar.

### Fuera de alcance de esta entrega

Las notificaciones al empleado cuando su solicitud se resuelve. La verificación de carga —p95 < 3 s
con 50 concurrentes— queda diferida a un ticket de performance propio; FEAT-001b, FEAT-001c y
FEAT-002 entregan la condición estructural (los índices), no la medición.
