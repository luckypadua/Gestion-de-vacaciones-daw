using Bunit;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Tests.Persistencia;
using GestionVacaciones.Web.Components.Identidad;
using GestionVacaciones.Web.Components.Pages;
using GestionVacaciones.Web.Components.Solicitudes;
using GestionVacaciones.Web.Identidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// B6-T6 y B6-T9: los <b>tres estados que se ven parecidos y significan cosas distintas</b> —sin
/// empleado seleccionado, sin solicitudes, y error del servicio— tienen que ser distinguibles en el
/// marcado renderizado, no solo en el código.
/// </summary>
/// <remarks>
/// <para>
/// Es la preocupación que atraviesa todo el ticket: <c>IdentidadDelEmpleado</c>,
/// <c>SinEmpleadoSeleccionadoException</c> y <c>AccesoASolicitudesDenegadoException</c> existen como
/// tipos separados exactamente para que la interfaz pueda separarlos. Estos tests son el punto donde
/// esa inversión se cobra: si la página los colapsara, todo aquel diseño no habría servido de nada.
/// </para>
/// <para>
/// El tercer estado —el vacío legítimo— lo fija <see cref="ListadoDeSolicitudesTests"/> sobre el
/// componente que lo renderiza. Acá se afirma que <b>no</b> aparece cuando no corresponde, que es la
/// mitad que se puede romper sin que aquel test se entere.
/// </para>
/// <para>
/// <b>El selector, en estos casos, no consigue su nómina</b>: la fábrica de contextos lanza. Es una
/// consecuencia buscada —vuelven a correr en cualquier máquina, con instancia SQL Server o sin ella— y
/// no debilita lo que afirman, porque el fallo de la nómina tiene su propio marcador y ninguno de los
/// asertos lo confunde con el de la página. Que la selección cambie efectivamente el listado es AC-07,
/// y lo verifica <see cref="SeleccionDeEmpleadoEnLaInterfazTests"/> contra la base real.
/// </para>
/// </remarks>
public sealed class MisSolicitudesTests : ContextoDeComponentes
{
    private const int EmpleadoActual = 7;

    public MisSolicitudesTests()
    {
        // Ninguno de estos casos llega a la base: o no hay identidad, o el propósito del test es que el
        // acceso a datos falle. La fábrica que lanza en cuanto alguien la usa provoca lo segundo sin
        // depender de que haya —o no— una instancia SQL Server a mano.
        Services.AddSingleton<TimeProvider>(new TiempoFijo(new DateTimeOffset(2026, 8, 3, 9, 0, 0, TimeSpan.FromHours(-3))));
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        Services.AddSingleton<PermisosService>();

        // FEAT-001b, Bloque 3: quinta dependencia de SolicitudesService. Se registra igual que en
        // Program.RegistrarDominio y en ComposicionDeLaPantalla —el ILogger<SolicitudesService> que
        // pide la sexta lo resuelve Services.AddLogging() de ContextoDeComponentes—: sin este registro,
        // el contenedor no puede activar SolicitudesService y la resolución revienta antes de que
        // ningún caso llegue a afirmar nada, en vez de reproducir el escenario que cada test describe.
        Services.AddSingleton<SaldoService>();
        Services.AddSingleton<SolicitudesService>();
        Services.AddSingleton<EmpleadosService>();
    }

    [Fact]
    public void B6_T6_Si_el_servicio_lanza_se_muestra_un_mensaje_generico_y_ninguna_traza()
    {
        // La base caída. El empleado está resuelto, así que la página pide el listado y el acceso a
        // datos revienta.
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(EmpleadoActual));

        var pagina = Render<MisSolicitudes>();

        Assert.Equal(MisSolicitudes.EstadoError, EstadoDeLaPantalla(pagina));

        var error = pagina.Find($"[data-testid='{MisSolicitudes.IdDelError}']");
        Assert.Contains(MisSolicitudes.MensajeDeFalloInesperado, error.TextContent, StringComparison.Ordinal);

        // F-SAST-09: ni el mensaje de la excepción, ni su tipo, ni una traza. La marca de la fábrica es
        // el texto exacto de la excepción que se produjo: si apareciera, sería la excepción impresa.
        Assert.DoesNotContain(FabricaQueNadieDebeUsar.Marca, pagina.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), pagina.Markup, StringComparison.Ordinal);

        // El espacio de nombres del dominio: es lo que encabeza cada línea de una traza de pila, y no
        // se busca la palabra «at» porque el runtime la traduce según la cultura del proceso.
        Assert.DoesNotContain("GestionVacaciones.Data", pagina.Markup, StringComparison.Ordinal);

        // Y no se confunde con las otras dos situaciones: ni «no enviaste solicitudes» ni el selector.
        Assert.Empty(pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDelEstadoVacio}']"));
    }

    [Fact]
    public void B6_T9_Sin_empleado_seleccionado_se_renderiza_el_selector_y_no_un_listado_vacio()
    {
        // El estado inicial de todo circuito recién abierto. Un listado vacío acá se leería como
        // «todavía no enviaste solicitudes», que es una afirmación sobre datos que nadie consultó.
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.SinSeleccionar());

        var pagina = Render<MisSolicitudes>();

        Assert.Equal(MisSolicitudes.EstadoSinEmpleado, EstadoDeLaPantalla(pagina));

        // Lo que se ofrece es elegir un empleado.
        Assert.Single(pagina.FindAll($"[data-testid='{SelectorDeEmpleado.IdDelSelector}']"));

        // Distinguible de B6-T7: el estado vacío del listado NO está.
        Assert.Empty(pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDelEstadoVacio}']"));

        // Y distinguible de B6-T6: tampoco hay error del listado. La página no consultó nada, así que
        // no hay nada que pudiera haber fallado.
        Assert.Empty(pagina.FindAll($"[data-testid='{MisSolicitudes.IdDelError}']"));

        // Sin empleado no hay a nombre de quién crear una solicitud: el formulario tampoco se ofrece.
        Assert.Empty(pagina.FindAll($"[data-testid='{FormularioDeAlta.IdDelFormulario}']"));
    }

    [Fact]
    public void Fuera_de_Development_la_pantalla_niega_sin_publicar_el_motivo_tecnico()
    {
        // AC-06 llevado a la interfaz. EmpleadoActualNoConfigurado lanza al resolver la identidad —no
        // devuelve un empleado por defecto— y la página tiene que sobrevivir a eso: sin este caso, la
        // pantalla principal de la aplicación revienta con una excepción sin manejar en el único
        // entorno donde el guardarraíl de R-01 está activo.
        Services.AddSingleton<IEmpleadoActualProvider, EmpleadoActualNoConfigurado>();

        var pagina = Render<MisSolicitudes>();

        Assert.Equal(MisSolicitudes.EstadoError, EstadoDeLaPantalla(pagina));

        // Sin selector: este proveedor contesta que no se puede elegir, y ofrecer la elección sería
        // ofrecer justamente lo que R-01 prohíbe fuera de desarrollo.
        Assert.Empty(pagina.FindAll($"[data-testid='{SelectorDeEmpleado.IdDelSelector}']"));

        // El mensaje de negación nombra la configuración interna —RF-01, la clave del entorno— y es un
        // diagnóstico para quien opera, no para quien mira la pantalla (F-SAST-09).
        Assert.DoesNotContain(
            EmpleadoActualNoConfigurado.MensajeDeNegacion, pagina.Markup, StringComparison.Ordinal);
    }

    private static string? EstadoDeLaPantalla(IRenderedComponent<MisSolicitudes> pagina) =>
        pagina.Find($"[{MisSolicitudes.AtributoDelEstado}]").GetAttribute(MisSolicitudes.AtributoDelEstado);
}

/// <summary>
/// FEAT-002 (Bloque 5, AC-06): la mitad de <c>MisSolicitudes</c> que necesita datos reales de una
/// solicitud ya resuelta —no hay proveedor en memoria en la spec, mismo criterio que
/// <c>SaldoEnPantallaConBaseDeDatosTests</c>—, así que vive en su propia clase con la instancia SQL
/// Server del entorno.
/// </summary>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class MisSolicitudesConBaseDeDatosTests : ContextoDeComponentes
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 7, 9, 15, 0, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;
    private readonly TiempoFijo _tiempo = new(_instanteFijo);

    public MisSolicitudesConBaseDeDatosTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task Mis_solicitudes_muestra_quien_resolvio_y_el_motivo_del_rechazo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        // Orden deliberado: las solicitudes de "empleado" quedan con ResueltoPorId = manager.Id (FK
        // NoAction), así que al limpiar tiene que borrarse PRIMERO -- el await using descarta en orden
        // inverso al de declaración, así que "empleado", declarado último, se descarta primero.
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        const string motivo = "No hay cobertura ese día";

        await SembrarResueltaAsync(
            empleado.Id, manager.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3),
            EstadoSolicitud.Aprobada, motivoDeRechazo: null);

        await SembrarResueltaAsync(
            empleado.Id, manager.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2),
            EstadoSolicitud.Rechazada, motivoDeRechazo: motivo);

        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaDeLaBaseDeTest(_baseDeDatos));
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(empleado.Id));
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
        Services.AddSingleton<SolicitudesService>();
        Services.AddSingleton<EmpleadosService>();

        var pagina = Render<MisSolicitudes>();

        pagina.WaitForState(
            () => pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDeLaFila}']").Count == 2,
            TimeSpan.FromSeconds(10));

        var filas = pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDeLaFila}']");

        var filaAprobada = filas.Single(fila =>
            fila.QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDelEstado}']")!.TextContent.Trim()
            == nameof(EstadoSolicitud.Aprobada));
        var filaRechazada = filas.Single(fila =>
            fila.QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDelEstado}']")!.TextContent.Trim()
            == nameof(EstadoSolicitud.Rechazada));

        // AC-06: quién la resolvió, visible en la aprobada.
        var resueltoPor = filaAprobada.QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDelResueltoPor}']");
        Assert.NotNull(resueltoPor);
        Assert.Contains(manager.Id.ToString(), resueltoPor.TextContent, StringComparison.Ordinal);

        // Y el motivo, solo en la rechazada.
        var motivoRenderizado = filaRechazada.QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDelMotivo}']");
        Assert.NotNull(motivoRenderizado);
        Assert.Equal(motivo, motivoRenderizado.TextContent.Trim());

        // La aprobada no lleva motivo: no fue un rechazo.
        Assert.Null(filaAprobada.QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDelMotivo}']"));
    }

    private async Task SembrarResueltaAsync(
        int empleadoId,
        int resueltoPorId,
        DateOnly inicio,
        DateOnly fin,
        EstadoSolicitud estado,
        string? motivoDeRechazo)
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        contexto.Solicitudes.Add(new Solicitud
        {
            EmpleadoId = empleadoId,
            FechaInicio = inicio,
            FechaFin = fin,
            DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
            Estado = estado,
            FechaCreacion = _instanteFijo,
            ResueltoPorId = resueltoPorId,
            FechaResolucion = _instanteFijo,
            MotivoDeRechazo = motivoDeRechazo,
        });

        await contexto.SaveChangesAsync(Cancelacion);
    }
}
