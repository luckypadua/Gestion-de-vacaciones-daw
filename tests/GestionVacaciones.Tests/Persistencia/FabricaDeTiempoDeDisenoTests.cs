using GestionVacaciones.Data;
using GestionVacaciones.Web.Configuracion;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// <see cref="VacacionesDbContextFactory"/>: la fábrica de tiempo de diseño que usa <c>dotnet ef</c>.
/// </summary>
public sealed class FabricaDeTiempoDeDisenoTests
{
    [Fact]
    public void La_fabrica_de_migraciones_lee_la_misma_variable_de_entorno_que_la_aplicacion()
    {
        // El literal «VACACIONES_CONNECTION» está escrito en dos ensamblados —la fábrica, en Data, y
        // CadenaDeConexion, en Web— y nada los ata: Data no puede referenciar a Web, así que la
        // duplicación es estructural y no se puede eliminar sin mover código de bloque. Lo que sí se
        // puede es impedir que se desincronicen en silencio: renombrar una sola de las dos dejaría
        // «dotnet ef database update» leyendo una variable que nadie define y aplicando migraciones
        // contra la base que EF adivine, sin que ningún otro test se entere.
        Assert.Equal(CadenaDeConexion.VariableDeEntorno, VacacionesDbContextFactory.VariableDeEntorno);
    }
}
