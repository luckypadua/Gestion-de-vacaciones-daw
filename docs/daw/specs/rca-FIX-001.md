# RCA: El link de Autorizaciones nunca aparece

| Campo | Valor |
|---|---|
| Ticket | FIX-001 |
| Fecha | 2026-08-07 |
| Reportado por | usuario, al ejecutar la aplicación y elegir un empleado |
| PRD relacionado | `prd-FEAT-002.md` — sin gap |

## Síntoma reportado

Al elegir un empleado en el selector de desarrollo, el log muestra:

```
fail: GestionVacaciones.Web.Components.Layout.MainLayout[0]
      No se pudo determinar si el empleado actual tiene equipo a cargo.
      GestionVacaciones.Data.Services.SinEmpleadoSeleccionadoException: Todavía no hay ningún
      empleado seleccionado en este circuito. ...
         at GestionVacaciones.Data.Services.IdentidadDelEmpleado.get_Id()
         at GestionVacaciones.Data.Services.PermisosService.EmpleadosBajoAutoridadDeAsync(...)
         at GestionVacaciones.Data.Services.SolicitudesService.TieneEquipoACargoAsync(...)
         at GestionVacaciones.Web.Components.Layout.MainLayout.OnInitializedAsync()
```

## Impacto real (más allá del log)

El log no es solo ruido: es el síntoma de que **el link "Autorizaciones" no aparece nunca, para
nadie, en ningún circuito** — sin importar qué empleado se elija ni si tiene equipo a cargo. Bloque
5 de FEAT-002 queda inalcanzable por la navegación que el propio spec diseñó para llegar a él.

## Cadena de eventos (root cause)

1. `MainLayout` envuelve `@Body`. `SelectorDeEmpleado` — el único lugar donde el circuito elige un
   empleado — vive **dentro** de `@Body` (en `MisSolicitudes.razor`), nunca en el layout.
2. `MainLayout.OnInitializedAsync()` (`MainLayout.razor:54-71`) corre **una sola vez**, al arrancar
   el circuito, y llama a `Solicitudes.TieneEquipoACargoAsync()` de inmediato — antes de que el
   usuario haya tenido ninguna oportunidad de interactuar con el selector.
3. En ese instante, `EmpleadoActualDesarrollo._identidad` (scoped, uno por circuito) todavía vale
   `IdentidadDelEmpleado.SinSeleccionar` (`EmpleadoActualDesarrollo.cs:35`) — es el estado inicial
   explícito, correcto por diseño.
4. `SolicitudesService.TieneEquipoACargoAsync` (`SolicitudesService.cs:979-985`) llama a
   `PermisosService.EmpleadosBajoAutoridadDeAsync`, que necesita `quienConsulta.Id`
   (`PermisosService.cs:305`). `IdentidadDelEmpleado.Id` lanza `SinEmpleadoSeleccionadoException`
   cuando no hay nadie elegido (`IEmpleadoActualProvider.cs:39`) — también por diseño: el tipo
   existe justamente para no devolver un `0` o un `null` silencioso.
5. `MainLayout` atrapa la excepción con un `catch (Exception)` genérico (`MainLayout.razor:60-66`),
   la loguea con `LogError` y fija `_tieneEquipoACargo = false`.
6. **Nada vuelve a preguntar.** `SelectorDeEmpleado.OnEmpleadoCambiado`
   (`SelectorDeEmpleado.razor:62-63`) sólo está cableado hacia `MisSolicitudes.RecargarAsync`
   (`MisSolicitudes.razor:21`) — el layout no se entera de que la identidad cambió, y
   `OnInitializedAsync` de un componente no vuelve a ejecutarse por un cambio de estado ajeno. El
   `_tieneEquipoACargo = false` del paso 5 queda fijo el resto del circuito.

El resultado no es un fallo intermitente ni dependiente de datos: ocurre **siempre**, en el orden en
que el circuito real se usa.

## Por qué los tests existentes no lo detectaron

`AutorizacionesTests` (`El_link_de_autorizaciones_aparece_para_un_manager` y el resto de esa
familia) llaman a `RegistrarComoQuienResuelve(manager.Id)` **antes** de `Render<MainLayout>`. Ese
orden — identidad ya resuelta cuando el layout arranca — es exactamente el que el flujo real
(`EmpleadoActualDesarrollo` + `SelectorDeEmpleado`) nunca produce: ahí el circuito siempre arranca en
`SinSeleccionar`. Los tests verifican una precondición que la aplicación real no cumple.

## Componente afectado

- `src/GestionVacaciones.Web/Components/Layout/MainLayout.razor` — evalúa la visibilidad del link
  una sola vez, en el momento equivocado, y no tiene forma de enterarse de que la identidad cambió
  después.

No están en falta: `IdentidadDelEmpleado`, `SinEmpleadoSeleccionadoException`,
`EmpleadoActualDesarrollo`, `PermisosService.EmpleadosBajoAutoridadDeAsync` y
`SolicitudesService.TieneEquipoACargoAsync` — todos se comportan tal como fueron diseñados
(`AGENTS.md`: «sin identidad» no es un error, y estos tipos lo tratan como tal). El defecto es que
`MainLayout` los consulta en el momento equivocado y no vuelve a consultarlos.

## Gap en el PRD

Ninguno. `prd-FEAT-002.md` (AC-07) exige qué debe mostrar la pantalla `/autorizaciones` una vez que
se llega a ella; no exige la existencia de un link ni cuándo debe reevaluarse. La forma de llegar —
el link condicional en `MainLayout` — es una decisión de `spec-FEAT-002.md` (Bloque 5), no del PRD.
El defecto es de implementación, no de requisito.

## Dirección de la corrección (no vinculante — se define en PLAN)

`MainLayout` necesita enterarse de cuándo cambia la identidad del circuito, no solo preguntar una
vez al arrancar. El diseño concreto (notificación desde el proveedor de identidad, cascada, u otro
mecanismo) se decide en PLAN.
