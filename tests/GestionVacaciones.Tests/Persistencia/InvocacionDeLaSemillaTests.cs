using System.Data.Common;
using GestionVacaciones.Data;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Web.Configuracion;
using Microsoft.AspNetCore.Builder;
using Xunit;
using HostWeb = GestionVacaciones.Web.Program;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// La semilla se invoca desde el arranque y <b>solo</b> en <c>Development</c>.
/// </summary>
/// <remarks>
/// <para>
/// No necesitan la instancia del entorno: necesitan justo lo contrario. Las tres apuntan a un
/// endpoint local sin nadie escuchando, y es esa ausencia la que las vuelve concluyentes —«no se
/// invocó» y «se invocó» se distinguen porque intentar conectarse falla al instante—. Con una base
/// viva, los tres casos se verían iguales desde afuera.
/// </para>
/// <para>
/// Van en la colección del entorno de proceso porque fijan <c>ASPNETCORE_ENVIRONMENT</c>, que es
/// estado global: en paralelo con otra clase que lo lea, una le cambiaría el entorno a la otra.
/// </para>
/// </remarks>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class InvocacionDeLaSemillaTests
{
    /// <summary>
    /// Endpoint local sin nadie escuchando —el sistema operativo rechaza el TCP de inmediato— con el
    /// catálogo <b>de la aplicación</b>: el guardarraíl de R-03 lo admite, así que lo único que puede
    /// impedir la conexión es que la semilla no se haya invocado.
    /// </summary>
    private const string CadenaHaciaNadieConElCatalogoDeLaAplicacion =
        "Server=127.0.0.1,14330;Initial Catalog=GestionVacacionesV2;User ID=usuario-ficticio;" +
        "Password=valor-ficticio-de-test;Encrypt=True;TrustServerCertificate=False;" +
        "Connect Timeout=2;ConnectRetryCount=0";

    /// <summary>
    /// La misma, con un catálogo que no está declarado sembrable. La semilla tiene que abortar
    /// <b>antes</b> de abrir la conexión: si abriera, el endpoint muerto la delataría.
    /// </summary>
    private const string CadenaHaciaNadieConOtroCatalogo =
        "Server=127.0.0.1,14330;Initial Catalog=CatalogoFicticioDeSemilla;User ID=usuario-ficticio;" +
        "Password=valor-ficticio-de-test;Encrypt=True;TrustServerCertificate=False;" +
        "Connect Timeout=2;ConnectRetryCount=0";

    private const string VariableDeEntornoDeAspNet = "ASPNETCORE_ENVIRONMENT";
    private const string VariableDeEntornoDeDotnet = "DOTNET_ENVIRONMENT";

    private const string EntornoDeProduccion = "Production";
    private const string EntornoDeDesarrollo = "Development";

    [Fact]
    public async Task Fuera_de_desarrollo_la_semilla_no_se_invoca()
    {
        // R-01 y R-03 se cruzan acá: fuera de Development no hay nómina ficticia, ni siquiera el
        // intento. La cadena apunta a un catálogo que el guardarraíl SÍ admite, así que este test no
        // se apoya en el guardarraíl: se apoya únicamente en la condición del entorno.
        await using var aplicacion = ConstruirCon(EntornoDeProduccion, CadenaHaciaNadieConElCatalogoDeLaAplicacion);

        var resultado = await HostWeb.SembrarSiCorrespondeAsync(aplicacion);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task En_desarrollo_la_semilla_si_se_invoca()
    {
        // La contracara del anterior. Sin este, «no se invoca en Production» lo cumpliría también una
        // semilla que no se invoca nunca, y el bloque entero quedaría muerto sin que nadie lo note.
        await using var aplicacion = ConstruirCon(EntornoDeDesarrollo, CadenaHaciaNadieConElCatalogoDeLaAplicacion);

        var excepcion = await Record.ExceptionAsync(() => HostWeb.SembrarSiCorrespondeAsync(aplicacion));

        // Se invocó de verdad: llegó hasta el motor, que no está. Y la excepción se propaga en vez de
        // tragarse: una base inalcanzable en desarrollo es un problema, no un silencio.
        Assert.NotNull(excepcion);
        Assert.IsAssignableFrom<DbException>(excepcion);
    }

    [Fact]
    public async Task En_desarrollo_con_otro_catalogo_aborta_antes_de_abrir_la_conexion_y_el_arranque_sigue()
    {
        // Mitigación R-03 vista desde la composición real, que es la que importa: el conjunto que
        // Program.cs le pasa a la semilla es el de la aplicación y no uno permisivo. Si lo fuera, la
        // semilla intentaría escribir y el endpoint muerto haría fallar este test.
        await using var aplicacion = ConstruirCon(EntornoDeDesarrollo, CadenaHaciaNadieConOtroCatalogo);

        var resultado = await HostWeb.SembrarSiCorrespondeAsync(aplicacion);

        // Aborta, y la aplicación sigue arrancando: no lanza.
        Assert.Equal(ResultadoDeSemilla.AbortadaPorCatalogo, resultado);
    }

    private static WebApplication ConstruirCon(string entorno, string cadena)
    {
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, entorno);
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, cadena);

        // El entorno solo hace falta mientras se construye el host: a partir de ahí queda capturado en
        // IWebHostEnvironment, así que restaurarlo acá no le cambia nada a la aplicación devuelta.
        return HostWeb.ConstruirAplicacion([]);
    }
}
