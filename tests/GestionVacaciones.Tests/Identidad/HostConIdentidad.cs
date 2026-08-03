using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Web.Configuracion;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using HostWeb = GestionVacaciones.Web.Program;

namespace GestionVacaciones.Tests.Identidad;

/// <summary>
/// Construye el host con el entorno y la clave de identidad de desarrollo puestos a mano. Existe
/// porque la doble condición de R-01 se decide con esas dos entradas y los tests del bloque necesitan
/// recorrer sus cuatro combinaciones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué se le cambia la raíz de contenido.</b> El directorio de salida de los tests contiene
/// copias del <c>appsettings.json</c> y del <c>appsettings.Development.json</c> del proyecto Web, y
/// ese último declara <c>PermitirIdentidadDeDesarrollo: true</c>. Sin mover la raíz de contenido, el
/// caso «la clave está ausente» —que es el que fija el valor por defecto seguro— no se puede
/// construir: la configuración del proyecto la aportaría siempre. Se apunta entonces a un directorio
/// vacío del propio <c>bin/</c>, con lo que las dos entradas quedan exclusivamente en manos del test.
/// </para>
/// <para>
/// El aserto de que ese mecanismo funciona no es implícito: lo fija
/// <c>El_host_de_prueba_deja_ausente_la_clave_de_identidad_de_desarrollo</c>. Sin él, el caso de la
/// clave ausente podría estar pasando por el motivo equivocado.
/// </para>
/// </remarks>
internal static class HostConIdentidad
{
    /// <summary>
    /// Cadena bien formada que no apunta a ninguna instancia viva, con un catálogo deliberadamente
    /// ficticio: ninguno de los tests de composición abre conexión, y nombrar acá una base real sería
    /// el primer paso para tocarla.
    /// </summary>
    public const string CadenaValida =
        "Server=localhost,1433;Initial Catalog=CatalogoFicticioDeIdentidad;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=False";

    public const string VariableDeEntornoDeAspNet = "ASPNETCORE_ENVIRONMENT";
    public const string VariableDeEntornoDeDotnet = "DOTNET_ENVIRONMENT";

    /// <summary>
    /// Grafía de <see cref="Web.Identidad.VerificacionDeIdentidad.ClaveDeConfiguracion"/> como
    /// variable de entorno: el proveedor de configuración del host traduce el doble guion bajo a los
    /// dos puntos de la clave.
    /// </summary>
    public const string VariableDeEntornoDeLaClave = "Vacaciones__PermitirIdentidadDeDesarrollo";

    public const string EntornoDeDesarrollo = "Development";
    public const string EntornoDeProduccion = "Production";

    private const string NombreDeLaRaizSinConfiguracion = "ContenidoSinConfiguracion";

    /// <summary>
    /// Construye el host con <paramref name="entorno"/> y con la clave de identidad de desarrollo en
    /// <paramref name="clave"/>. Un <c>null</c> en la clave significa <b>ausente</b>, no «false».
    /// </summary>
    /// <param name="ajustarServicios">
    /// Punto de extensión de la composición, que los tests usan para <b>forzar</b> un registro que la
    /// composición normal no haría. No puede debilitar el guardarraíl de R-01: la verificación de
    /// identidad corre después, sobre el contenedor ya construido.
    /// </param>
    /// <param name="raizDeContenido">
    /// Raíz de contenido del host. Por defecto, el directorio vacío que deja la clave exclusivamente en
    /// manos del test. Se le pasa <see cref="RaizDelProyectoWeb"/> cuando lo que se quiere verificar es
    /// justamente la configuración <b>versionada</b>.
    /// </param>
    public static WebApplication Construir(
        string entorno,
        string? clave,
        string? cadena = null,
        Action<IServiceCollection>? ajustarServicios = null,
        string? raizDeContenido = null)
    {
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, entorno);
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variableDeCadena = new VariableDeEntornoTemporal(
            CadenaDeConexion.VariableDeEntorno, cadena ?? CadenaValida);
        using var variableDeClave = new VariableDeEntornoTemporal(VariableDeEntornoDeLaClave, clave);

        // El entorno y la clave solo hacen falta mientras se construye el host: de ahí en adelante
        // quedan capturados en IWebHostEnvironment y en la configuración ya materializada.
        return HostWeb.ConstruirAplicacion(
            [$"--contentRoot={raizDeContenido ?? RaizSinConfiguracion()}"],
            ajustarServicios);
    }

    /// <summary>
    /// Directorio vacío al que se apunta la raíz de contenido por defecto, para que la clave quede
    /// exclusivamente en manos del test. Vive dentro de <c>bin/</c> —no en el temporal del sistema— para
    /// que se lo lleve el mismo <c>clean</c> que borra la salida del build y no quede basura fuera del
    /// repositorio.
    /// </summary>
    public static string RaizSinConfiguracion()
    {
        var raiz = Path.Combine(AppContext.BaseDirectory, NombreDeLaRaizSinConfiguracion);
        Directory.CreateDirectory(raiz);
        return raiz;
    }

    /// <summary>
    /// Directorio del proyecto Web <b>en el repositorio</b>, con sus <c>appsettings</c> versionados. No
    /// es el directorio de salida: lo que ahí hay son copias, y una copia no es evidencia de lo que se
    /// commiteó.
    /// </summary>
    public static string RaizDelProyectoWeb() =>
        Path.Combine(RaizDelRepositorio.Localizar(), "src", "GestionVacaciones.Web");
}
