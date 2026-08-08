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

### Fuera de alcance de esta entrega

El tope anual de 14 días y el cálculo del saldo (FEAT-001b), la detección de superposición de
períodos (FEAT-001c), y la aprobación o rechazo por parte del manager. La verificación de carga
—p95 < 3 s con 50 concurrentes— queda diferida a un ticket de performance propio.
