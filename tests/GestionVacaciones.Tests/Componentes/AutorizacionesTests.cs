using Bunit;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Tests.Persistencia;
using GestionVacaciones.Web.Components.Layout;
using GestionVacaciones.Web.Components.Pages;
using GestionVacaciones.Web.Identidad;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// FEAT-002 (Bloque 5): la pantalla de autorizaciones —<c>Autorizaciones.razor</c>— y la visibilidad
/// condicional del link que la alcanza desde <c>MainLayout.razor</c> (AC-07, y AC-01/AC-02 desde acá).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué necesita la instancia SQL Server 2022 del entorno.</b> Igual que el resto de
/// <c>Componentes/</c> con datos reales: <c>SolicitudesService</c> y <c>PermisosService</c> son
/// sellados, no hay proveedor en memoria en la spec, y el organigrama (manager/designado) es un dato
/// que solo la base puede dar. El único caso que no la necesita —el fallo del listado— usa
/// <c>FabricaQueNadieDebeUsar</c>, mismo patrón que <c>MisSolicitudesTests</c>.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class AutorizacionesTests : ContextoDeComponentes
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 7, 9, 15, 0, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;
    private readonly TiempoFijo _tiempo = new(_instanteFijo);

    public AutorizacionesTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_manager_ve_el_listado_de_pendientes_de_su_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var inicio = new DateOnly(2026, 9, 1);
        var fin = new DateOnly(2026, 9, 3);
        await SembrarPendienteAsync(subordinado.Id, inicio, fin);

        RegistrarComoQuienResuelve(manager.Id);

        var pagina = Render<Autorizaciones>();

        pagina.WaitForState(
            () => EstadoDeLaPantalla(pagina) != Autorizaciones.EstadoCargando, TimeSpan.FromSeconds(10));

        Assert.Equal(Autorizaciones.EstadoListado, EstadoDeLaPantalla(pagina));

        var filas = pagina.FindAll($"[data-testid='{Autorizaciones.IdDeLaFila}']");
        Assert.Single(filas);

        var fila = filas[0];
        Assert.False(string.IsNullOrWhiteSpace(
            fila.QuerySelector($"[data-testid='{Autorizaciones.IdDelEmpleado}']")!.TextContent));
        Assert.Equal(
            "3",
            fila.QuerySelector($"[data-testid='{Autorizaciones.IdDeLosDiasCorridos}']")!.TextContent.Trim());
    }

    [Fact]
    public async Task Aprobar_una_solicitud_la_saca_del_listado()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);
        await SembrarPendienteAsync(subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        RegistrarComoQuienResuelve(manager.Id);

        var pagina = Render<Autorizaciones>();
        pagina.WaitForState(
            () => EstadoDeLaPantalla(pagina) == Autorizaciones.EstadoListado, TimeSpan.FromSeconds(10));

        await pagina.Find($"[data-testid='{Autorizaciones.IdDelBotonDeAprobar}']").ClickAsync(new MouseEventArgs());

        Assert.Equal(Autorizaciones.EstadoVacio, EstadoDeLaPantalla(pagina));
        Assert.Empty(pagina.FindAll($"[data-testid='{Autorizaciones.IdDeLaFila}']"));

        await using var contexto = _baseDeDatos.CrearContexto();
        var solicitud = await contexto.Solicitudes
            .SingleAsync(candidata => candidata.EmpleadoId == subordinado.Id, Cancelacion);
        Assert.Equal(EstadoSolicitud.Aprobada, solicitud.Estado);
        Assert.Equal(manager.Id, solicitud.ResueltoPorId);
    }

    [Fact]
    public async Task Rechazar_sin_motivo_no_permite_confirmar()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);
        await SembrarPendienteAsync(subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        RegistrarComoQuienResuelve(manager.Id);

        var pagina = Render<Autorizaciones>();
        pagina.WaitForState(
            () => EstadoDeLaPantalla(pagina) == Autorizaciones.EstadoListado, TimeSpan.FromSeconds(10));

        await pagina.Find($"[data-testid='{Autorizaciones.IdDelBotonDeRechazar}']").ClickAsync(new MouseEventArgs());

        var confirmar = pagina.Find($"[data-testid='{Autorizaciones.IdDelBotonDeConfirmarRechazo}']");
        Assert.True(confirmar.HasAttribute("disabled"));

        // Sin confirmar: no se llamó al servidor, la solicitud sigue Pendiente y en el listado.
        Assert.Single(pagina.FindAll($"[data-testid='{Autorizaciones.IdDeLaFila}']"));
    }

    [Fact]
    public async Task Rechazar_con_motivo_la_saca_del_listado_con_el_motivo_guardado()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);
        await SembrarPendienteAsync(subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        RegistrarComoQuienResuelve(manager.Id);

        var pagina = Render<Autorizaciones>();
        pagina.WaitForState(
            () => EstadoDeLaPantalla(pagina) == Autorizaciones.EstadoListado, TimeSpan.FromSeconds(10));

        await pagina.Find($"[data-testid='{Autorizaciones.IdDelBotonDeRechazar}']").ClickAsync(new MouseEventArgs());

        const string textoDelMotivo = "No hay cobertura ese día";
        var campoDeMotivo = pagina.FindComponent<MudTextField<string>>();
        await pagina.InvokeAsync(() => campoDeMotivo.Instance.ValueChanged.InvokeAsync(textoDelMotivo));

        var confirmar = pagina.Find($"[data-testid='{Autorizaciones.IdDelBotonDeConfirmarRechazo}']");
        Assert.False(confirmar.HasAttribute("disabled"));

        await confirmar.ClickAsync(new MouseEventArgs());

        Assert.Equal(Autorizaciones.EstadoVacio, EstadoDeLaPantalla(pagina));
        Assert.Empty(pagina.FindAll($"[data-testid='{Autorizaciones.IdDeLaFila}']"));

        await using var contexto = _baseDeDatos.CrearContexto();
        var solicitud = await contexto.Solicitudes
            .SingleAsync(candidata => candidata.EmpleadoId == subordinado.Id, Cancelacion);
        Assert.Equal(EstadoSolicitud.Rechazada, solicitud.Estado);
        Assert.Equal(textoDelMotivo, solicitud.MotivoDeRechazo);
    }

    [Fact]
    public async Task Sin_solicitudes_pendientes_se_ve_un_estado_vacio_no_un_error()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        RegistrarComoQuienResuelve(manager.Id);

        var pagina = Render<Autorizaciones>();
        pagina.WaitForState(
            () => EstadoDeLaPantalla(pagina) != Autorizaciones.EstadoCargando, TimeSpan.FromSeconds(10));

        Assert.Equal(Autorizaciones.EstadoVacio, EstadoDeLaPantalla(pagina));
        Assert.Single(pagina.FindAll($"[data-testid='{Autorizaciones.IdDelEstadoVacio}']"));
        Assert.Empty(pagina.FindAll($"[data-testid='{Autorizaciones.IdDelError}']"));
    }

    [Fact]
    public async Task El_link_de_autorizaciones_no_aparece_para_quien_no_tiene_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var solo = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        RegistrarComoQuienResuelve(solo.Id);

        var layout = Render<MainLayout>(parametros => parametros.Add(pantalla => pantalla.Body, ContenidoDePrueba));

        layout.WaitForState(() => EstadoDelMenu(layout) == MainLayout.EstadoListo, TimeSpan.FromSeconds(10));

        Assert.Empty(layout.FindAll($"[data-testid='{MainLayout.IdDelLinkDeAutorizaciones}']"));
    }

    [Fact]
    public async Task El_link_de_autorizaciones_aparece_para_un_manager()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        RegistrarComoQuienResuelve(manager.Id);

        var layout = Render<MainLayout>(parametros => parametros.Add(pantalla => pantalla.Body, ContenidoDePrueba));

        layout.WaitForState(() => EstadoDelMenu(layout) == MainLayout.EstadoListo, TimeSpan.FromSeconds(10));

        Assert.Single(layout.FindAll($"[data-testid='{MainLayout.IdDelLinkDeAutorizaciones}']"));
    }

    [Fact]
    public async Task El_link_de_autorizaciones_aparece_al_elegir_un_manager_sin_recrear_el_layout()
    {
        // FIX-001: reproduce el bug tal como ocurre en producción -- el circuito arranca sin
        // identidad (SelectorDeEmpleado.razor, dentro de @Body, todavía no ofreció nada) y el usuario
        // elige DESPUÉS de que <MainLayout> ya se inicializó. Por eso usa el proveedor real
        // (EmpleadoActualDesarrollo), no IdentidadDePrueba: es el único que arranca SinSeleccionar y
        // cambia con SeleccionarAsync, igual que en producción.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        RegistrarConSelectorInteractivo();

        var layout = Render<MainLayout>(parametros => parametros.Add(pantalla => pantalla.Body, ContenidoDePrueba));
        layout.WaitForState(() => EstadoDelMenu(layout) == MainLayout.EstadoListo, TimeSpan.FromSeconds(10));

        // Antes de elegir: el link no aparece -- y no por el catch genérico, sino por el estado
        // esperado "sin identidad todavía" (SinEmpleadoSeleccionadoException), que
        // EvaluarSiTieneEquipoACargoAsync trata sin loguear error.
        Assert.Empty(layout.FindAll($"[data-testid='{MainLayout.IdDelLinkDeAutorizaciones}']"));

        var identidad = Services.GetRequiredService<IEmpleadoActualProvider>();
        var resultado = await identidad.SeleccionarAsync(manager.Id, Cancelacion);
        Assert.Equal(ResultadoDeSeleccion.Seleccionado, resultado);

        // Reactivo: nadie volvió a renderizar <MainLayout> desde cero -- el mismo layout se entera
        // solo, vía IEmpleadoActualProvider.IdentidadCambiada.
        layout.WaitForState(
            () => layout.FindAll($"[data-testid='{MainLayout.IdDelLinkDeAutorizaciones}']").Count == 1,
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Si_TieneEquipoACargoAsync_falla_el_link_no_aparece_y_el_resto_del_layout_sigue_en_pie()
    {
        // Hallazgo 1 de VERIFY (NFR-01): el catch de MainLayout.OnInitializedAsync alrededor de
        // TieneEquipoACargoAsync no tenía ninguna ejecución. Misma fábrica que fuerza el fallo en
        // Si_el_listado_falla_se_ve_un_estado_de_error_distinguible: TieneEquipoACargoAsync llega a
        // PermisosService.EmpleadosBajoAutoridadDeAsync, que abre un contexto y encuentra la fábrica
        // que revienta.
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(1));
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
        Services.AddSingleton<SolicitudesService>();

        var layout = Render<MainLayout>(parametros => parametros.Add(pantalla => pantalla.Body, ContenidoDePrueba));

        layout.WaitForState(() => EstadoDelMenu(layout) == MainLayout.EstadoListo, TimeSpan.FromSeconds(10));

        // El link no aparece: la duda ante un fallo real no lleva a nadie a una pantalla que el
        // servidor de todos modos le va a negar.
        Assert.Empty(layout.FindAll($"[data-testid='{MainLayout.IdDelLinkDeAutorizaciones}']"));

        // Y el resto del layout sigue en pie -- @Body, el contenido de prueba --: el catch no tumba la
        // pantalla completa.
        Assert.Contains("contenido de prueba", layout.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(FabricaQueNadieDebeUsar.Marca, layout.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Si_resolver_falla_se_muestra_el_mensaje_sin_romper_la_pantalla()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);
        await SembrarPendienteAsync(subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        RegistrarComoQuienResuelve(manager.Id);

        var pagina = Render<Autorizaciones>();
        pagina.WaitForState(
            () => EstadoDeLaPantalla(pagina) == Autorizaciones.EstadoListado, TimeSpan.FromSeconds(10));

        // Simula la carrera de AC-04: otra persona ya la resolvió, directo contra la base, justo antes
        // del clic — la fila en pantalla queda vieja a propósito.
        await using (var contexto = _baseDeDatos.CrearContexto())
        {
            var solicitud = await contexto.Solicitudes
                .SingleAsync(candidata => candidata.EmpleadoId == subordinado.Id, Cancelacion);
            solicitud.Estado = EstadoSolicitud.Aprobada;
            solicitud.ResueltoPorId = manager.Id;
            solicitud.FechaResolucion = _instanteFijo;
            await contexto.SaveChangesAsync(Cancelacion);
        }

        await pagina.Find($"[data-testid='{Autorizaciones.IdDelBotonDeAprobar}']").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            ErroresDeSolicitud.SolicitudYaResuelta,
            pagina.Find($"[data-testid='{Autorizaciones.IdDelErrorDeResolucion}']").TextContent.Trim());

        // El resto de la pantalla sigue en pie: el título y el estado no cayeron al error genérico.
        Assert.Contains("Autorizaciones", pagina.Markup, StringComparison.Ordinal);
        Assert.NotEqual(Autorizaciones.EstadoError, EstadoDeLaPantalla(pagina));
    }

    [Fact]
    public async Task Si_resolver_lanza_una_excepcion_real_se_muestra_el_mensaje_generico_sin_romper_la_pantalla()
    {
        // Hallazgo 2 de VERIFY: el catch (Exception) genérico de Autorizaciones.ResolverAsync no tenía
        // ninguna ejecución. El test de arriba ejercita el camino de rechazo de NEGOCIO
        // (FueResuelta = false, "SolicitudYaResuelta"); este fuerza una EXCEPCIÓN real de verdad.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);
        await SembrarPendienteAsync(subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3));

        RegistrarComoQuienResuelve(manager.Id);

        var pagina = Render<Autorizaciones>();
        pagina.WaitForState(
            () => EstadoDeLaPantalla(pagina) == Autorizaciones.EstadoListado, TimeSpan.FromSeconds(10));

        // La solicitud desaparece de la base justo antes del clic -- la fila en pantalla queda vieja a
        // propósito, con un Id que ResolverAsync ya no va a encontrar. SolicitudesService.ResolverAsync
        // lanza ArgumentException desde IntentarResolverAsync, que no es un rechazo modelado por
        // ResultadoDeLaResolucion: es la excepción real que el catch genérico tiene que atrapar.
        await using (var contexto = _baseDeDatos.CrearContexto())
        {
            var solicitud = await contexto.Solicitudes
                .SingleAsync(candidata => candidata.EmpleadoId == subordinado.Id, Cancelacion);
            contexto.Solicitudes.Remove(solicitud);
            await contexto.SaveChangesAsync(Cancelacion);
        }

        await pagina.Find($"[data-testid='{Autorizaciones.IdDelBotonDeAprobar}']").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            Autorizaciones.MensajeDeFalloAlResolver,
            pagina.Find($"[data-testid='{Autorizaciones.IdDelErrorDeResolucion}']").TextContent.Trim());

        // El resto de la pantalla sigue en pie: el título no cayó y el estado no se convirtió en el
        // error genérico del listado, que significa otra cosa.
        Assert.Contains("Autorizaciones", pagina.Markup, StringComparison.Ordinal);
        Assert.NotEqual(Autorizaciones.EstadoError, EstadoDeLaPantalla(pagina));
    }

    [Fact]
    public void Si_el_listado_falla_se_ve_un_estado_de_error_distinguible()
    {
        // La base caída: misma reproducción que MisSolicitudesTests.B6_T6, sin depender de que haya, o
        // no, una instancia SQL Server a mano.
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(1));
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
        Services.AddSingleton<SolicitudesService>();

        var pagina = Render<Autorizaciones>();

        Assert.Equal(Autorizaciones.EstadoError, EstadoDeLaPantalla(pagina));

        var error = pagina.Find($"[data-testid='{Autorizaciones.IdDelError}']");
        Assert.Equal(Autorizaciones.MensajeDeFalloInesperado, error.TextContent.Trim());

        // Ni la marca de la fábrica ni el tipo de la excepción (F-SAST-09), y no se confunde con el
        // estado vacío legítimo, que significaría otra cosa.
        Assert.DoesNotContain(FabricaQueNadieDebeUsar.Marca, pagina.Markup, StringComparison.Ordinal);
        Assert.Empty(pagina.FindAll($"[data-testid='{Autorizaciones.IdDelEstadoVacio}']"));
    }

    [Fact]
    public void Sin_empleado_seleccionado_se_ve_un_estado_distinto_del_error_generico()
    {
        // Mismo criterio que MisSolicitudesTests.B6_T9: la ausencia de identidad no es «el listado
        // falló» ni «el equipo no tiene pendientes», es un tercer estado explícito, distinguible desde
        // afuera. Con FabricaQueNadieDebeUsar de testigo: SinEmpleadoSeleccionadoException sale de
        // PermisosService.EmpleadosBajoAutoridadDeAsync antes de abrir ningún contexto, así que si esta
        // pantalla llegara a intentarlo, el test lo delataría.
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.SinSeleccionar());
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
        Services.AddSingleton<SolicitudesService>();

        var pagina = Render<Autorizaciones>();

        Assert.Equal(Autorizaciones.EstadoSinEmpleado, EstadoDeLaPantalla(pagina));

        var aviso = pagina.Find($"[data-testid='{Autorizaciones.IdDelEstadoSinEmpleado}']");
        Assert.Equal(Autorizaciones.MensajeSinEmpleado, aviso.TextContent.Trim());

        // Ni el error genérico ni el vacío legítimo: son estados distintos y significan cosas opuestas.
        Assert.Empty(pagina.FindAll($"[data-testid='{Autorizaciones.IdDelError}']"));
        Assert.Empty(pagina.FindAll($"[data-testid='{Autorizaciones.IdDelEstadoVacio}']"));
        Assert.DoesNotContain(FabricaQueNadieDebeUsar.Marca, pagina.Markup, StringComparison.Ordinal);
    }

    private static string? EstadoDeLaPantalla(IRenderedComponent<Autorizaciones> pagina) =>
        pagina.Find($"[{Autorizaciones.AtributoDelEstado}]").GetAttribute(Autorizaciones.AtributoDelEstado);

    private static string? EstadoDelMenu(IRenderedComponent<MainLayout> layout) =>
        layout.Find($"[{MainLayout.AtributoDelEstado}]").GetAttribute(MainLayout.AtributoDelEstado);

    /// <summary>Cuerpo mínimo para renderizar <c>MainLayout</c> aislado, sin depender del router.</summary>
    private static RenderFragment ContenidoDePrueba => constructor => constructor.AddContent(0, "contenido de prueba");

    private void RegistrarComoQuienResuelve(int empleadoId)
    {
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaDeLaBaseDeTest(_baseDeDatos));
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(empleadoId));
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
        Services.AddSingleton<SolicitudesService>();
    }

    /// <summary>
    /// Registra el proveedor de identidad REAL (FIX-001), no el doble de prueba: es el único que
    /// arranca <c>SinSeleccionar</c> y cambia con <c>SeleccionarAsync</c> validando contra la nómina,
    /// igual que <c>SelectorDeEmpleado.razor</c> en producción.
    /// </summary>
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

    private async Task AsignarManagerAsync(int empleadoId, int managerId)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        var empleado = await contexto.Empleados.SingleAsync(candidato => candidato.Id == empleadoId, Cancelacion);
        empleado.ManagerId = managerId;
        await contexto.SaveChangesAsync(Cancelacion);
    }

    private async Task SembrarPendienteAsync(int empleadoId, DateOnly inicio, DateOnly fin)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        contexto.Solicitudes.Add(new Solicitud
        {
            EmpleadoId = empleadoId,
            FechaInicio = inicio,
            FechaFin = fin,
            DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
            Estado = EstadoSolicitud.Pendiente,
            FechaCreacion = _instanteFijo,
        });
        await contexto.SaveChangesAsync(Cancelacion);
    }
}
