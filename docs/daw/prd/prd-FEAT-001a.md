# PRD FEAT-001a: Andamiaje, identidad del empleado y alta de solicitud con validación de fechas y listado propio

| Field | Value |
|-------|-------|
| Ticket | FEAT-001a |
| Tracker | none |
| Date | 2026-08-01 |
| PRD loops | 0 |

> **Sub-ticket `a` de FEAT-001** (índice en `prd-FEAT-001.md`), que a su vez recorta
> `docs/daw/prd/PRD.md` (PRD-001). Los identificadores `RF-xx` / `AC-xx` citados entre paréntesis
> son los del PRD-001; los `FR-xx` / `AC-xx` de este archivo son los de este sub-ticket.

## Context and Problem

El repositorio no contiene todavía código .NET: no hay solución, ni proyectos, ni modelo de datos.
Sin ese esqueleto no hay dónde apoyar ninguna regla de negocio del producto.

Este sub-ticket entrega la rebanada vertical más chica que se sostiene sola: un empleado abre la
aplicación, registra una solicitud de vacaciones con un período válido, y la ve en su listado. Es
poco funcionalmente, y es exactamente lo que hace falta para que los dos sub-tickets siguientes
—tope anual y no superposición— sean cada uno una regla de negocio y nada más.

Como PRD-001 RF-01 (autenticación OAuth) queda fuera, hay que resolver de quién es cada solicitud.
La respuesta acotada de este ticket es un proveedor único de identidad cuya implementación
productiva se niega a inventar un empleado.

**Persona:** el **Empleado**, que registra sus solicitudes y consulta las que ya envió.

## Goals

- Dejar en pie la solución .NET, sus proyectos y el modelo de datos con su migración inicial.
- Que un empleado registre una solicitud con un período válido y la vea persistida.
- Que la identidad del empleado tenga un único punto de resolución, reemplazable por OAuth sin tocar
  a ningún llamador.
- No permitir que el sustituto de identidad funcione fuera de desarrollo.

## Functional Requirements

- FR-01: El sistema debe registrar una solicitud de vacaciones con su fecha de inicio, su fecha de
  fin, su cantidad de días corridos y su fecha de creación, en estado "Pendiente".
  *(PRD-001 RF-02)*
- FR-02: El sistema debe calcular y mostrar la cantidad de días corridos del período antes de
  permitir el envío. *(PRD-001 RF-02, AC-03)*
- FR-03: El sistema debe rechazar todo período cuya fecha de inicio sea anterior a la fecha actual,
  o cuya fecha de fin sea anterior a la fecha de inicio. *(PRD-001 RF-02, AC-04)*
- FR-04: El sistema debe listar únicamente las solicitudes propias del empleado, ordenadas de forma
  descendente por fecha de creación, cada una con su estado actual. *(PRD-001 RF-08, AC-10.1,
  AC-10.2)*
- FR-05: El sistema debe resolver la identidad del empleado actual a través de un único proveedor, y
  su implementación fuera del entorno `Development` no debe devolver ningún empleado.
  *(sustituto acotado de PRD-001 RF-01)*

## Non-Functional Requirements

- NFR-01: Crear una solicitud y listar las solicitudes propias deben responder en menos de 3
  segundos en el percentil 95 (p95), bajo una carga concurrente de al menos 50 usuarios.
  *(PRD-001 RNF-04)*
- NFR-02: La cobertura de líneas, ramas y funciones sobre el código nuevo debe ser mayor o igual al
  80%.
- NFR-03: La interfaz debe funcionar sin errores de renderizado ni de funcionalidad en las 2 últimas
  versiones estables de Chrome, Edge y Firefox en escritorio. *(PRD-001 RNF-03)*
- NFR-04: Las invariantes del período deben estar reforzadas con check constraints en la base de
  datos: deben ser 0 las filas persistibles con fecha de fin anterior a la fecha de inicio, o con
  una cantidad de días corridos menor o igual a 0.
- NFR-05: El acceso a datos debe usar `IDbContextFactory<VacacionesDbContext>` en el 100% de las
  operaciones: 0 registros de `DbContext` mediante `AddDbContext`.
- NFR-06: La resolución de la identidad del empleado actual debe concentrarse en 1 única interfaz:
  0 llamadores que obtengan el empleado por otra vía.
- NFR-07: El proyecto debe compilar con 0 advertencias, con `TreatWarningsAsErrors` activo y
  nullable reference types habilitado.

## Acceptance Criteria

- AC-01: WHEN el empleado selecciona una fecha de inicio y una fecha de fin, THE sistema SHALL
  calcular y mostrar la cantidad de días corridos del período antes de habilitar el envío. *(FR-02)*
- AC-02: IF la fecha de inicio es anterior a la fecha actual, THEN THE sistema SHALL impedir el
  envío y mostrar el mensaje "La fecha de inicio no puede ser anterior a hoy". *(FR-03)*
- AC-03: IF la fecha de fin es anterior a la fecha de inicio, THEN THE sistema SHALL impedir el
  envío y mostrar el mensaje "La fecha de fin no puede ser anterior a la fecha de inicio". *(FR-03)*
- AC-04: WHEN el empleado envía una solicitud cuyo período supera las validaciones de fecha, THE
  sistema SHALL persistirla en estado "Pendiente" con su período, su cantidad de días corridos y su
  fecha de creación. *(FR-01)*
- AC-05: WHEN el empleado entra a la pantalla de sus solicitudes, THE sistema SHALL listar
  únicamente las solicitudes de ese empleado, ordenadas de forma descendente por fecha de creación,
  cada una con su estado actual. *(FR-04)*
- AC-06: WHILE la aplicación corre en un entorno distinto de `Development`, THE sistema SHALL lanzar
  una excepción al resolver el empleado actual, en lugar de devolver un empleado por defecto.
  *(FR-05)*
- AC-07: WHILE la aplicación corre en `Development`, WHEN el usuario selecciona un empleado de la
  nómina sembrada, THE sistema SHALL usar ese empleado como autor de toda solicitud creada y como
  sujeto del listado mostrado. *(FR-05)*

## Out of Scope

- **El tope anual de 14 días y el saldo de días disponibles.** Los entrega FEAT-001b. Este
  sub-ticket acepta una solicitud de cualquier duración.
- **La validación de superposición entre períodos.** La entrega FEAT-001c. Este sub-ticket acepta
  dos solicitudes del mismo empleado con fechas solapadas.
- **PRD-001 RF-01 (autenticación OAuth).** Sustituida por el proveedor de FR-05. Ticket futuro.
- **PRD-001 RF-05 y RF-06 (aprobar y rechazar).** Toda solicitud creada nace y permanece en estado
  "Pendiente". Ticket futuro.
- **PRD-001 RF-07.x (notificaciones in-app y por correo).** Ticket futuro.
- **PRD-001 AC-10.4 (datos de quién autorizó o rechazó).** Sin flujo de aprobación esos campos
  estarían siempre vacíos.
- **PRD-001 RF-09 y AC-11 (denegar con 403 el acceso a datos de otro empleado).** Depende de la
  identidad real de RF-01.
- **Cancelar o editar una solicitud enviada.** Fuera del alcance del producto.
- **Dispositivos móviles.** Fuera del alcance del producto.
- **La vista del manager o del designado.** Este sub-ticket entrega solo la vista del propio
  empleado.

## Risks and Mitigations

- **El resultado de este sub-ticket llega a producción sin las reglas de negocio** de FEAT-001b y
  FEAT-001c, permitiendo solicitudes de 300 días o solapadas → mitigación: queda escrito en el PRD
  padre que `a` no debe desplegarse sin `b` y `c`, y el estado "Pendiente" implica que ninguna
  solicitud produce efectos hasta que exista el flujo de aprobación.
- **El sustituto de identidad (FR-05) llega a producción** → mitigación: la implementación se
  registra condicionada al entorno y la variante productiva lanza excepción en vez de elegir un
  empleado por defecto (AC-06), de modo que el fallo sea ruidoso e inmediato.
- **Un `DbContext` compartido entre componentes Blazor concurrentes** rompe con "A second operation
  was started on this context instance" → mitigación: NFR-05 impone `IDbContextFactory` en todas las
  operaciones, y cada operación abre y cierra el suyo.
- **La nómina de desarrollo no se puede sembrar** porque las relaciones manager/designado son
  autorreferencias circulares → mitigación: la carga se hace con `SeedDatos` en tiempo de ejecución
  y no con `HasData`, que no puede ordenar los `INSERT`.
- **El cálculo de días corridos difiere entre la interfaz y el servicio**, mostrando un número y
  persistiendo otro → mitigación: el cálculo reside en el dominio y la interfaz lo consume, sin
  reimplementarlo.

## Dependencies

- **`docs/daw/prd/PRD.md` (PRD-001)** — PRD de producto. Los mensajes literales de AC-02 y AC-03
  provienen de su criterio AC-04.
- **`docs/daw/prd/prd-FEAT-001.md`** — PRD padre e índice de la división.
- **.NET 10 LTS y C# 14** — runtime y lenguaje. Declarado en `AGENTS.md` → Stack.
- **SQL Server 2022 + Entity Framework Core 10** — persistencia de `Empleado` y `Solicitud`,
  migración inicial y check constraints de NFR-04. Declarado en `AGENTS.md` → Stack.
- **Blazor Server + MudBlazor** — formulario de alta, listado y selector de empleado. Declarado en
  `AGENTS.md` → Stack.
- **`SeedDatos`** — la nómina de desarrollo (Ana, Diego, Bruno y Carla) que alimenta el selector de
  AC-07. Se crea en este mismo sub-ticket; no existe todavía.
- **Sin dependencia del proveedor de identidad OAuth** ni **del servicio de correo**: ambos siguen
  como "a confirmar" en el PRD-001 y quedan fuera de alcance.
