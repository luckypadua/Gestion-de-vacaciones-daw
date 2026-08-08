using System.Text;
using GestionVacaciones.Tests.Identidad;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostWeb = GestionVacaciones.Web.Program;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// La canalización HTTP: el manejador de excepciones genérico y HSTS fuera de <c>Development</c>, y la
/// redirección a HTTPS y la validación antiforgery en todos los entornos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué esto bloqueaba.</b> <c>threat-FEAT-001a.md</c> lista como <b>mitigación 3 de R-01
/// (CRITICAL)</b> que fuera de <c>Development</c> no haya página de excepciones del desarrollador. La
/// mitad de esa mitigación que vive en <c>DetailedErrors = false</c> estaba cubierta; la otra mitad
/// —la rama <c>if (!IsDevelopment())</c> de la canalización, con su manejador genérico— no la
/// ejecutaba ningún test. Borrar <c>UseAntiforgery()</c> o <c>UseHsts()</c> dejaba la suite entera en
/// verde, y la única evidencia de F-SAST-12 era mirar una línea.
/// </para>
/// <para>
/// <b>Cómo se observa sin sumar dependencias.</b> El camino canónico —<c>TestHost</c>,
/// <c>WebApplicationFactory</c>— son paquetes NuGet, y agregarlos es una decisión de PLAN que no
/// corresponde tomar en CODE. Pero <b>no hacen falta</b>: <c>ApplicationBuilder</c>,
/// <c>DefaultHttpContext</c> y las features de antiforgery vienen en el framework compartido que el
/// proyecto ya referencia. Así que estos tests arman la canalización <b>real</b> —los mismos
/// <c>Use*</c>, sobre el contenedor de servicios del host real— le enganchan un terminal propio, y le
/// pasan una petición. Lo que se afirma es el <b>comportamiento observable</b> de la canalización, no
/// el texto del archivo: cada uno de los cuatro <c>Use*</c> tiene al menos un test que se pone rojo si
/// se lo borra.
/// </para>
/// <para>
/// El terminal lo pone el test porque la canalización de la aplicación termina en los componentes
/// Razor, que necesitan un circuito. Lo que está bajo prueba es lo que pasa <i>antes</i> de llegar
/// ahí, que es exactamente donde viven las cuatro mitigaciones.
/// </para>
/// </remarks>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class CanalizacionHttpTests
{
    /// <summary>
    /// Puerto HTTPS al que redirigir. Se declara por variable de entorno —la misma vía que en
    /// producción— porque sin puerto conocido el middleware de redirección no redirige: registra que no
    /// pudo determinarlo y deja pasar la petición. Sin esto, el test de la redirección pasaría en verde
    /// por el motivo equivocado.
    /// </summary>
    private const string VariableDelPuertoHttps = "ASPNETCORE_HTTPS_PORT";

    private const string PuertoHttps = "443";

    /// <summary>
    /// Host que <b>no</b> es de bucle local. HSTS excluye por defecto <c>localhost</c>, <c>127.0.0.1</c>
    /// y <c>[::1]</c>: con cualquiera de ellos la cabecera no se emite nunca y el test afirmaría lo
    /// contrario de lo que cree.
    /// </summary>
    private const string HostPublico = "vacaciones.ejemplo";

    /// <summary>Texto de la excepción del terminal. Si aparece en la respuesta, se filtró la traza.</summary>
    private const string MarcaDeLaExcepcion = "traza-que-no-debe-viajar-al-navegador";

    private const string CabeceraDeHsts = "Strict-Transport-Security";

    [Fact]
    public async Task Fuera_de_Development_una_excepcion_no_escapa_y_la_respuesta_no_lleva_traza()
    {
        // Mitigación 3 de R-01 y F-SAST-09. El terminal revienta: sin manejador, la excepción sube y el
        // servidor contesta con lo que tenga configurado —en Development, la página del desarrollador
        // con la traza, la configuración y el código fuente—.
        var respuesta = await EjecutarAsync(
            HostConIdentidad.EntornoDeProduccion,
            terminal: _ => throw new InvalidOperationException(MarcaDeLaExcepcion));

        Assert.Equal(StatusCodes.Status500InternalServerError, respuesta.Codigo);

        // Cuerpo genérico, y nada más que eso.
        Assert.Equal("Se produjo un error inesperado.", respuesta.Cuerpo);

        Assert.DoesNotContain(MarcaDeLaExcepcion, respuesta.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), respuesta.Cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("GestionVacaciones", respuesta.Cuerpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task En_Development_la_excepcion_se_propaga_para_que_la_vea_quien_desarrolla()
    {
        // La contracara imprescindible. Sin ella, «hay manejador fuera de Development» lo cumpliría
        // también una canalización que lo pusiera SIEMPRE, y con eso quien desarrolla pierde la página
        // de excepciones —el diagnóstico entero— sin que nada se ponga en rojo.
        //
        // Que la excepción salga de la canalización es lo que deja que el host de desarrollo la
        // presente: acá no hay nadie que la atrape.
        var excepcion = await Assert.ThrowsAsync<InvalidOperationException>(() => EjecutarAsync(
            HostConIdentidad.EntornoDeDesarrollo,
            terminal: _ => throw new InvalidOperationException(MarcaDeLaExcepcion)));

        Assert.Equal(MarcaDeLaExcepcion, excepcion.Message);
    }

    [Fact]
    public async Task Fuera_de_Development_la_respuesta_lleva_HSTS()
    {
        var respuesta = await EjecutarAsync(
            HostConIdentidad.EntornoDeProduccion,
            prepararPeticion: SobreHttpsYHostPublico);

        Assert.True(
            respuesta.Cabeceras.ContainsKey(CabeceraDeHsts),
            "La respuesta no lleva Strict-Transport-Security: sin HSTS, la primera petición de cada " +
            "navegador puede viajar en claro.");
    }

    [Fact]
    public async Task En_Development_la_respuesta_no_lleva_HSTS()
    {
        // Contracara, por el mismo motivo que la del manejador de excepciones: HSTS le fija al navegador
        // que ese host es HTTPS por meses. Emitirlo desde el entorno de desarrollo deja al desarrollador
        // sin poder abrir su propio «http://» hasta que limpie el estado del navegador.
        var respuesta = await EjecutarAsync(
            HostConIdentidad.EntornoDeDesarrollo,
            prepararPeticion: SobreHttpsYHostPublico);

        Assert.False(respuesta.Cabeceras.ContainsKey(CabeceraDeHsts));
    }

    [Theory]
    [InlineData(HostConIdentidad.EntornoDeProduccion)]
    [InlineData(HostConIdentidad.EntornoDeDesarrollo)]
    public async Task Una_peticion_en_claro_se_redirige_a_HTTPS_en_todos_los_entornos(string entorno)
    {
        // A diferencia de las dos anteriores, esta no depende del entorno: la aplicación no atiende en
        // claro en ninguno. Se recorren los dos para fijarlo.
        var respuesta = await EjecutarAsync(entorno, prepararPeticion: peticion =>
        {
            peticion.Request.IsHttps = false;
            peticion.Request.Host = new HostString(HostPublico);
            peticion.Request.Path = "/";
        });

        Assert.Equal(StatusCodes.Status307TemporaryRedirect, respuesta.Codigo);
        Assert.StartsWith(
            $"https://{HostPublico}", respuesta.Cabeceras["Location"].ToString(), StringComparison.Ordinal);

        // Y la petición NO siguió: el terminal nunca corrió. Es la mitad que importa —redirigir y
        // además atender sería atender en claro igual—.
        Assert.False(respuesta.LlegoAlTerminal);
    }

    [Theory]
    [InlineData(HostConIdentidad.EntornoDeProduccion)]
    [InlineData(HostConIdentidad.EntornoDeDesarrollo)]
    public async Task La_validacion_antiforgery_corre_en_todos_los_entornos(string entorno)
    {
        // UseAntiforgery() es lo que hace que un POST sin token quede marcado como no validado. Sin esa
        // línea, la feature no existe y el endpoint no tiene cómo enterarse: el formulario de alta
        // aceptaría envíos originados en otro sitio.
        var respuesta = await EjecutarAsync(entorno, prepararPeticion: peticion =>
        {
            SobreHttpsYHostPublico(peticion);
            peticion.Request.Method = HttpMethods.Post;

            // Un endpoint que exige la validación, como el que produce Blazor para sus formularios. Se
            // pone a mano porque acá no hay enrutamiento: lo que está bajo prueba es el middleware.
            peticion.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new ExigeAntiforgery()),
                "endpoint-que-exige-antiforgery"));
        });

        var validacion = respuesta.Contexto.Features.Get<IAntiforgeryValidationFeature>();

        Assert.NotNull(validacion);

        // Y el veredicto es «no válida»: no había token. Sin este aserto, la feature podría estar puesta
        // por cualquier otra cosa.
        Assert.False(validacion.IsValid);
    }

    [Fact]
    public async Task El_envoltorio_de_la_canalizacion_instala_los_middleware_y_no_solo_los_endpoints()
    {
        // EL ESLABÓN QUE ATA EL ENVOLTORIO A LO QUE ENVUELVE, y el que faltaba.
        //
        // Los otros tests de este archivo ejercitan ConfigurarMiddleware, que es donde viven los cuatro
        // Use*. Ninguno comprueba que ConfigurarCanalizacion —lo que la aplicación llama de verdad al
        // arrancar— lo invoque. Borrar esa única línea del envoltorio dejaba la aplicación sin manejador
        // de excepciones, sin HSTS, sin redirección a HTTPS y sin antiforgery —la mitigación 3 de R-01 y
        // F-SAST-12 desaparecidas— con la suite entera en verde. Es el mismo modo de fallo de siempre:
        // el test verifica lo que el código hace, no lo que la aplicación arma.
        //
        // Acá se construye la aplicación real, se la configura por su propia puerta de entrada y se le
        // pasa una petición. La ruta no existe y el fondo de la canalización contesta 404, pero eso es
        // irrelevante: lo que se mira ocurre ANTES de llegar al fondo.
        var canalizacion = ConstruirPorElEnvoltorio(
            HostConIdentidad.EntornoDeProduccion, out var aplicacion);

        await using (aplicacion)
        {
            // Una petición en claro tiene que redirigirse, no atenderse. Sin los middieware instalados,
            // llega al fondo y contesta 404.
            var enClaro = await PasarPorAsync(canalizacion, aplicacion, peticion =>
            {
                peticion.Request.IsHttps = false;
                peticion.Request.Scheme = Uri.UriSchemeHttp;
                peticion.Request.Host = new HostString(HostPublico);
                peticion.Request.Path = "/";
            });

            Assert.Equal(StatusCodes.Status307TemporaryRedirect, enClaro.Codigo);

            // Y una que ya viene por HTTPS tiene que salir con HSTS, porque el entorno no es Development.
            var porHttps = await PasarPorAsync(canalizacion, aplicacion, SobreHttpsYHostPublico);

            Assert.True(
                porHttps.Cabeceras.ContainsKey(CabeceraDeHsts),
                "La aplicación configurada por ConfigurarCanalizacion no emite HSTS: el envoltorio no " +
                "instaló los middleware.");
        }
    }

    [Fact]
    public async Task El_envoltorio_tampoco_instala_HSTS_en_Development()
    {
        // Contracara del anterior, en el mismo punto de entrada. Sin ella, «el envoltorio instala los
        // middleware» lo cumpliría también un envoltorio que instalara la rama de producción siempre.
        var canalizacion = ConstruirPorElEnvoltorio(
            HostConIdentidad.EntornoDeDesarrollo, out var aplicacion);

        await using (aplicacion)
        {
            var porHttps = await PasarPorAsync(canalizacion, aplicacion, SobreHttpsYHostPublico);

            Assert.False(porHttps.Cabeceras.ContainsKey(CabeceraDeHsts));

            // Pero la redirección a HTTPS sí, que no depende del entorno: es lo que distingue «no
            // instaló la rama de producción» de «no instaló nada».
            var enClaro = await PasarPorAsync(canalizacion, aplicacion, peticion =>
            {
                peticion.Request.IsHttps = false;
                peticion.Request.Scheme = Uri.UriSchemeHttp;
                peticion.Request.Host = new HostString(HostPublico);
                peticion.Request.Path = "/";
            });

            Assert.Equal(StatusCodes.Status307TemporaryRedirect, enClaro.Codigo);
        }
    }

    /// <summary>
    /// Construye la aplicación real y la configura por <b>su propia puerta de entrada</b>
    /// —<c>ConfigurarCanalizacion</c>, la que llama <c>Main</c>—, y devuelve la canalización lista para
    /// recibir peticiones.
    /// </summary>
    /// <remarks>
    /// <c>WebApplication</c> implementa <see cref="IApplicationBuilder"/>, así que la canalización se
    /// puede materializar sin levantar el servidor. Los endpoints de los componentes no quedan
    /// enganchados —eso ocurre al arrancar— y no hace falta que queden: lo que este test mira son los
    /// middleware que atienden antes.
    /// </remarks>
    private static RequestDelegate ConstruirPorElEnvoltorio(string entorno, out WebApplication aplicacion)
    {
        using var puertoHttps = new VariableDeEntornoTemporal(VariableDelPuertoHttps, PuertoHttps);

        aplicacion = HostConIdentidad.Construir(entorno, clave: null);

        HostWeb.ConfigurarCanalizacion(aplicacion);

        return ((IApplicationBuilder)aplicacion).Build();
    }

    private static async Task<RespuestaObservada> PasarPorAsync(
        RequestDelegate canalizacion,
        WebApplication aplicacion,
        Action<DefaultHttpContext> prepararPeticion)
    {
        using var ambito = aplicacion.Services.CreateScope();
        using var cuerpo = new MemoryStream();

        var peticion = new DefaultHttpContext
        {
            RequestServices = ambito.ServiceProvider,
        };

        peticion.Request.Method = HttpMethods.Get;
        peticion.Response.Body = cuerpo;

        prepararPeticion(peticion);

        await canalizacion(peticion);

        return new RespuestaObservada(
            peticion,
            peticion.Response.StatusCode,
            Encoding.UTF8.GetString(cuerpo.ToArray()),
            peticion.Response.Headers,
            LlegoAlTerminal: false);
    }

    private static void SobreHttpsYHostPublico(DefaultHttpContext peticion)
    {
        peticion.Request.IsHttps = true;
        peticion.Request.Scheme = Uri.UriSchemeHttps;
        peticion.Request.Host = new HostString(HostPublico);
        peticion.Request.Path = "/";
    }

    /// <summary>
    /// Arma la canalización real para <paramref name="entorno"/> y le pasa una petición.
    /// </summary>
    /// <remarks>
    /// La canalización se construye sobre el <see cref="IServiceProvider"/> del host real, así que los
    /// middleware resuelven exactamente los servicios que resuelven en producción. Lo único que pone el
    /// test es el terminal.
    /// </remarks>
    private static async Task<RespuestaObservada> EjecutarAsync(
        string entorno,
        Action<DefaultHttpContext>? prepararPeticion = null,
        RequestDelegate? terminal = null)
    {
        using var puertoHttps = new VariableDeEntornoTemporal(VariableDelPuertoHttps, PuertoHttps);

        await using var aplicacion = HostConIdentidad.Construir(entorno, clave: null);

        var llegoAlTerminal = false;

        var constructor = new ApplicationBuilder(aplicacion.Services);

        HostWeb.ConfigurarMiddleware(constructor, aplicacion.Environment);

        constructor.Run(contexto =>
        {
            llegoAlTerminal = true;
            return terminal?.Invoke(contexto) ?? Task.CompletedTask;
        });

        var canalizacion = constructor.Build();

        using var ambito = aplicacion.Services.CreateScope();
        using var cuerpo = new MemoryStream();

        var peticion = new DefaultHttpContext
        {
            RequestServices = ambito.ServiceProvider,
        };

        peticion.Request.Method = HttpMethods.Get;

        // Por defecto, una petición que YA viene por HTTPS. Con una en claro, la redirección
        // cortocircuita la canalización antes del terminal y ningún test que necesite llegar más
        // adentro podría hacerlo: el caso de la petición en claro lo arma explícitamente el test que lo
        // afirma.
        SobreHttpsYHostPublico(peticion);

        peticion.Response.Body = cuerpo;

        prepararPeticion?.Invoke(peticion);

        await canalizacion(peticion);

        return new RespuestaObservada(
            peticion,
            peticion.Response.StatusCode,
            Encoding.UTF8.GetString(cuerpo.ToArray()),
            peticion.Response.Headers,
            llegoAlTerminal);
    }

    private sealed record RespuestaObservada(
        DefaultHttpContext Contexto,
        int Codigo,
        string Cuerpo,
        IHeaderDictionary Cabeceras,
        bool LlegoAlTerminal);

    /// <summary>
    /// Metadato que declara que el endpoint exige validación antiforgery. Es lo que Blazor le pone a
    /// los endpoints de sus formularios; acá se implementa la interfaz pública del framework porque la
    /// clase que la implementa allá es interna.
    /// </summary>
    private sealed class ExigeAntiforgery : IAntiforgeryMetadata
    {
        public bool RequiresValidation => true;
    }
}
