using GestionVacaciones.Web.Configuracion;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MudBlazor;
using Xunit;
using HostWeb = GestionVacaciones.Web.Program;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// B1-T1, B1-T3, B1-T4 y B1-T5: composición del host y validación de la cadena de conexión en el
/// arranque.
/// </summary>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class ArranqueDelHostTests
{
    private const string CadenaValida =
        "Server=localhost,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=False";

    // Valor deliberadamente malformado: la clave vacía hace fallar al parser de cadenas de conexión.
    // El fragmento "valor-ficticio-de-test" no es una credencial: existe para comprobar que el
    // mensaje de error no lo repite.
    private const string CadenaNoParseable = "=;Password=valor-ficticio-de-test";

    private const string CadenaSinCatalogo =
        "Server=localhost,1433;Integrated Security=True;Encrypt=True;TrustServerCertificate=False";

    private const string CadenaSinCifrado =
        "Server=host-de-prueba,1433;Initial Catalog=GestionVacacionesV2;User ID=usuario-ficticio;" +
        "Password=valor-ficticio-de-test";

    // La forma que usan los contenedores de SQL Server del Block 2: certificado autofirmado.
    private const string CadenaDeContenedorDeTest =
        "Server=127.0.0.1,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=True";

    private const string VariableDeConfiguracionEquivalente = "ConnectionStrings__Vacaciones";

    private const string VariableDeEntornoDeAspNet = "ASPNETCORE_ENVIRONMENT";
    private const string VariableDeEntornoDeDotnet = "DOTNET_ENVIRONMENT";

    private const string EntornoDeProduccion = "Production";
    private const string EntornoDeDesarrollo = "Development";

    /// <summary>Retención de circuitos desconectados que trae Blazor Server por defecto.</summary>
    private static readonly TimeSpan _retencionPorDefectoDeBlazor = TimeSpan.FromMinutes(3);

    /// <summary>Tamaño máximo de mensaje entrante que trae SignalR por defecto, en bytes.</summary>
    private const long TamanoPorDefectoDeSignalR = 32 * 1024;

    [Fact]
    public async Task B1_T1_El_host_se_construye_y_resuelve_la_raiz_de_servicios()
    {
        // El entorno se fija explícitamente: sin esto el test afirmaba DetailedErrors=false apoyado en
        // que ASPNETCORE_ENVIRONMENT no estuviera definida en la máquina, y fallaba en cualquiera que
        // la exportara.
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, EntornoDeProduccion);
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaValida);

        await using var aplicacion = HostWeb.ConstruirAplicacion([]);

        Assert.NotNull(aplicacion.Services.GetRequiredService<IConfiguration>());
        Assert.NotNull(aplicacion.Services.GetRequiredService<ILoggerFactory>());
        Assert.Equal(CadenaValida, aplicacion.Services.GetRequiredService<CadenaDeConexionResuelta>().Valor);

        // MudBlazor registrado. Sus servicios se instancian dentro de un circuito vivo (SnackbarService
        // se suscribe al NavigationManager), así que acá se comprueba el registro, no la instancia.
        var esServicio = aplicacion.Services.GetRequiredService<IServiceProviderIsService>();
        Assert.True(esServicio.IsService(typeof(ISnackbar)));
        Assert.True(esServicio.IsService(typeof(IDialogService)));

        // F-SAST-09: sin errores detallados fuera de Development.
        var opcionesDeCircuito = aplicacion.Services.GetRequiredService<IOptions<CircuitOptions>>().Value;
        Assert.False(opcionesDeCircuito.DetailedErrors);

        // R-06: se afirman LOS CUATRO límites, no solo los dos que endurecen. Un límite configurado
        // más flojo que el default deja la aplicación más expuesta que si no se hubiera escrito, y eso
        // solo se detecta si el test lo mira.
        Assert.Equal(50, opcionesDeCircuito.DisconnectedCircuitMaxRetained);
        Assert.Equal(5, opcionesDeCircuito.MaxBufferedUnacknowledgedRenderBatches);
        Assert.Equal(TimeSpan.FromMinutes(2), opcionesDeCircuito.DisconnectedCircuitRetentionPeriod);
        Assert.True(
            opcionesDeCircuito.DisconnectedCircuitRetentionPeriod < _retencionPorDefectoDeBlazor,
            "La retención de circuitos desconectados no puede ser igual ni mayor que el default de Blazor: sería un no-op vendido como mitigación.");

        var tamanoMaximoDeMensaje = OpcionesDelConcentradorDeComponentes(aplicacion.Services).MaximumReceiveMessageSize;
        Assert.NotNull(tamanoMaximoDeMensaje);
        Assert.Equal(16L * 1024L, tamanoMaximoDeMensaje.Value);
        Assert.True(
            tamanoMaximoDeMensaje.Value <= TamanoPorDefectoDeSignalR,
            "El tamaño máximo de mensaje no puede superar el default de SignalR: lo aflojaría en lugar de mitigarlo.");
    }

    [Fact]
    public async Task En_desarrollo_el_host_acepta_la_cadena_de_los_contenedores_y_habilita_los_errores_detallados()
    {
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, EntornoDeDesarrollo);
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaDeContenedorDeTest);

        await using var aplicacion = HostWeb.ConstruirAplicacion([]);

        var opcionesDeCircuito = aplicacion.Services.GetRequiredService<IOptions<CircuitOptions>>().Value;
        Assert.True(opcionesDeCircuito.DetailedErrors);
        Assert.Equal(CadenaDeContenedorDeTest, aplicacion.Services.GetRequiredService<CadenaDeConexionResuelta>().Valor);
    }

    [Fact]
    public void Fuera_de_desarrollo_el_arranque_rechaza_una_cadena_sin_cifrado()
    {
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, EntornoDeProduccion);
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaSinCifrado);

        var excepcion = Assert.Throws<InvalidOperationException>(() => HostWeb.ConstruirAplicacion([]));

        Assert.Contains("Encrypt", excepcion.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("valor-ficticio-de-test", excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void B1_T3_Sin_ninguna_fuente_de_cadena_el_arranque_lanza()
    {
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);

        var excepcion = Assert.Throws<InvalidOperationException>(() => HostWeb.ConstruirAplicacion([]));

        // El mensaje debe nombrar las dos fuentes posibles: es lo único accionable que tiene quien despliega.
        Assert.Contains(CadenaDeConexion.VariableDeEntorno, excepcion.Message, StringComparison.Ordinal);
        Assert.Contains(CadenaDeConexion.ClaveDeConfiguracion, excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void B1_T4_Cadena_no_parseable_lanza_sin_revelar_el_valor()
    {
        // La fuente perdedora se neutraliza aunque hoy la precedencia la haga inocua: dejar el patrón
        // débil junto al fuerte invita a copiar el débil.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaNoParseable);

        var excepcion = Assert.Throws<InvalidOperationException>(() => HostWeb.ConstruirAplicacion([]));

        Assert.Contains(CadenaDeConexion.VariableDeEntorno, excepcion.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(CadenaNoParseable, excepcion.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("valor-ficticio-de-test", excepcion.Message, StringComparison.OrdinalIgnoreCase);
        // Tampoco encadenada en una excepción interna: ToString() acabaría en un log.
        Assert.DoesNotContain("valor-ficticio-de-test", excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void B1_T5_Cadena_sin_initial_catalog_lanza()
    {
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaSinCatalogo);

        var excepcion = Assert.Throws<InvalidOperationException>(() => HostWeb.ConstruirAplicacion([]));

        Assert.Contains("Initial Catalog", excepcion.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resuelve las opciones del concentrador SignalR que atiende los circuitos Blazor. El tipo del
    /// concentrador es interno al framework, así que la reflexión es la única forma de afirmar sobre
    /// las opciones realmente configuradas en vez de sobre la constante del código de producción.
    /// </summary>
    private static HubOptions OpcionesDelConcentradorDeComponentes(IServiceProvider servicios)
    {
        var tipoDelConcentrador = typeof(CircuitOptions).Assembly
            .GetType("Microsoft.AspNetCore.Components.Server.ComponentHub", throwOnError: true)!;
        var tipoDeOpciones = typeof(IOptions<>)
            .MakeGenericType(typeof(HubOptions<>).MakeGenericType(tipoDelConcentrador));

        var opciones = servicios.GetRequiredService(tipoDeOpciones);
        var valor = tipoDeOpciones.GetProperty("Value")!.GetValue(opciones);

        return Assert.IsAssignableFrom<HubOptions>(valor);
    }
}
