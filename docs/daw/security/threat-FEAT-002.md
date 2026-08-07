# Modelo de amenazas — FEAT-002

| Campo | Valor |
|---|---|
| Ticket | FEAT-002 |
| Spec | `docs/daw/specs/spec-FEAT-002.md` |
| PRD | `docs/daw/prd/prd-FEAT-002.md` |
| Modelo previo | `docs/daw/security/threat-FEAT-001c.md` — sus riesgos R-01 a R-21 **siguen vigentes** |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §3 |
| Fecha | 2026-08-07 |
| Resultado | **PASSED** |

> **Numeración continua.** Los componentes siguen desde C15 y los riesgos desde R-22, que es donde
> quedó FEAT-001c.

---

## 1. Clasificación de los datos (F-TM-05)

| Dato | Clasificación | Novedad de este ticket |
|---|---|---|
| `ResueltoPorId`, `FechaResolucion` | **PII** — identifica quién decidió y cuándo sobre otra persona | Nuevo |
| `MotivoDeRechazo` | **PII, texto libre** — quien lo escribe (el manager) puede incluir más detalle del estrictamente necesario | Nuevo, y el de mayor superficie de este ticket: es el único campo de texto libre que un usuario escribe sobre *otra* persona en toda la aplicación |
| El período y el estado de una solicitud, visto por el manager | **PII** — ya clasificado en FEAT-001a, pero **primera vez que un empleado distinto del titular lo ve de verdad** (hasta acá la frontera existía en el código, no se cruzaba) | Cruce real, no solo estructural |
| El nombre del empleado, visto por el manager en el listado de su equipo | **PII** — necesario para que el listado sea utilizable (FR-05) | Nuevo |

**El motivo de rechazo es el dato que ordena este análisis.** A diferencia del saldo (FEAT-001b, un
número derivado) o las fechas (estructuradas), es texto libre que una persona escribe sobre otra sin
ninguna restricción de contenido — el mismo tipo de riesgo que R-05 de FEAT-001a ya aceptó para
«identidades y períodos de licencia», ampliado ahora a lo que el manager decida escribir.

### Cifrado exigido (F-TM-07)

| Tramo | Exigencia |
|---|---|
| Navegador → servidor | Sin cambios respecto de FEAT-001a/b/c |
| Host → SQL Server | Sin cambios respecto de FEAT-001a/b/c |
| En reposo | **Sin superficie nueva de cifrado.** Las tres columnas nuevas son texto/fecha/id planos en la
misma tabla `Solicitudes`, con la misma postura que el resto de sus columnas (R-09 de FEAT-001a,
riesgo aceptado de PII sin cifrado en reposo, no se agrava — mismas condiciones de revisión) |

---

## 2. Fronteras de confianza (F-TM-02)

| # | Frontera | Qué la cruza | Nivel de confianza |
|---|---|---|---|
| **TB-1** a **TB-6** | Las de FEAT-001a/b/c | — | Sin cambios |
| **TB-5** (ya existía) | Empleado A → datos del empleado B | **Se cruza de verdad por primera vez.** FEAT-001b la declaró "ensanchada" cuando `SaldoService` la dejó estructuralmente lista; hasta FEAT-002 nadie distinto del titular veía el período o el estado de una solicitud ajena. Ahora un manager sí lo hace, para su equipo |
| **TB-7** | Manager/designado → escribe el estado de una solicitud ajena | **Nueva.** Es la primera vez en toda la aplicación que una operación de escritura modifica una fila cuyo `EmpleadoId` no es el del autor de la operación. `CrearAsync` solo escribe la fila del propio autor; `ResolverAsync` escribe la de otra persona |

**TB-7 es la frontera que más pesa en este ticket.** Toda la superficie de Elevation of Privilege
(R-22) vive en esta frontera: quién puede cruzarla y quién no lo decide un único punto
(`PermisosService`), y todo el diseño de este ticket gira en asegurar que sea el único.

---

## 3. Análisis STRIDE por componente (F-TM-01)

### C15 — `PermisosService` gana organigrama (Bloque 2)

| | Análisis |
|---|---|
| **S** | Sin superficie nueva. Quien consulta sigue saliendo de `IEmpleadoActualProvider`, nunca de un parámetro |
| **T** | `Empleado.ManagerId`/`DesignadoId` son datos administrados externamente (PRD-001, Fuera de Alcance) — este ticket los lee, no los escribe. Sin superficie de manipulación nueva |
| **R** | Las decisiones que `PermisosService` autoriza quedan en la base (`ResueltoPorId`, `FechaResolucion`), no solo en un log — más fuerte que R-18/R-20 |
| **I** | **Ver R-24.** Las consultas nuevas tocan `Empleado`, que además de `ManagerId`/`DesignadoId` carga `Nombre`/`Correo` (PII). Deben proyectar solo lo necesario (`Id`, `ManagerId`, `DesignadoId`), nunca `.Include()` completo — mismo criterio que el resto del dominio |
| **D** | Sin superficie nueva — mismo volumen de consultas que ya cubre NFR-03 |
| **E** | **Ver R-22 — el riesgo central de este ticket.** `PermisosService` sigue siendo la única sede (`AGENTS.md`); `PuedeResolverLasSolicitudesDe` excluye `self` explícitamente, así que ni siquiera un dato externo anómalo (`ManagerId == Id` de la propia persona) alcanzaría a autorizar autoaprobación |

### C16 — `SolicitudesService.ResolverAsync` (Bloque 3)

| | Análisis |
|---|---|
| **S** | Quien resuelve sale de `IEmpleadoActualProvider`, nunca de un parámetro — mismo patrón que `CrearAsync` (mitigación ya usada para R-13) |
| **T** | **Ver R-24** — `MotivoDeRechazo` es texto libre que después se muestra a otra persona (el empleado). Es la superficie de XSS más nueva de este ticket |
| **R** | Igual que C15: la resolución queda en la base con quién y cuándo |
| **I** | El motivo puede contener más detalle personal del estrictamente necesario — **ver R-24** |
| **D** | El reintento dirigido ante conflicto de serialización (mismo mecanismo que C14 de FEAT-001c) es único, no en bucle — **ver R-25** |
| **E** | Delega la autorización en `PermisosService.PuedeResolverLasSolicitudesDe`, nunca la reimplementa — verificado por el mismo escaneo que ya protege esto (`Ningun_otro_archivo_de_src_decide_negar_la_visibilidad`) |

### C17 — Listado de pendientes del equipo (Bloque 4)

| | Análisis |
|---|---|
| **S** | Sin superficie nueva |
| **T** | Sin superficie nueva — solo lectura |
| **R** | No aplica (no es una decisión, es una consulta) |
| **I** | Cruza TB-5 de verdad (ver arriba). Debe proyectar (`.Select`), nunca `.Include()`, y filtrar exclusivamente por los `EmpleadoId` que `PermisosService` autoriza — nunca "todos los pendientes" sin filtrar |
| **D** | Cubierto por NFR-03 (índices ya existentes de las FK de `Empleado`) |
| **E** | Mismo mecanismo de C15: si el filtro de autorización tuviera un error, el listado expondría solicitudes de un equipo ajeno — **ver R-22** |

### C18 — Pantalla de autorizaciones (Bloque 5)

| | Análisis |
|---|---|
| **S/T/R/D/E** | Mismo patrón ya auditado para `SaldoDelEmpleado.razor` (FEAT-001b, C11): sin `TimeProvider`, sin reloj propio, sin reimplementar ninguna regla del servidor. El campo de motivo se valida en el cliente por comodidad, pero el rechazo real (AC-03) lo decide el servidor |
| **I** | Renderiza `MotivoDeRechazo` (texto libre ajeno) — **nunca con `MarkupString`** (prohibición total de `AGENTS.md`, sin excepción para este campo) |

### C19 — Link condicional en `MainLayout.razor` (Bloque 5)

| | Análisis |
|---|---|
| **T** | La decisión "¿tengo un equipo a cargo?" sale de un método del dominio, nunca de una consulta propia del componente — mismo guardarraíl que ya vigila `ComponentesSinAccesoADatosTests` (sin `IDbContextFactory`/`DbContext` en ningún `.razor`) |
| Resto | Sin superficie — es un link, no un control de acceso: ocultarlo es UX, no seguridad. Alguien que edite la URL a mano sigue protegido por `PermisosService` en el servidor |

### C20 — Esquema: columnas y check constraint nuevas (Bloque 1)

| | Análisis |
|---|---|
| **T** | La check constraint (defensa en profundidad) impide que una fila quede con `Estado=Aprobada` y `ResueltoPorId` NULL, o `Estado=Pendiente` con datos de resolución — mismo patrón que las 4 existentes |
| **I** | `ResueltoPorId` es un id, no PII por sí solo; `MotivoDeRechazo` sí lo es — ver R-24/R-26 |
| Resto | Sin superficie nueva — ningún índice nuevo (los de `Empleado.ManagerId`/`DesignadoId` ya existían) |

---

## 4. Riesgos identificados

### 🟠 R-22 — HIGH · Elevación de privilegio: resolver solicitudes de un equipo ajeno

**Categoría STRIDE:** Elevation of Privilege · **Probabilidad:** Baja (requiere un defecto de
implementación, no una entrada maliciosa trivial) · **Impacto:** Alto — es exactamente la capacidad
que este ticket introduce por primera vez: escribir sobre la fila de otra persona (TB-7)

Si `PermisosService.PuedeResolverLasSolicitudesDe` tuviera un error (por ejemplo, una condición mal
invertida, o no excluir `self` correctamente), cualquier empleado autenticado podría aprobar o
rechazar solicitudes de personas que no están a su cargo — incluidas las propias.

**Mitigación:**
1. `PermisosService` sigue siendo la única sede de la decisión (`AGENTS.md`); `ResolverAsync` y el
   listado del equipo la consultan, nunca la reimplementan.
2. `PuedeResolverLasSolicitudesDe` excluye `self` explícitamente como condición propia, no como
   consecuencia accidental de la consulta — así que ni un dato externo anómalo (`ManagerId == Id`)
   alcanzaría a autorizar autoaprobación.
3. Tests dedicados que ejercitan la denegación cruzada (un empleado sin relación intenta resolver la
   solicitud de otro) contra la instancia real, más el escaneo existente
   `Ningun_otro_archivo_de_src_decide_negar_la_visibilidad` extendido para cubrir `ResolverAsync`.

### 🟡 R-24 — MEDIUM · El motivo de rechazo, como texto libre, es la superficie de XSS más nueva

**Categoría STRIDE:** Tampering / Information Disclosure · **Probabilidad:** Media (cualquier manager
puede escribir cualquier texto) · **Impacto:** Medio — se muestra a otra persona (el empleado), y
podría incluir HTML/script si se renderizara sin escapar, o más detalle personal del necesario

**Mitigación:**
1. Blazor Server escapa por defecto todo `@texto` interpolado en el marcado; el proyecto prohíbe
   `MarkupString` de forma absoluta (`AGENTS.md`), así que no hay ninguna vía habilitada para
   renderizar el motivo como HTML.
2. Test dedicado que confirma que la pantalla de "Mis Solicitudes" muestra el motivo como texto
   plano, con un centinela (`<script>`, comillas) que no se interpreta.
3. Sobre el contenido personal que el motivo pueda llevar: mismo control de acceso que el resto de la
   PII de este ticket — solo el empleado titular, su manager y el designado de su manager pueden
   verlo (FR-06/AC-08), igual criterio que R-05 de FEAT-001a.

### 🟢 R-25 — LOW · Dos resoluciones casi simultáneas de la misma solicitud

**Categoría STRIDE:** Denial of Service (de la operación, no del servicio) · **Probabilidad:** Baja ·
**Impacto:** Bajo, ya mitigado por diseño

Mitigación: transacción `Serializable` envolviendo lectura y escritura, con un reintento único y
dirigido ante conflicto de serialización (mismo mecanismo, mismo razonamiento, que R-16/R-21 de
FEAT-001c) — nunca en bucle. Es exactamente la mitigación que el propio PRD pide en su sección de
Riesgos.

### 🟡 R-26 — MEDIUM · El motivo de rechazo puede contener más detalle personal del necesario

**Categoría STRIDE:** Information Disclosure · **Probabilidad:** Media · **Impacto:** Medio

A diferencia de los datos estructurados del resto de la aplicación (fechas, estados, saldo), nada
impide que un manager escriba en el motivo información sensible no relacionada con la solicitud en
sí (por ejemplo, detalles de salud). **Aceptado sin mitigación técnica adicional**, con el mismo
control ya vigente para R-05 de FEAT-001a: el campo hereda exactamente las mismas restricciones de
acceso que el resto de los datos de la solicitud (FR-06/AC-08), y no hay ningún camino nuevo de
exposición que R-05 no cubriera ya. No se agrega redacción ni filtrado de contenido: está fuera del
alcance de este ticket (ver "Out of Scope" del PRD) y sería una función de moderación, no de control
de acceso.

**Revisar si:** en producción se observa que los motivos de rechazo llevan con frecuencia información
sensible no relacionada — en ese caso amerita un ticket de política de uso, no un cambio técnico.

---

## 5. Riesgos de FEAT-001a/b/c confirmados sin cambios

1. **R-05** — datos personales sensibles, control de acceso por rol. Se extiende, no se reabre.
2. **R-10** — la validación vive en el servidor; el campo de motivo en la UI es comodidad, no la
   regla.
3. **R-13** — ninguna firma pública acepta un `empleadoId`; se extiende a `ResolverAsync` (quien
   resuelve sale de `IEmpleadoActualProvider`).
4. **R-16** — los conflictos de motor no se convierten en rechazo sin verificación (mismo reintento
   dirigido que R-25 reutiliza).

---

## 6. Resumen

```
┌─────────────────────────────────────────────────────────┐
│  /daw-threat-modeling FEAT-002 — PASSED                  │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  Attack surfaces identified: 6 (C15-C20)                 │
│  Trust boundaries declared: 1 nueva (TB-7) + TB-5 se      │
│    cruza de verdad por primera vez                        │
│                                                          │
│  Risks:                                                  │
│    🟠 HIGH: R-22 — elevación de privilegio al resolver   │
│       solicitudes de un equipo ajeno                       │
│       — Mitigation: PermisosService como única sede,       │
│         self excluido explícitamente, tests de denegación  │
│         cruzada, foldeado en la spec                        │
│    🟡 MEDIUM: R-24 — motivo de rechazo como superficie de  │
│       XSS — Mitigation: sin MarkupString, test de texto     │
│       plano                                                  │
│    🟢 LOW: R-25 — concurrencia — Mitigation: reintento       │
│       dirigido, mismo patrón que R-16/R-21                    │
│    🟡 MEDIUM: R-26 — motivo con PII incidental — Aceptado,   │
│       mismo control que R-05                                  │
│                                                          │
│  Mitigations to fold into the spec:                      │
│    1. PuedeResolverLasSolicitudesDe sin self (R-22)         │
│    2. Proyección mínima en las consultas de organigrama     │
│       (R-22, I de C15/C17)                                    │
│    3. Test de texto plano para el motivo (R-24)               │
│    4. Reintento dirigido en ResolverAsync (R-25)               │
│                                                          │
│  ─────────────────────────────────────────────────────   │
│  Risks: C:0 H:1 M:2 L:1                                   │
│  Report: docs/daw/security/threat-FEAT-002.md              │
└─────────────────────────────────────────────────────────┘
```
