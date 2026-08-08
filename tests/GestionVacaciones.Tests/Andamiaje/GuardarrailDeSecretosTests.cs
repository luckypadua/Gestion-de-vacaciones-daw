using Xunit;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// B1-T6: mitigación R-02. El repositorio es público y el historial de git conserva lo que se
/// publica una vez, aunque después se borre.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué ya no alcanza con los <c>appsettings</c>.</b> Esta auditoría miraba solo
/// <c>appsettings*.json</c>, razonando que ahí es donde vive la configuración. Pero las cadenas de
/// conexión del repositorio están en archivos <c>.cs</c> —centinelas de test, cadenas de ejemplo en la
/// documentación del código— y esa familia no la revisaba nadie: una cadena real pegada en una
/// constante de test entraba sin que nada se pusiera en rojo. Es además lo que sostiene, hacia
/// adelante, que las cadenas de conexión que encuentra el SAST sean un falso positivo: sin este
/// barrido, «son todas ficticias» es una observación sobre el código de hoy y no una propiedad del
/// repositorio.
/// </para>
/// </remarks>
public sealed class GuardarrailDeSecretosTests
{
    /// <summary>
    /// Marca que declara una línea como <b>parte de esta auditoría</b> y no como una credencial. La
    /// usa exactamente una línea: la que enumera los patrones buscados, que por definición los
    /// contiene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué una marca por línea y no excluir este archivo.</b> Excluirlo entero lo dejaría sin
    /// auditar para siempre, y es un archivo de test como cualquier otro: mañana alguien le agrega un
    /// caso con una cadena de conexión. La marca es visible en la propia línea, así que aparece en el
    /// diff de quien la agregue, que es donde tiene que discutirse.
    /// </para>
    /// </remarks>
    private const string MarcaDeAuditoria = "centinela-de-auditoria";

    private static readonly string[] _patronesProhibidos = ["Password", "User ID", "pwd="]; // centinela-de-auditoria

    private static readonly string[] _carpetasExcluidas = ["bin", "obj", ".git", ".vs", "node_modules"];

    /// <summary>
    /// Los centinelas ficticios que el propio repositorio usa para probar que una cadena con
    /// credenciales se rechaza, o que no se la registra en ningún log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es lista blanca por VALOR, no por archivo.</b> Excluir los archivos que hoy los contienen
    /// dejaría esos archivos sin auditar para siempre, que es justo donde resulta más cómodo pegar una
    /// cadena real «un momentito». Excluyendo el valor, cualquier otra cosa que aparezca al lado sigue
    /// en rojo.
    /// </para>
    /// <para>
    /// Cada uno es inconfundiblemente falso <i>en su propio texto</i>, y eso es lo que hace que la
    /// lista se pueda auditar de un vistazo. Un centinela nuevo que parezca real no debería entrar acá.
    /// </para>
    /// <para>
    /// <c>ValorFicticio</c> es el <i>identificador</i> de la constante que ya vale
    /// <c>valor-ficticio-de-test</c>: en las líneas que la interpolan —<c>Password={ValorFicticio}</c>—
    /// viaja el nombre y no el valor, así que sin él la línea no se distinguiría de una credencial
    /// pegada a mano. Sigue siendo inconfundiblemente falso en su propio texto.
    /// </para>
    /// </remarks>
    private static readonly string[] _centinelasFicticios =
        ["valor-ficticio-de-test", "usuario-ficticio", "host-de-prueba", "ValorFicticio", MarcaDeAuditoria];

    [Fact]
    public void B1_T6_Ningun_appsettings_versionado_contiene_credenciales()
    {
        AuditarVersionados("appsettings*.json", excluirLocales: true);
    }

    [Fact]
    public void Ningun_archivo_de_codigo_versionado_contiene_credenciales()
    {
        // Los .cs del repositorio entero, producción y tests. Es donde el SAST encuentra las cadenas de
        // conexión; que sean ficticias hoy es una observación, y esto lo convierte en una propiedad.
        AuditarVersionados("*.cs", excluirLocales: false);
    }

    [Fact]
    public void Ningun_componente_versionado_contiene_credenciales()
    {
        AuditarVersionados("*.razor", excluirLocales: false);
    }

    private static void AuditarVersionados(string patronDeArchivo, bool excluirLocales)
    {
        var raiz = RaizDelRepositorio.Localizar();
        var archivos = ArchivosVersionados(raiz, patronDeArchivo, excluirLocales).ToList();

        // Sin esta afirmación el test pasaría en verde por no haber encontrado nada que revisar.
        Assert.NotEmpty(archivos);

        var infracciones = new List<string>();
        foreach (var archivo in archivos)
        {
            var contenido = File.ReadAllText(archivo);

            // Los centinelas se retiran ANTES de buscar: lo que queda es todo lo que nadie declaró
            // ficticio. Se retira la línea entera y no solo el valor, porque la credencial ficticia y su
            // clave —«User ID=usuario-ficticio»— viajan juntas en la misma línea.
            var sinCentinelas = string.Join(
                '\n',
                contenido.Split('\n').Where(linea => !_centinelasFicticios.Any(
                    centinela => linea.Contains(centinela, StringComparison.Ordinal))));

            foreach (var patron in _patronesProhibidos)
            {
                if (sinCentinelas.Contains(patron, StringComparison.OrdinalIgnoreCase))
                {
                    infracciones.Add($"{Path.GetRelativePath(raiz, archivo)} contiene '{patron}'");
                }
            }
        }

        Assert.Empty(infracciones);
    }

    private static IEnumerable<string> ArchivosVersionados(string raiz, string patronDeArchivo, bool excluirLocales)
    {
        var opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var archivo in Directory.EnumerateFiles(raiz, patronDeArchivo, opciones))
        {
            var rutaRelativa = Path.GetRelativePath(raiz, archivo).Replace('\\', '/');
            if (rutaRelativa.Split('/').Any(tramo => _carpetasExcluidas.Contains(tramo)))
            {
                continue;
            }

            // `appsettings.*.local.json` está en .gitignore: no se versiona, no se audita.
            if (excluirLocales && Path.GetFileName(archivo).EndsWith(".local.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return archivo;
        }
    }
}
