using System.Diagnostics;
using System.Reflection;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Web.Identidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GestionVacaciones.Tests.Identidad;

/// <summary>
/// <b>Capa 2 de la corrección de R-01:</b> un artefacto compilado en <c>Release</c> no registra el
/// sustituto de identidad de desarrollo <b>con ninguna variable de entorno</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué hace falta una tercera condición.</b> Las dos del modelo de amenazas —el entorno y la
/// clave— salen las dos de la configuración, así que las dos las controla quien controla el entorno de
/// ejecución. La Capa 1 vuelve a la clave independiente del entorno, pero sigue siendo una variable
/// que alguien puede exportar. Esta condición no se puede exportar: se decide al compilar, y el
/// artefacto que se despliega se compila en <c>Release</c>. Es el único control que sobrevive a un
/// <c>ASPNETCORE_ENVIRONMENT</c> mal puesto <i>más</i> la clave puesta a mano.
/// </para>
/// <para>
/// <b>La contrapartida, que es intencional:</b> correr la aplicación localmente en <c>Release</c> deja
/// de ofrecer el selector de empleado. Quien necesite el selector corre en <c>Debug</c>, que es como se
/// desarrolla. Está escrito también en <see cref="CompilacionDelArtefacto"/>, donde se lo va a leer.
/// </para>
/// <para>
/// <b>Cómo se verifica algo que decide el compilador.</b> Los tests corren en <c>Debug</c>, así que un
/// <c>#if DEBUG</c> crudo sería inerte y no habría nada que observar. Por eso la decisión pasa por un
/// valor —<see cref="CompilacionDelArtefacto.EsDeDepuracion"/>— y las funciones que la consultan
/// aceptan recibirlo, de modo que un test pueda ejercitar el comportamiento del artefacto de
/// <c>Release</c> sin compilar en Release. Lo que sostiene esa costura son otros dos asertos de este
/// archivo: que el valor refleja de verdad cómo se compiló el ensamblado, y que no hay ningún otro
/// <c>#if</c> por el que la decisión pueda escaparse.
/// </para>
/// </remarks>
public sealed class GuardarrailDeCompilacionTests
{
    [Fact]
    public void Un_artefacto_de_Release_no_habilita_la_identidad_de_desarrollo_ni_con_las_dos_condiciones()
    {
        // Las dos condiciones de R-01 cumplidas: entorno de desarrollo y clave en true. Es exactamente
        // el escenario del hallazgo —variable de entorno mal puesta en un host productivo— y acá el
        // artefacto es el que se despliega.
        var configuracion = ConfiguracionCon("true");

        Assert.False(VerificacionDeIdentidad.PermiteIdentidadDeDesarrollo(
            configuracion, esDesarrollo: true, compiladoParaDepuracion: false));
    }

    [Fact]
    public void Un_artefacto_de_depuracion_con_las_dos_condiciones_si_la_habilita()
    {
        // Contracara imprescindible: sin ella, «Release no habilita» lo cumpliría también un guardarraíl
        // que no habilitara NUNCA, y con eso el entorno de desarrollo de todo el mundo se queda sin
        // selector y el bloque entero deja de servir.
        var configuracion = ConfiguracionCon("true");

        Assert.True(VerificacionDeIdentidad.PermiteIdentidadDeDesarrollo(
            configuracion, esDesarrollo: true, compiladoParaDepuracion: true));
    }

    [Fact]
    public void En_un_artefacto_de_Release_el_arranque_falla_si_el_proveedor_de_desarrollo_quedo_resuelto()
    {
        // La otra mitad, espejo de B4-T4: no alcanza con no registrarlo, hay que impedir que arranque si
        // igual quedó registrado —un error de composición, un host futuro que copie el registro sin
        // copiar la condición—. Acá el entorno ES Development y aun así tiene que lanzar: lo que lo
        // decide es cómo se compiló.
        var excepcion = Assert.Throws<InvalidOperationException>(() => VerificacionDeIdentidad.Verificar(
            ContenedorConElProveedorDeDesarrollo(),
            new EntornoDePrueba(HostConIdentidad.EntornoDeDesarrollo),
            compiladoParaDepuracion: false));

        // El mensaje tiene que nombrar la condición que falló. Con el texto de B4-T4 —que habla del
        // entorno— quien despliega revisaría ASPNETCORE_ENVIRONMENT, que acá está bien.
        Assert.Contains("Release", excepcion.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(EmpleadoActualDesarrollo), excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void En_un_artefacto_de_depuracion_y_en_desarrollo_el_mismo_contenedor_arranca()
    {
        // Contracara del anterior, por el mismo motivo que la del primero.
        VerificacionDeIdentidad.Verificar(
            ContenedorConElProveedorDeDesarrollo(),
            new EntornoDePrueba(HostConIdentidad.EntornoDeDesarrollo),
            compiladoParaDepuracion: true);
    }

    [Fact]
    public void La_constante_de_compilacion_refleja_de_verdad_como_se_compilo_el_ensamblado_Web()
    {
        // Lo que sostiene toda la costura de arriba. Sin este aserto,
        // CompilacionDelArtefacto.EsDeDepuracion podría estar devuelto a mano —un `true` fijo, un
        // `#if` borrado— y los cuatro tests anteriores seguirían verdes ejercitando un parámetro que en
        // producción siempre llega en true. Se compara contra un hecho del ENSAMBLADO, no contra otra
        // constante: el DebuggableAttribute que emite el compilador dice si se compiló sin optimizar.
        var ensambladoWeb = typeof(CompilacionDelArtefacto).Assembly;
        var atributo = ensambladoWeb.GetCustomAttribute<DebuggableAttribute>();

        var compiladoSinOptimizar = atributo is not null && atributo.IsJITOptimizerDisabled;

        Assert.Equal(compiladoSinOptimizar, CompilacionDelArtefacto.EsDeDepuracion);
    }

    [Fact]
    public void La_decision_de_compilacion_vive_en_un_unico_archivo()
    {
        // Escaneo de fuente, como los guardarraíles estructurales del Bloque 6. La costura de arriba
        // solo vale si NO hay otra vía: un `#if DEBUG` suelto en cualquier otro archivo del proyecto Web
        // sería una segunda decisión de compilación, invisible para los tests —que corren en Debug— y
        // por lo tanto exactamente el modo de fallo que este archivo existe para cerrar.
        var raiz = RaizDelRepositorio.Localizar();
        var proyectoWeb = Path.Combine(raiz, "src", "GestionVacaciones.Web");

        var archivos = ArchivosDeCodigo(proyectoWeb).ToList();

        Assert.NotEmpty(archivos);

        // Una DIRECTIVA de verdad: una línea que empieza con «#if». Buscar la subcadena suelta
        // encontraba también los comentarios que hablan de las directivas —este guardarraíl se explica
        // en la documentación de los dos archivos— y ponía en rojo por mencionar la regla.
        var conDirectivaDeCompilacion = archivos
            .Where(archivo => File.ReadLines(archivo).Any(
                linea => linea.TrimStart().StartsWith("#if", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal([$"{nameof(CompilacionDelArtefacto)}.cs"], conDirectivaDeCompilacion);

        // Y la sede de la decisión de R-01 tiene que consultarla. Sin esto, la constante podría existir
        // en su archivo, impecable, y no estar enchufada a nada.
        var sedeDeLaDecision = File.ReadAllText(
            Path.Combine(proyectoWeb, "Identidad", $"{nameof(VerificacionDeIdentidad)}.cs"));

        Assert.Contains(nameof(CompilacionDelArtefacto), sedeDeLaDecision, StringComparison.Ordinal);
    }

    private static IEnumerable<string> ArchivosDeCodigo(string proyectoWeb)
    {
        var opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var archivo in Directory.EnumerateFiles(proyectoWeb, "*.cs", opciones))
        {
            var rutaRelativa = Path.GetRelativePath(proyectoWeb, archivo).Replace('\\', '/');

            // bin/ y obj/ no se versionan, y obj/ contiene el código generado de los .razor.
            if (rutaRelativa.StartsWith("bin/", StringComparison.Ordinal)
                || rutaRelativa.StartsWith("obj/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return archivo;
        }
    }

    private static IConfiguration ConfiguracionCon(string? valor) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection([new(VerificacionDeIdentidad.ClaveDeConfiguracion, valor)])
            .Build();

    /// <summary>
    /// Contenedor con el sustituto de desarrollo registrado, que es lo que la verificación tiene que
    /// encontrar. La fábrica de contextos lanza si alguien la usa: nadie abre conexión acá.
    /// </summary>
    private static IServiceProvider ContenedorConElProveedorDeDesarrollo()
    {
        var servicios = new ServiceCollection();

        servicios.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        servicios.AddScoped<EmpleadosService>();
        servicios.AddScoped<IEmpleadoActualProvider, EmpleadoActualDesarrollo>();

        return servicios.BuildServiceProvider();
    }

    /// <summary>Entorno mínimo: lo único que la verificación mira es el nombre.</summary>
    private sealed class EntornoDePrueba : IHostEnvironment
    {
        public EntornoDePrueba(string nombre) => EnvironmentName = nombre;

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "GestionVacaciones.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
