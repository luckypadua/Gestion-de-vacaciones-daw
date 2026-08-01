using Xunit;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// La localización de la raíz sostiene a los tests que auditan archivos versionados (B1-T6 y el
/// contenedor de error de circuito). En un <c>git worktree</c> enlazado la marca <c>.git</c> no es un
/// directorio sino un <b>archivo</b> con la línea <c>gitdir: …</c>: buscando solo el directorio, el
/// bucle sube más allá de la raíz, agota el árbol y lanza, tumbando esos tests sin que nadie haya
/// tocado su código. <c>.daw/orchestrator.md</c> recomienda worktree para trabajar tickets en
/// paralelo, así que el caso es real y no una hipótesis.
/// </summary>
public sealed class RaizDelRepositorioTests
{
    [Fact]
    public void Localizar_reconoce_la_raiz_cuando_git_es_un_directorio()
    {
        // El clon normal. Está acá para que el test del worktree no pueda pasar «por accidente» con
        // una implementación que solo mire archivos.
        using var arbol = new ArbolTemporal();
        Directory.CreateDirectory(Path.Combine(arbol.Raiz, ".git"));
        var partida = arbol.CrearSubdirectorio("tests", "GestionVacaciones.Tests", "bin", "Debug", "net10.0");

        Assert.Equal(arbol.Raiz, RaizDelRepositorio.Localizar(partida));
    }

    [Fact]
    public void Localizar_reconoce_la_raiz_cuando_git_es_un_archivo_de_worktree_enlazado()
    {
        using var arbol = new ArbolTemporal();
        File.WriteAllText(
            Path.Combine(arbol.Raiz, ".git"),
            "gitdir: /ruta/al/repositorio/.git/worktrees/ejemplo" + Environment.NewLine);
        var partida = arbol.CrearSubdirectorio("tests", "GestionVacaciones.Tests", "bin", "Debug", "net10.0");

        Assert.Equal(arbol.Raiz, RaizDelRepositorio.Localizar(partida));
    }

    /// <summary>
    /// Árbol de directorios desechable bajo el temporal del sistema. No se toca el repositorio real:
    /// un test que crea o borra un <c>.git</c> dentro del árbol de trabajo puede dejarlo inservible.
    /// </summary>
    private sealed class ArbolTemporal : IDisposable
    {
        public ArbolTemporal()
        {
            Raiz = Path.Combine(Path.GetTempPath(), $"gv-raiz-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Raiz);
        }

        public string Raiz { get; }

        public string CrearSubdirectorio(params string[] tramos) =>
            Directory.CreateDirectory(Path.Combine([Raiz, .. tramos])).FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Raiz, recursive: true);
            }
            catch (IOException)
            {
                // Un temporal que no se pudo borrar no invalida lo que el test afirmó; el sistema lo
                // recicla. Tragarlo acá evita convertir una limpieza en un fallo espurio.
            }
        }
    }
}
