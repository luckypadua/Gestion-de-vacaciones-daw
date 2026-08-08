using GestionVacaciones.Tests.Andamiaje;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// B6-T8 y los otros dos guardarraíles estructurales de la interfaz: ningún componente toca el
/// <c>DbContext</c>, ninguno tiene reloj propio, y ninguno inyecta marcado sin escapar.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué se auditan las fuentes y no el contenedor.</b> Las tres son propiedades de <i>todo</i>
/// componente, incluido el que alguien escriba mañana. Un test que renderizara los cuatro de hoy y
/// mirara qué resolvieron no diría nada del quinto, y estas tres reglas se rompen justamente por ahí:
/// nadie reescribe un componente existente para inyectarle el contexto; lo hace el archivo nuevo, en
/// el que la regla no está a la vista. La misma forma que <c>GuardarrailDeSecretosTests</c> del Bloque
/// 1 aplica sobre los <c>appsettings</c>.
/// </para>
/// </remarks>
public sealed class ComponentesSinAccesoADatosTests
{
    /// <summary>
    /// Acceso a datos. <c>AGENTS.md</c>: «la UI Blazor no consulta la base directamente; pasa siempre
    /// por los servicios». Y NFR-05: cada operación abre y cierra su propio contexto, cosa que un
    /// componente —vivo todo el circuito— no puede garantizar.
    /// </summary>
    private static readonly string[] _tokensDeAccesoADatos =
        ["IDbContextFactory", "VacacionesDbContext", "DbContext"];

    /// <summary>
    /// Fuentes de tiempo. Es lo que vuelve <b>estructural</b> la mitigación R-10: un componente sin
    /// reloj no puede saber qué día es hoy, así que no puede reimplementar AC-02 aunque quiera. La
    /// comparación de fechas la decide <c>SolicitudesService</c>, en el servidor, y la interfaz muestra
    /// el mensaje que aquel devuelve.
    /// </summary>
    private static readonly string[] _tokensDeReloj =
        ["TimeProvider", "DateTime.Today", "DateTime.Now", "DateTime.UtcNow"];

    [Fact]
    public void B6_T8_Ningun_componente_declara_una_inyeccion_del_contexto_ni_de_su_fabrica()
    {
        AuditarComponentes(_tokensDeAccesoADatos);
    }

    [Fact]
    public void Ningun_componente_tiene_su_propia_fuente_de_tiempo()
    {
        AuditarComponentes(_tokensDeReloj);
    }

    [Fact]
    public void Ningun_componente_inyecta_marcado_sin_escapar()
    {
        // Mitigación R-08 y criterio de cierre del bloque. La prohibición es total y no «con entrada de
        // usuario» a propósito: distinguir cuál MarkupString lleva datos del usuario exige leer el
        // código, y esa lectura es la que no ocurre en la revisión apurada que introduce el problema.
        AuditarComponentes(["MarkupString"]);
    }

    private static void AuditarComponentes(string[] tokensProhibidos)
    {
        var raiz = RaizDelRepositorio.Localizar();
        var componentes = ComponentesVersionados(raiz).ToList();

        // Sin esta afirmación, el test pasaría en verde por no haber encontrado nada que revisar: una
        // carpeta renombrada dejaría la auditoría sin objeto y sin aviso.
        Assert.NotEmpty(componentes);

        var infracciones = new List<string>();
        foreach (var componente in componentes)
        {
            var contenido = File.ReadAllText(componente);
            foreach (var token in tokensProhibidos)
            {
                if (contenido.Contains(token, StringComparison.Ordinal))
                {
                    infracciones.Add($"{Path.GetRelativePath(raiz, componente)} menciona «{token}»");
                }
            }
        }

        Assert.Empty(infracciones);
    }

    /// <summary>
    /// Los <c>.razor</c> del proyecto Web. Se recorre el proyecto entero y no solo
    /// <c>Components/</c>: un componente puede nacer en cualquier carpeta y la auditoría no puede
    /// depender de dónde lo dejen.
    /// </summary>
    private static IEnumerable<string> ComponentesVersionados(string raiz)
    {
        var proyectoWeb = Path.Combine(raiz, "src", "GestionVacaciones.Web");

        var opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var archivo in Directory.EnumerateFiles(proyectoWeb, "*.razor", opciones))
        {
            var rutaRelativa = Path.GetRelativePath(proyectoWeb, archivo).Replace('\\', '/');

            // bin/ y obj/ no se versionan y contienen copias generadas del marcado.
            if (rutaRelativa.StartsWith("bin/", StringComparison.Ordinal)
                || rutaRelativa.StartsWith("obj/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return archivo;
        }
    }
}
