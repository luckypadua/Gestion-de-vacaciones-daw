using GestionVacaciones.Data;
using GestionVacaciones.Web.Components;
using GestionVacaciones.Web.Configuracion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;

namespace GestionVacaciones.Web;

/// <summary>
/// Composición del host Blazor Server. La construcción del contenedor y la configuración de la
/// canalización viven en métodos separados y públicos para que el arranque sea verificable sin
/// levantar el servidor.
/// </summary>
public static class Program
{
    /// <summary>Circuitos desconectados que se retienen antes de descartar el más antiguo (R-06).</summary>
    private const int MaximoDeCircuitosDesconectadosRetenidos = 50;

    /// <summary>Lotes de render pendientes de confirmación por circuito (R-06).</summary>
    private const int MaximoDeLotesDeRenderSinConfirmar = 5;

    /// <summary>
    /// Tamaño máximo de un mensaje entrante del circuito, en bytes (R-06). SignalR permite 32 KiB por
    /// defecto; este límite lo baja a la mitad porque la aplicación no transporta nada grande por el
    /// circuito: dos fechas y la selección de un desplegable. Un valor por encima del default no
    /// mitigaría nada, dejaría la aplicación más expuesta que sin escribir la línea.
    /// </summary>
    private const long TamanoMaximoDeMensajeRecibido = 16 * 1024;

    /// <summary>
    /// Cuánto se retiene un circuito desconectado antes de descartarlo (R-06). Blazor retiene 3
    /// minutos por defecto: repetir ese valor sería un no-op etiquetado como mitigación, así que se
    /// baja. Cada circuito retenido es memoria del servidor reservada para una sesión que quizá no
    /// vuelva, y una reconexión legítima ocurre en segundos.
    /// </summary>
    private static readonly TimeSpan _retencionDeCircuitosDesconectados = TimeSpan.FromMinutes(2);

    public static void Main(string[] args)
    {
        var aplicacion = ConstruirAplicacion(args);
        ConfigurarCanalizacion(aplicacion);
        aplicacion.Run();
    }

    /// <summary>Construye el host y su contenedor de servicios.</summary>
    /// <exception cref="InvalidOperationException">
    /// Si la cadena de conexión falta, no es interpretable, no indica catálogo o —fuera de
    /// <c>Development</c>— no exige cifrado verificado hacia SQL Server (R-02, F-TM-07). Falla al
    /// arrancar, no en la primera consulta.
    /// </exception>
    public static WebApplication ConstruirAplicacion(string[] args)
    {
        var constructor = WebApplication.CreateBuilder(args);

        var esDesarrollo = constructor.Environment.IsDevelopment();

        var cadenaDeConexion = CadenaDeConexion.Resolver(constructor.Configuration, esDesarrollo);
        constructor.Services.AddSingleton(new CadenaDeConexionResuelta(cadenaDeConexion));

        // AddDbContextFactory y NUNCA AddDbContext (NFR-05). Un DbContext scoped vive todo el
        // circuito Blazor Server, y dos componentes que consultan a la vez lo comparten hasta que EF
        // lanza «A second operation was started on this context instance». Con la fábrica, cada
        // operación abre y cierra el suyo. AGENTS.md lo registra como cicatriz del proyecto.
        constructor.Services.AddDbContextFactory<VacacionesDbContext>(
            opciones => opciones.UseSqlServer(cadenaDeConexion));

        // AddDbContextFactory agrega POR SU CUENTA un descriptor scoped de VacacionesDbContext que
        // delega en la fábrica, como comodidad para quien prefiera inyectar el contexto. Se retira:
        // scoped en Blazor Server es «uno por circuito», así que un componente que inyectara el
        // contexto obtendría el mismo de siempre y volvería a caer en «A second operation was started
        // on this context instance» —justo lo que NFR-05 quiere impedir— sin escribir en ningún lado
        // la palabra AddDbContext. Sin el descriptor, un componente con «@inject VacacionesDbContext»
        // revienta al activarse, en su primer render: NO al arrancar —ValidateOnBuild solo alcanza a
        // los servicios registrados, y los componentes Blazor no lo son—, pero sí el 100% de las
        // veces y en la primera prueba manual, en vez de una vez cada tantas bajo concurrencia.
        // Fallar siempre es lo que lo vuelve un guardarraíl; fallar a veces es una trampa.
        constructor.Services.RemoveAll<VacacionesDbContext>();

        constructor.Services
            .AddRazorComponents()
            .AddInteractiveServerComponents(opciones =>
            {
                // Fuera de Development no se filtran trazas al navegador (F-SAST-09).
                opciones.DetailedErrors = esDesarrollo;
                opciones.DisconnectedCircuitMaxRetained = MaximoDeCircuitosDesconectadosRetenidos;
                opciones.DisconnectedCircuitRetentionPeriod = _retencionDeCircuitosDesconectados;
                opciones.MaxBufferedUnacknowledgedRenderBatches = MaximoDeLotesDeRenderSinConfirmar;
            })
            .AddHubOptions(opciones => opciones.MaximumReceiveMessageSize = TamanoMaximoDeMensajeRecibido);

        constructor.Services.AddMudServices();

        return constructor.Build();
    }

    /// <summary>Configura la canalización HTTP.</summary>
    public static WebApplication ConfigurarCanalizacion(WebApplication aplicacion)
    {
        ArgumentNullException.ThrowIfNull(aplicacion);

        if (!aplicacion.Environment.IsDevelopment())
        {
            // Sin página de excepciones del desarrollador: la respuesta no lleva traza ni
            // configuración, solo un mensaje genérico (F-SAST-09).
            aplicacion.UseExceptionHandler(manejador => manejador.Run(async contexto =>
            {
                contexto.Response.StatusCode = StatusCodes.Status500InternalServerError;
                contexto.Response.ContentType = "text/plain; charset=utf-8";
                await contexto.Response.WriteAsync("Se produjo un error inesperado.").ConfigureAwait(false);
            }));

            aplicacion.UseHsts();
        }

        aplicacion.UseHttpsRedirection();
        aplicacion.UseStaticFiles();
        aplicacion.UseAntiforgery();

        aplicacion.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return aplicacion;
    }
}
