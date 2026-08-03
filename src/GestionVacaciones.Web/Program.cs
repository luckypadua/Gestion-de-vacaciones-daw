using GestionVacaciones.Data;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Web.Components;
using GestionVacaciones.Web.Configuracion;
using GestionVacaciones.Web.Identidad;
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

    public static async Task Main(string[] args)
    {
        var aplicacion = ConstruirAplicacion(args);
        ConfigurarCanalizacion(aplicacion);
        await SembrarSiCorrespondeAsync(aplicacion).ConfigureAwait(false);
        await aplicacion.RunAsync().ConfigureAwait(false);
    }

    /// <summary>Construye el host y su contenedor de servicios.</summary>
    /// <param name="args">Argumentos de la línea de comandos.</param>
    /// <param name="ajustarServicios">
    /// Punto de extensión de la composición, que corre <b>después</b> de todos los registros y
    /// <b>antes</b> de los dos guardarraíles del arranque. Lo usan los tests para <b>forzar</b> un
    /// registro que la composición normal no haría —el caso de R-01 en el que el proveedor de desarrollo
    /// queda activo fuera de <c>Development</c>—.
    /// <para>
    /// <b>A qué queda sujeto lo que se fuerce acá.</b> A los dos guardarraíles que corren <b>aguas
    /// abajo</b> de esta línea: el retiro del descriptor del <see cref="VacacionesDbContext"/> (NFR-05),
    /// que ocurre a continuación sobre la colección ya ajustada, y
    /// <see cref="VerificacionDeIdentidad.Verificar"/> (R-01), que corre sobre el contenedor ya
    /// construido. Cualquier guardarraíl que se agregue después tiene que mantener esa propiedad:
    /// colocado antes de esta línea, la costura lo esquiva.
    /// </para>
    /// <para>
    /// <b>Y a qué NO: a la validación de la cadena de conexión (R-02, F-TM-07).</b>
    /// <see cref="CadenaDeConexion.Resolver"/> —que es donde se exige el cifrado verificado hacia SQL
    /// Server— corre <b>aguas arriba</b>, así que la costura no está sujeta a ella: un delegado que
    /// retire <see cref="IDbContextFactory{TContext}"/> y vuelva a registrar la fábrica con
    /// <c>Encrypt=False</c> no vuelve a pasar por esa validación, y nada posterior revalida la cadena de
    /// la fábrica que finalmente se resuelve. Queda escrito como el límite particular que es, en vez de
    /// afirmar la propiedad general —«lo que se fuerce acá queda sujeto a los guardarraíles»—, que sería
    /// afirmar más de lo que se cumple.
    /// </para>
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Si la cadena de conexión falta, no es interpretable, no indica catálogo o —fuera de
    /// <c>Development</c>— no exige cifrado verificado hacia SQL Server (R-02, F-TM-07). También si la
    /// identidad del empleado actual quedó resuelta por el sustituto de desarrollo fuera de
    /// <c>Development</c> (R-01). Falla al arrancar, no en la primera consulta ni en el primer uso.
    /// </exception>
    public static WebApplication ConstruirAplicacion(string[] args, Action<IServiceCollection>? ajustarServicios = null)
    {
        var constructor = WebApplication.CreateBuilder(args);

        var esDesarrollo = constructor.Environment.IsDevelopment();

        var cadenaDeConexion = CadenaDeConexion.Resolver(constructor.Configuration, esDesarrollo);
        constructor.Services.AddSingleton(new CadenaDeConexionResuelta(cadenaDeConexion));

        // AddDbContextFactory y NUNCA AddDbContext (NFR-05). Un DbContext scoped vive todo el
        // circuito Blazor Server, y dos componentes que consultan a la vez lo comparten hasta que EF
        // lanza «A second operation was started on this context instance». Con la fábrica, cada
        // operación abre y cierra el suyo. AGENTS.md lo registra como cicatriz del proyecto.
        //
        // Esta llamada agrega además, por su cuenta, un descriptor scoped del contexto que no
        // corresponde: se retira más abajo, después del punto de extensión de la composición (ver el
        // comentario del RemoveAll, que explica por qué va ahí y no acá).
        constructor.Services.AddDbContextFactory<VacacionesDbContext>(
            opciones => opciones.UseSqlServer(cadenaDeConexion));

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

        RegistrarIdentidad(constructor, esDesarrollo);

        ajustarServicios?.Invoke(constructor.Services);

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
        //
        // Va acá, DESPUÉS del punto de extensión, y no junto al AddDbContextFactory que crea el
        // descriptor. Es un RemoveAll, o sea una operación de un instante y no una condición
        // permanente: colocado antes, cualquier registro posterior lo revierte, y el punto de extensión
        // es precisamente el único lugar por el que llegan registros posteriores —un delegado de dos
        // líneas que pida la fábrica y devuelva un contexto reabre el agujero sin escribir la palabra
        // AddDbContext en ningún archivo, así que ni el test de composición ni el escaneo de fuente lo
        // verían—. El guardarraíl de R-01 no tiene ese problema porque mira el contenedor ya construido.
        constructor.Services.RemoveAll<VacacionesDbContext>();

        var aplicacion = constructor.Build();

        // Mitigación 2 de R-01: la comprobación corre acá, en el arranque, y no en el primer uso. Se
        // hace sobre el contenedor ya construido —no sobre los descriptores— para que cubra también lo
        // que haya registrado el punto de extensión de arriba.
        VerificacionDeIdentidad.Verificar(aplicacion.Services, aplicacion.Environment);

        return aplicacion;
    }

    /// <summary>
    /// Registra la <b>única</b> sede de la identidad del empleado actual (NFR-06), y la nómina que
    /// alimenta el selector.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Mitigación 1 de R-01 (CRITICAL): dos condiciones independientes.</b> El sustituto de
    /// desarrollo —que permite ser cualquier empleado sin credencial— se registra solo si el entorno es
    /// <c>Development</c> <i>y además</i> la clave <c>Vacaciones:PermitirIdentidadDeDesarrollo</c> vale
    /// <c>true</c>. En cualquier otro caso queda <see cref="EmpleadoActualNoConfigurado"/>, que lanza
    /// (AC-06). El valor por defecto es el seguro: habilitar exige declarar dos cosas, no habilitar no
    /// exige nada.
    /// </para>
    /// <para>
    /// <b>Mitigación de R-04:</b> <see cref="EmpleadosService"/> —que devuelve la plantilla completa—
    /// queda atado al MISMO guardarraíl y no a uno propio. Sin las dos condiciones no existe, así que no
    /// hay desde dónde volcar la nómina.
    /// </para>
    /// <para>
    /// <b>Scoped, no singleton.</b> En Blazor Server el ámbito es el circuito: como singleton, quien
    /// eligiera un empleado se lo cambiaría a todos los demás (AC-07). Las dos implementaciones se
    /// registran con el mismo tiempo de vida a propósito, para que el comportamiento de un consumidor no
    /// dependa del entorno. Y ninguna se registra por su propio tipo concreto: la única vía de acceso al
    /// empleado actual es la interfaz (NFR-06).
    /// </para>
    /// </remarks>
    private static void RegistrarIdentidad(WebApplicationBuilder constructor, bool esDesarrollo)
    {
        if (VerificacionDeIdentidad.PermiteIdentidadDeDesarrollo(constructor.Configuration, esDesarrollo))
        {
            constructor.Services.AddScoped<EmpleadosService>();
            constructor.Services.AddScoped<IEmpleadoActualProvider, EmpleadoActualDesarrollo>();

            return;
        }

        constructor.Services.AddScoped<IEmpleadoActualProvider, EmpleadoActualNoConfigurado>();
    }

    /// <summary>
    /// Siembra la nómina de desarrollo, <b>solo</b> en <c>Development</c>. Devuelve <c>null</c> cuando
    /// no corresponde sembrar, para que «no se invocó» sea distinguible de «se invocó y abortó».
    /// </summary>
    /// <remarks>
    /// <para>
    /// La condición del entorno vive acá y no dentro de <see cref="SeedDatos"/>: la semilla decide
    /// sobre qué base puede escribir (R-03), y el host decide si corresponde escribir. Fuera de
    /// <c>Development</c> ni siquiera se resuelve la fábrica: cuatro empleados sin credencial son
    /// identidades utilizables por el selector, y R-01 es exactamente el escenario de arrancar en
    /// producción con la variable de entorno mal puesta.
    /// </para>
    /// <para>
    /// El conjunto declarado es <see cref="CatalogosSembrables.DeLaAplicacion"/> —solo
    /// <c>GestionVacacionesV2</c>—: es el único lugar del código que otorga ese permiso, y lo nombra.
    /// </para>
    /// <para>
    /// Si la escritura falla, la excepción <b>se propaga</b> y la aplicación no arranca. Es
    /// deliberado: una semilla a medias, o una base inalcanzable en desarrollo, son problemas que
    /// conviene ver al arrancar y no tres pantallas después. El aborto por catálogo, en cambio, no
    /// lanza: registra el aviso y la aplicación sigue.
    /// </para>
    /// </remarks>
    public static async Task<ResultadoDeSemilla?> SembrarSiCorrespondeAsync(WebApplication aplicacion)
    {
        ArgumentNullException.ThrowIfNull(aplicacion);

        if (!aplicacion.Environment.IsDevelopment())
        {
            return null;
        }

        var semilla = new SeedDatos(
            aplicacion.Services.GetRequiredService<IDbContextFactory<VacacionesDbContext>>(),
            CatalogosSembrables.DeLaAplicacion,
            aplicacion.Services.GetRequiredService<ILogger<SeedDatos>>());

        return await semilla.SembrarAsync().ConfigureAwait(false);
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
