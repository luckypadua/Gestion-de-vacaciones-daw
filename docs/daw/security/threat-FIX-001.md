# Threat Model: FIX-001 — El link de Autorizaciones nunca aparece

| Campo | Valor |
|---|---|
| Ticket | FIX-001 |
| Fecha | 2026-08-07 |
| Diseño analizado | Evento `IdentidadCambiada` en `IEmpleadoActualProvider` + reevaluación reactiva en `MainLayout` |

## Componentes nuevos o modificados

1. `IEmpleadoActualProvider.IdentidadCambiada` (nuevo miembro de interfaz, `event EventHandler?`,
   sin payload).
2. `EmpleadoActualDesarrollo.SeleccionarAsync` — dispara el evento tras una selección exitosa.
3. `EmpleadoActualNoConfigurado` — declara el evento, nunca lo dispara (nunca cambia de identidad).
4. `IdentidadDePrueba` (test-only) — declara el evento, nunca lo dispara.
5. `MainLayout.razor` — se suscribe al evento, reevalúa `TieneEquipoACargoAsync()` reactivamente,
   implementa `IDisposable` para desuscribirse.

## Límites de confianza

No se agrega ningún límite de confianza nuevo. TB-1 (navegador → servidor) ya existe y gobierna la
entrada de `SeleccionarAsync(empleadoId)`; ese código y su validación contra la nómina **no
cambian**. El evento nuevo es enteramente server-side, en proceso, y se dispara **después** de que
la validación de TB-1 ya pasó — no cruza ningún límite nuevo.

## Análisis STRIDE

### `IEmpleadoActualProvider.IdentidadCambiada` (evento)

| Categoría | Evaluación |
|---|---|
| Spoofing | N/A — evento .NET en proceso, sin serialización ni superficie de red. |
| Tampering | N/A — mismo razonamiento. |
| Repudiation | Sin cambio — no altera qué ni cómo se audita la selección (`SeleccionarAsync` ya es el único punto de cambio, sin cambios en esa auditoría). |
| Information Disclosure | LOW — el evento es `EventHandler` sin payload **a propósito**: ningún suscriptor puede recibir el `Id` del evento y tiene que volver a consultar `Identidad`, la única sede de la verdad (NFR-06). Decisión de diseño, no un hallazgo a mitigar. |
| Denial of Service | ver "Riesgos" abajo — MEDIUM, con mitigación. |
| Elevation of Privilege | Ninguno — ver "Elevación de privilegio" abajo. |

### `EmpleadoActualDesarrollo.SeleccionarAsync` (disparo del evento)

| Categoría | Evaluación |
|---|---|
| Elevation of Privilege | El evento se dispara **solo** dentro de la rama que ya pasó `ExisteEnLaNominaAsync` y asignó `_identidad`, nunca en `RechazadaPorEmpleadoInexistente`. No abre ninguna vía nueva para seleccionar una identidad no válida — la validación contra la nómina es la misma de siempre. |

### `MainLayout` (suscripción y reevaluación reactiva)

| Categoría | Evaluación |
|---|---|
| Denial of Service | MEDIUM — ver mitigación abajo. |
| Information Disclosure | Sin cambio — el `catch` separado para `SinEmpleadoSeleccionadoException` (sin log) vs. `Exception` genérica (con `LogError`, sin PII, mismo patrón que el resto del dominio) **mejora** la señal del log existente; no agrega ninguna superficie nueva. |

## Riesgos

- 🟡 **MEDIUM — Denial of Service (circuito completo) por excepción no atrapada en el handler
  reactivo.** Si `TieneEquipoACargoAsync()` lanza dentro del nuevo handler del evento y esa
  excepción no queda contenida, en Blazor Server una excepción no manejada en un componente puede
  tumbar el circuito completo — de una regresión que hoy solo oculta un link a una que cierra la
  sesión del usuario.
  **Mitigación (obligatoria en la spec):** el `try/catch` del handler reactivo replica exactamente
  el de la evaluación inicial (`SinEmpleadoSeleccionadoException` → estado normal sin log,
  `Exception` genérica → `LogError` y `_tieneEquipoACargo = false`), colocado **dentro** de la
  continuación async que recibe `InvokeAsync`, de forma que ninguna excepción escapa sin observar.
  Un test dedicado fuerza el fallo durante la ruta **reactiva** (no solo la inicial) — mismo patrón
  que cerró F-VER-03/NFR-01 en VERIFY de FEAT-002 sobre este mismo archivo.

- 🟢 **LOW — Carrera entre el disparo del evento y `Dispose()` al cerrar el circuito.** Aceptado.
  Blazor Server serializa el trabajo de un circuito en su propio `SynchronizationContext`; tanto el
  disparo (originado en una interacción de UI que llama a `SeleccionarAsync`) como `Dispose`
  (disparado por el framework al cerrar el circuito) corren en ese mismo contexto, así que no hay
  concurrencia real entre ambos. La desuscripción en `IDisposable` se agrega de todos modos, como
  higiene de diseño — es la primera vez que el repo introduce un `event`, sin precedente previo — no
  porque se haya encontrado una carrera real.
  **Revisar si:** el proyecto adopta prerenderizado (`InteractiveServer` con `prerender: true` en un
  modo que abra un scope de DI distinto al del circuito interactivo); hoy no lo hace.

## Elevación de privilegio — por qué no aplica

La reevaluación reactiva vuelve a preguntar `PermisosService.EmpleadosBajoAutoridadDeAsync` — la
**única** sede de esa decisión — cada vez que se dispara. No puede producir un `true` que la propia
pantalla `/autorizaciones` no volvería a confirmar de forma independiente (esa pantalla hace su
propia consulta contra el mismo servicio). Un estado de UI incorrecto o desactualizado es, en el
peor caso, un link muerto o un link visible que lleva a una pantalla vacía — nunca un bypass del
control de acceso, que vive enteramente del lado del servidor y no cambia con este fix.

## Datos sensibles (F-TM-05)

Ninguno nuevo. El evento no lleva payload; el único dato que atraviesa el flujo (`Id` del empleado)
ya está gobernado por `IdentidadDelEmpleado` y su `ToString()` sin PII (R-12), sin cambios.

## Cifrado (F-TM-07)

No aplica — ningún dato PII ni credencial nuevos, evento en proceso sin persistencia ni transporte
de red.

---

┌─────────────────────────────────────────────────────────┐
│  /daw-threat-modeling — PASSED                            │
├─────────────────────────────────────────────────────────┤
│                                                           │
│  Attack surfaces identified: 3 (evento de interfaz,       │
│    disparo en EmpleadoActualDesarrollo, handler reactivo   │
│    en MainLayout)                                          │
│  Trust boundaries declared: 0 nuevos (TB-1 ya existente,   │
│    sin cambios)                                             │
│                                                           │
│  Risks:                                                    │
│    🟡 MEDIUM: DoS por excepción no atrapada en el handler   │
│      reactivo — Mitigation: try/catch replicado dentro de   │
│      InvokeAsync + test dedicado sobre la ruta reactiva      │
│    🟢 LOW: carrera evento/Dispose — aceptado, sin acción     │
│      adicional (SynchronizationContext serializa ambos)      │
│                                                           │
│  Mitigations to fold into the spec:                         │
│    1. Try/catch del handler reactivo idéntico al de la       │
│       evaluación inicial, dentro de la continuación async.    │
│    2. Test que fuerza el fallo de TieneEquipoACargoAsync       │
│       durante la reevaluación reactiva (no solo la inicial).   │
│                                                           │
│  ─────────────────────────────────────────────────────      │
│  Risks: C:0 H:0 M:1 L:1                                     │
│  Report: docs/daw/security/threat-FIX-001.md                │
└─────────────────────────────────────────────────────────┘
