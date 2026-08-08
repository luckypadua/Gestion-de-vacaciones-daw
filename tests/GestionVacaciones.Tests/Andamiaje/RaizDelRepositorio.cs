namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// Localiza la raíz del repositorio a partir del directorio de salida de los tests. Lo necesitan los
/// tests que auditan archivos versionados (configuración, marcado, estilos), que no son artefactos
/// compilados y por tanto no se alcanzan desde el ensamblado.
/// </summary>
internal static class RaizDelRepositorio
{
    public static string Localizar() => Localizar(AppContext.BaseDirectory);

    /// <summary>Sube desde <paramref name="directorioDePartida"/> hasta el primer ancestro marcado como raíz.</summary>
    public static string Localizar(string directorioDePartida)
    {
        var directorio = new DirectoryInfo(directorioDePartida);
        while (directorio is not null && !EsLaRaiz(directorio))
        {
            directorio = directorio.Parent;
        }

        return directorio?.FullName
            ?? throw new InvalidOperationException(
                $"No se encontró la raíz del repositorio partiendo de «{directorioDePartida}».");
    }

    /// <summary>
    /// Se aceptan las dos formas que toma la marca <c>.git</c>: un <b>directorio</b> en un clon
    /// normal, y un <b>archivo</b> con la línea <c>gitdir: …</c> en un <c>git worktree</c> enlazado.
    /// Mirando solo el directorio, en un worktree el bucle sube más allá de la raíz hasta agotar el
    /// árbol y lanza, tumbando todos los tests que auditan archivos versionados.
    /// </summary>
    private static bool EsLaRaiz(DirectoryInfo directorio)
    {
        var marca = Path.Combine(directorio.FullName, ".git");
        return Directory.Exists(marca) || File.Exists(marca);
    }
}
