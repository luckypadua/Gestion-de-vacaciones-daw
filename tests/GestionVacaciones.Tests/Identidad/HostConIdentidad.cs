using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Web.Configuracion;
using GestionVacaciones.Web.Identidad;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
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
/// copias de los <c>appsettings</c> del proyecto Web, y lo que ahí diga la configuración se sumaría a
/// lo que el test declara. Apuntando la raíz a un directorio vacío del propio <c>bin/</c>, las dos
/// entradas de la doble condición quedan exclusivamente en manos del test.
/// <para>
/// <b>Ya no es tan crítico como era.</b> Hasta la corrección de R-01, el
/// <c>appsettings.Development.json</c> declaraba <c>PermitirIdentidadDeDesarrollo: true</c>, así que
/// sin mover la raíz el caso «la clave está ausente» ni siquiera se podía construir. Hoy esa clave ya
/// no está en ningún <c>appsettings</c> —vive en <c>launchSettings.json</c>, que no se publica— y
/// mover la raíz pasa a ser aislamiento y no una necesidad. Se conserva por eso: aísla.
/// </para>
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
    /// Saltea el test en curso si el proyecto Web se compiló en <c>Release</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué existe.</b> La capa 2 de la mitigación de R-01 impide que un artefacto de
    /// <c>Release</c> registre el sustituto de identidad de desarrollo, con cualquier entorno y
    /// cualquier clave. Los casos que afirman sobre el proveedor de desarrollo <i>resuelto desde el
    /// host</i> describen entonces el comportamiento del artefacto de depuración, y bajo
    /// <c>dotnet test -c Release</c> se quedan sin objeto: lo correcto es que no corran, no que fallen.
    /// Una suite roja por la configuración con la que se la compiló enseña a ignorar el rojo, que es
    /// exactamente el criterio que el fixture de integración ya aplica cuando no hay instancia
    /// SQL Server.
    /// </para>
    /// <para>
    /// <b>Esto no deja la capa 2 sin verificar en Release: la verifica mejor.</b>
    /// <c>GuardarrailDeCompilacionTests</c> corre en las dos configuraciones, y en <c>Release</c> el
    /// salteo de estos casos <i>es</i> la observación de que el guardarraíl funcionó de punta a punta
    /// sobre un artefacto real.
    /// </para>
    /// </remarks>
    public static void SaltearSiElArtefactoNoEsDeDepuracion() => Assert.SkipUnless(
        CompilacionDelArtefacto.EsDeDepuracion,
        "El proyecto Web se compiló en Release y la capa 2 de R-01 impide registrar el sustituto de " +
        "identidad de desarrollo en un artefacto desplegable. Este caso afirma sobre el artefacto de " +
        "depuración, así que acá no tiene objeto. La condición de compilación la verifica " +
        "GuardarrailDeCompilacionTests, que sí corre en las dos configuraciones.");

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
