# Fix-plan FIX-001: El link de Autorizaciones nunca aparece

| Field | Value |
|-------|-------|
| Ticket | FIX-001 |
| Tier | FIX |
| RCA | docs/daw/specs/rca-FIX-001.md |
| Date | 2026-08-07 |
| Spec loops | 0 |

## Problem

El link "Autorizaciones" del menú (`MainLayout.razor`) nunca aparece, para ningún empleado, en
ningún circuito — sin importar si tiene equipo a cargo. Al abrir la aplicación queda además un
`LogError` espurio en el log (`SinEmpleadoSeleccionadoException`), que el usuario reportó como
síntoma visible.

## Root cause

`MainLayout.OnInitializedAsync()` evalúa `SolicitudesService.TieneEquipoACargoAsync()` una única
vez, al arrancar el circuito — antes de que `SelectorDeEmpleado` (dentro de `@Body`) le dé al
usuario oportunidad de elegir un empleado. En ese instante `IdentidadDelEmpleado` vale
`SinSeleccionar`, así que la llamada lanza `SinEmpleadoSeleccionadoException`; `MainLayout` la
atrapa con un `catch (Exception)` genérico, la loguea como error y fija
`_tieneEquipoACargo = false` para siempre — nada vuelve a preguntar cuando la identidad cambia
después, porque `SelectorDeEmpleado.OnEmpleadoCambiado` solo está cableado hacia
`MisSolicitudes.RecargarAsync`, no hacia el layout. Detalle completo en
`docs/daw/specs/rca-FIX-001.md`.

## Solución — pasos

1. `src/GestionVacaciones.Data/Services/IEmpleadoActualProvider.cs` — agregar a la interfaz
   `IEmpleadoActualProvider` el miembro:
   ```csharp
   /// <summary>
   /// Se dispara cuando <see cref="Identidad"/> cambia de verdad (un <see cref="SeleccionarAsync"/>
   /// exitoso). Sin datos en el payload a propósito (<c>EventHandler</c>, no
   /// <c>EventHandler&lt;int&gt;</c>): quien se suscribe vuelve a preguntar <see cref="Identidad"/>,
   /// la única sede de la verdad (NFR-06), en vez de confiar en lo que el evento le pase.
   /// Existe para que un componente que NO es ancestro de quien elige la identidad —como
   /// <c>MainLayout</c>, que envuelve <c>@Body</c> en vez de vivir dentro de él— pueda reaccionar sin
   /// convertirse en uno.
   /// </summary>
   event EventHandler? IdentidadCambiada;
   ```

2. `src/GestionVacaciones.Web/Identidad/EmpleadoActualDesarrollo.cs` — declarar el evento
   (`public event EventHandler? IdentidadCambiada;`) y dispararlo **solo** dentro de la rama que ya
   asigna `_identidad`, en `SeleccionarAsync`, inmediatamente después de la asignación y antes del
   `return`:
   ```csharp
   _identidad = IdentidadDelEmpleado.De(empleadoId);
   IdentidadCambiada?.Invoke(this, EventArgs.Empty);

   return ResultadoDeSeleccion.Seleccionado;
   ```
   Nunca se dispara en la rama `RechazadaPorEmpleadoInexistente`: ahí `_identidad` no cambió, y
   revaluar contra la misma identidad no tiene nada que aportar.

3. `src/GestionVacaciones.Web/Identidad/EmpleadoActualNoConfigurado.cs` — declarar el evento
   (`public event EventHandler? IdentidadCambiada;`), nunca dispararlo: `SeleccionarAsync` en esta
   clase siempre lanza antes de llegar a un punto donde podría hacerlo. Un comentario breve lo deja
   explícito, mismo criterio que el resto de la clase (contradice la regla general a propósito).

4. `tests/GestionVacaciones.Tests/Dominio/IdentidadDePrueba.cs` — declarar el evento
   (`public event EventHandler? IdentidadCambiada;`), requerido para seguir compilando como
   implementación directa de la interfaz. Nunca se dispara: esta clase ya declara que "los tests del
   dominio no cambian la identidad del circuito", y el evento no cambia esa regla.

5. `src/GestionVacaciones.Web/Components/Layout/MainLayout.razor` — reescribir el bloque `@code`:
   - `@inject IEmpleadoActualProvider EmpleadoActual` (nuevo, arriba, junto a los otros `@inject`).
   - `@implements IDisposable` en la etiqueta del componente.
   - Extraer la evaluación a un método privado compartido, para que la ruta inicial y la reactiva
     ejecuten exactamente el mismo código (y por lo tanto la misma cobertura de sus dos `catch`):
     ```csharp
     protected override void OnInitialized() =>
         EmpleadoActual.IdentidadCambiada += AlCambiarLaIdentidad;

     protected override async Task OnInitializedAsync()
     {
         await EvaluarSiTieneEquipoACargoAsync();
         _estado = EstadoListo;
     }

     /// <summary>
     /// Reevalúa la pregunta de negocio y actualiza <see cref="_tieneEquipoACargo"/>. Separa el
     /// estado esperado —todavía no hay identidad— del fallo real: el primero no es un error (mismo
     /// criterio que <c>MisSolicitudes.RecargarAsync</c> aplica a su propio catch de
     /// <see cref="SinEmpleadoSeleccionadoException"/>) y no ensucia el log; el segundo sigue
     /// registrado, sin catch silencioso.
     /// </summary>
     private async Task EvaluarSiTieneEquipoACargoAsync()
     {
         try
         {
             _tieneEquipoACargo = await Solicitudes.TieneEquipoACargoAsync();
         }
         catch (SinEmpleadoSeleccionadoException)
         {
             _tieneEquipoACargo = false;
         }
         catch (Exception excepcion)
         {
             Registro.LogError(excepcion, "No se pudo determinar si el empleado actual tiene equipo a cargo.");
             _tieneEquipoACargo = false;
         }
     }

     /// <summary>
     /// Handler del evento de <see cref="IEmpleadoActualProvider.IdentidadCambiada"/>. Reevalúa y
     /// vuelve a renderizar; la excepción, si la hay, queda contenida DENTRO de la continuación que
     /// recibe <c>InvokeAsync</c> — mitigación del riesgo MEDIUM del modelo de amenazas: sin este
     /// contenido, un fallo transitorio de <c>TieneEquipoACargoAsync</c> podría tumbar el circuito
     /// completo en lugar de solo ocultar el link.
     /// </summary>
     private void AlCambiarLaIdentidad(object? remitente, EventArgs argumentos) =>
         _ = InvokeAsync(async () =>
         {
             await EvaluarSiTieneEquipoACargoAsync();
             StateHasChanged();
         });

     /// <summary>Desuscribe del proveedor de identidad. Higiene de diseño: primer <c>event</c> del repo.</summary>
     public void Dispose() => EmpleadoActual.IdentidadCambiada -= AlCambiarLaIdentidad;
     ```
   - El resto del componente (`_estado`, `_tieneEquipoACargo`, las constantes, el marcado) no cambia.

6. `tests/GestionVacaciones.Tests/Componentes/AutorizacionesTests.cs`:
   - Nuevo helper privado junto a `RegistrarComoQuienResuelve`:
     ```csharp
     private void RegistrarConSelectorInteractivo()
     {
         Services.AddSingleton<TimeProvider>(_tiempo);
         Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaDeLaBaseDeTest(_baseDeDatos));
         Services.AddSingleton<EmpleadosService>();
         Services.AddSingleton<IEmpleadoActualProvider, EmpleadoActualDesarrollo>();
         Services.AddSingleton<PermisosService>();
         Services.AddSingleton<SaldoService>();
         Services.AddSingleton<SolicitudesService>();
     }
     ```
     Usa el proveedor de identidad **real** (`EmpleadoActualDesarrollo`), no `IdentidadDePrueba`: es
     el único que reproduce el orden real del bug — arranca `SinSeleccionar` y cambia por
     `SeleccionarAsync`, exactamente lo que `SelectorDeEmpleado` hace en producción.
   - Nuevo test, reproduce el bug end-to-end:
     ```csharp
     [Fact]
     public async Task El_link_de_autorizaciones_aparece_al_elegir_un_manager_sin_recrear_el_layout()
     {
         _baseDeDatos.SaltearSiNoEstaDisponible();

         await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
         await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
         await AsignarManagerAsync(subordinado.Id, manager.Id);

         RegistrarConSelectorInteractivo();

         var layout = Render<MainLayout>(parametros => parametros.Add(pantalla => pantalla.Body, ContenidoDePrueba));
         layout.WaitForState(() => EstadoDelMenu(layout) == MainLayout.EstadoListo, TimeSpan.FromSeconds(10));

         // Antes de elegir: el link no aparece, y no por el catch genérico -- es el estado
         // "sin identidad todavía", que EvaluarSiTieneEquipoACargoAsync trata sin loguear error
         // (cubre la rama SinEmpleadoSeleccionadoException, sin ejercer antes de este fix).
         Assert.Empty(layout.FindAll($"[data-testid='{MainLayout.IdDelLinkDeAutorizaciones}']"));

         var identidad = Services.GetRequiredService<IEmpleadoActualProvider>();
         var resultado = await identidad.SeleccionarAsync(manager.Id, Cancelacion);
         Assert.Equal(ResultadoDeSeleccion.Seleccionado, resultado);

         // Reactivo: nadie volvió a renderizar <MainLayout> desde cero -- el mismo layout se entera.
         layout.WaitForState(
             () => layout.FindAll($"[data-testid='{MainLayout.IdDelLinkDeAutorizaciones}']").Count == 1,
             TimeSpan.FromSeconds(10));
     }
     ```
   - `IdentidadDePrueba` sigue usándose sin cambios en los tests existentes de este archivo
     (`RegistrarComoQuienResuelve`): no se tocan.

## Dependencias entre pasos

1 → (2, 3, 4) → 5 → 6. El paso 1 (la interfaz) es prerrequisito de todos los demás — nada compila
sin él. Los pasos 2, 3 y 4 son independientes entre sí. El paso 5 depende de que 1 y 2 existan (usa
el evento de la interfaz y confía en que `EmpleadoActualDesarrollo` lo dispare). El paso 6 depende
de 5 (verifica su comportamiento) y de 4 (para que `IdentidadDePrueba`, usado en el resto del
archivo, siga compilando).

## Error handling

- `SinEmpleadoSeleccionadoException` (circuito sin identidad todavía): estado esperado, sin log,
  `_tieneEquipoACargo = false`. Mismo criterio que `MisSolicitudes.RecargarAsync` ya aplica.
- Cualquier otra `Exception` (falla real: base caída, etc.): se loguea con `Registro.LogError`
  (sin PII, mismo mensaje que hoy) y `_tieneEquipoACargo = false`. No es un catch silencioso.
- La excepción del handler reactivo (`AlCambiarLaIdentidad`) queda contenida **dentro** de la
  continuación de `InvokeAsync` — mitigación del riesgo MEDIUM (DoS) del modelo de amenazas
  (`docs/daw/security/threat-FIX-001.md`): como ambos call sites llaman al mismo método
  `EvaluarSiTieneEquipoACargoAsync`, la cobertura de sus dos `catch` que ya ejercita el test
  existente `Si_TieneEquipoACargoAsync_falla_el_link_no_aparece_y_el_resto_del_layout_sigue_en_pie`
  se extiende por construcción a la ruta reactiva — no hace falta un segundo test de fallo apuntado
  específicamente a esa ruta.
- El evento nunca se dispara en `RechazadaPorEmpleadoInexistente`: `_identidad` no cambió, así que
  no hay nada nuevo que reevaluar.

## Tests

- [ ] **Regression test** — `El_link_de_autorizaciones_aparece_al_elegir_un_manager_sin_recrear_el_layout`
  (paso 6): falla ANTES del fix (el link nunca aparece tras `SeleccionarAsync`), pasa DESPUÉS.
  Cubre además, en su primera aserción, la rama `SinEmpleadoSeleccionadoException` de
  `EvaluarSiTieneEquipoACargoAsync` — sin ejercer antes de este fix, porque ningún test existente
  arrancaba el layout sin identidad ya resuelta.
- [ ] Los 3 tests existentes de `AutorizacionesTests.cs` que renderizan `<MainLayout>`
  (`El_link_de_autorizaciones_no_aparece_para_quien_no_tiene_equipo`,
  `El_link_de_autorizaciones_aparece_para_un_manager`,
  `Si_TieneEquipoACargoAsync_falla_el_link_no_aparece_y_el_resto_del_layout_sigue_en_pie`) siguen en
  verde sin modificarse — su registro sigue usando `IdentidadDePrueba`, que ahora también implementa
  el evento (sin dispararlo), y el comportamiento que verifican no cambió.
- [ ] La suite completa (`dotnet test`) sigue en 338+ tests en verde, 0 advertencias
  (`TreatWarningsAsErrors`).

## Regression risk

**Low.** El cambio es aditivo en la interfaz (un evento nuevo, ningún miembro existente cambia de
firma) y el único componente productivo con comportamiento nuevo es `MainLayout`, cuya lógica de
evaluación es la misma que antes, solo compartida entre dos call sites. El mayor riesgo identificado
(DoS por excepción no contenida en el handler reactivo) está mitigado por diseño: el `try/catch`
vive dentro de `EvaluarSiTieneEquipoACargoAsync`, que ambos call sites comparten, no en el borde de
`InvokeAsync`.

## Rollback plan

- **Pasos:** revertir el commit de este fix. Es trivial: no hay migración de datos, no hay cambios
  de esquema, no hay estado persistido — el único efecto es en memoria, por circuito. Revertir el
  código vuelve exactamente al comportamiento (y al bug) anterior.
- **Indicadores:** cualquier regresión en `AutorizacionesTests.cs` o en la suite completa tras el
  merge; o un reporte de que el link aparece cuando no corresponde (lo que indicaría que la
  reevaluación no está usando `PermisosService` como única fuente — ver "Elevación de privilegio" en
  el modelo de amenazas, que descarta esto por diseño pero es lo primero a revisar si ocurriera).
