using Xunit;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// B1-T6: mitigación R-02. El repositorio es público y el historial de git conserva lo que se
/// publica una vez, aunque después se borre.
/// </summary>
public sealed class GuardarrailDeSecretosTests
{
    private static readonly string[] _patronesProhibidos = ["Password", "User ID", "pwd="];

    private static readonly string[] _carpetasExcluidas = ["bin", "obj", ".git", "node_modules"];

    [Fact]
    public void B1_T6_Ningun_appsettings_versionado_contiene_credenciales()
    {
        var raiz = RaizDelRepositorio.Localizar();
        var archivos = ArchivosDeConfiguracionVersionados(raiz).ToList();

        // Sin esta afirmación el test pasaría en verde por no haber encontrado nada que revisar.
        Assert.NotEmpty(archivos);

        var infracciones = new List<string>();
        foreach (var archivo in archivos)
        {
            var contenido = File.ReadAllText(archivo);
            foreach (var patron in _patronesProhibidos)
            {
                if (contenido.Contains(patron, StringComparison.OrdinalIgnoreCase))
                {
                    infracciones.Add($"{Path.GetRelativePath(raiz, archivo)} contiene '{patron}'");
                }
            }
        }

        Assert.Empty(infracciones);
    }

    private static IEnumerable<string> ArchivosDeConfiguracionVersionados(string raiz)
    {
        var opciones = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var archivo in Directory.EnumerateFiles(raiz, "appsettings*.json", opciones))
        {
            var rutaRelativa = Path.GetRelativePath(raiz, archivo).Replace('\\', '/');
            if (rutaRelativa.Split('/').Any(tramo => _carpetasExcluidas.Contains(tramo)))
            {
                continue;
            }

            // `appsettings.*.local.json` está en .gitignore: no se versiona, no se audita.
            if (Path.GetFileName(archivo).EndsWith(".local.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return archivo;
        }
    }
}
