using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionVacaciones.Data;

/// <summary>Qué hizo la semilla. Es lo que permite afirmar sobre ella sin leer el log.</summary>
public enum ResultadoDeSemilla
{
    /// <summary>La nómina no estaba y quedó escrita.</summary>
    Sembrada,

    /// <summary>Ya había empleados: la semilla no tocó nada (es idempotente).</summary>
    YaHabiaNomina,

    /// <summary>
    /// La conexión apuntaba a un catálogo que no está declarado sembrable: no se escribió ni se leyó
    /// nada (mitigación R-03).
    /// </summary>
    AbortadaPorCatalogo,
}

/// <summary>
/// Conjunto <b>cerrado</b> de catálogos sobre los que se permite sembrar la nómina de desarrollo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué el conjunto es un tipo y no una constante incrustada en <see cref="SeedDatos"/>.</b> La
/// mitigación de R-03 es «antes de escribir, comprobar que el catálogo sea el de la aplicación». Con
/// el nombre incrustado, esa comprobación no se podía ejercitar: los tests de integración corren
/// sobre <c>GestionVacacionesV2_Test</c>, donde la semilla habría abortado siempre, así que el camino
/// feliz era inverificable y el guardarraíl —el sad path— tampoco tenía cómo demostrarse. Las salidas
/// fáciles son peores que el problema: aceptar cualquier catálogo, o saltear la comprobación al
/// detectar que se corre dentro de un test, convierten la mitigación en decoración.
/// </para>
/// <para>
/// Se hace entonces explícito e inyectable, pero <b>cerrado</b>, que es lo que impide que el punto de
/// inyección sea la puerta trasera del guardarraíl:
/// </para>
/// <list type="number">
///   <item><description>solo se construye por <see cref="Declarar"/>, que rechaza el conjunto vacío,
///   los nombres en blanco —emparejarían con una conexión sin catálogo— y el catálogo de la v1, que
///   <c>AGENTS.md</c> marca como intocable;</description></item>
///   <item><description>no existe ningún valor que signifique «cualquiera»;</description></item>
///   <item><description>la composición productiva declara <see cref="DeLaAplicacion"/>, y es el test
///   —no la semilla— el que declara su base descartable;</description></item>
///   <item><description>la comparación es por igualdad exacta, sin distinguir mayúsculas: una base
///   que solo comparte el prefijo, como <c>GestionVacacionesV2_Test</c>, no entra.</description></item>
/// </list>
/// <para>
/// El resultado es un guardarraíl que sigue siendo estructural en producción y que además queda
/// verificado: B3-T3 lo ejercita con <see cref="DeLaAplicacion"/> contra una base real, viva y vacía,
/// y comprueba que no escribe ni una fila.
/// </para>
/// </remarks>
public sealed class CatalogosSembrables
{
    /// <summary>Base de la v2, la única de la aplicación.</summary>
    public const string CatalogoDeLaVersion2 = "GestionVacacionesV2";

    /// <summary>
    /// Base de la v1. Tiene su propio historial de migraciones y su propia data: <c>AGENTS.md</c>
    /// registra el cruce v1/v2 como cicatriz del proyecto y prohíbe mezclarlas.
    /// </summary>
    public const string CatalogoDeLaVersion1 = "GestionVacaciones";

    private readonly string[] _nombres;

    private CatalogosSembrables(string[] nombres) => _nombres = nombres;

    /// <summary>El conjunto de la aplicación: exactamente la base de la v2, y nada más.</summary>
    public static CatalogosSembrables DeLaAplicacion { get; } = Declarar(CatalogoDeLaVersion2);

    /// <summary>Los catálogos declarados, en el orden en que se declararon.</summary>
    public IReadOnlyList<string> Nombres => _nombres;

    /// <summary>Declara el conjunto de catálogos sembrables.</summary>
    /// <exception cref="ArgumentException">
    /// Si no se declara ninguno, si alguno está en blanco o si alguno es la base v1.
    /// </exception>
    public static CatalogosSembrables Declarar(params string[] nombres)
    {
        ArgumentNullException.ThrowIfNull(nombres);

        if (nombres.Length == 0)
        {
            // No es peligroso —no admitiría nada— pero sí es un error de composición mudo: la semilla
            // no escribiría nunca y el motivo no aparecería en ningún lado.
            throw new ArgumentException(
                "No se declaró ningún catálogo sembrable. La semilla no escribiría nunca y el motivo " +
                "no sería evidente: declará al menos uno.",
                nameof(nombres));
        }

        foreach (var nombre in nombres)
        {
            if (string.IsNullOrWhiteSpace(nombre))
            {
                // Un nombre vacío emparejaría con una conexión que no indica catálogo, que es
                // justamente el caso en el que no se sabe adónde se está escribiendo.
                throw new ArgumentException(
                    "Hay un catálogo declarado en blanco. Emparejaría con una conexión sin «Initial " +
                    "Catalog», que es el caso en el que no se sabe adónde se escribe.",
                    nameof(nombres));
            }

            if (string.Equals(nombre, CatalogoDeLaVersion1, StringComparison.OrdinalIgnoreCase))
            {
                // El punto de inyección no puede ser la puerta trasera del guardarraíl: ni un test ni
                // un host futuro pueden declarar sembrable la base v1.
                throw new ArgumentException(
                    $"«{CatalogoDeLaVersion1}» es la base de la v1 y AGENTS.md la marca como " +
                    "intocable: sembrar cuatro empleados ficticios ahí crea identidades que el " +
                    "selector aceptaría. No se puede declarar sembrable.",
                    nameof(nombres));
            }
        }

        return new CatalogosSembrables([.. nombres]);
    }

    /// <summary>
    /// ¿Se puede sembrar sobre <paramref name="catalogo"/>? Igualdad exacta —sin distinguir
    /// mayúsculas, como hace SQL Server con el nombre de la base—, nunca por prefijo: un
    /// <c>StartsWith</c> dejaría entrar cualquier base clonada con el nombre de la real por delante.
    /// </summary>
    public bool Admite(string? catalogo) =>
        !string.IsNullOrWhiteSpace(catalogo) &&
        _nombres.Contains(catalogo, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Los nombres declarados, para el mensaje de diagnóstico del aborto. Son nombres de bases, no
    /// credenciales: la cadena de conexión nunca entra acá.
    /// </summary>
    public override string ToString() => string.Join(", ", _nombres);
}

/// <summary>
/// Semilla de la nómina de desarrollo: <b>Ana</b> (manager), <b>Diego</b> (designado de Ana),
/// <b>Bruno</b> y <b>Carla</b>. Se invoca al arrancar y <b>solo</b> en <c>Development</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué es código y no <c>HasData</c>.</b> Las relaciones manager/designado son autorreferencias
/// circulares —Ana designa a Diego y Diego reporta a Ana— y <c>HasData</c> no puede ordenar esos
/// <c>INSERT</c>: necesita conocer las claves de antemano y aquí son <c>identity</c>. <c>AGENTS.md</c>
/// lo registra como cicatriz del proyecto.
/// </para>
/// <para>
/// <b>Mitigación R-03.</b> Antes de leer y antes de escribir, comprueba que el catálogo de la conexión
/// esté declarado sembrable (ver <see cref="CatalogosSembrables"/>). Si no lo está, aborta sin abrir
/// la conexión, registra el catálogo encontrado en nivel <c>Warning</c> y <b>la aplicación sigue
/// arrancando</b>: la nómina ficticia es comodidad de desarrollo, no un requisito para levantar.
/// </para>
/// <para>
/// <b>R-12.</b> El log nombra el catálogo y la cantidad de empleados. Nunca nombre ni correo, aunque
/// acá sean ficticios: el hábito es el que después se aplica sobre datos reales.
/// </para>
/// </remarks>
public sealed class SeedDatos
{
    /// <summary>La manager del equipo. Su designado es Diego.</summary>
    public const string NombreDeAna = "Ana Gómez";

    /// <summary>Correo ficticio. El dominio <c>.local</c> no resuelve en ninguna red.</summary>
    public const string CorreoDeAna = "ana.gomez@ejemplo.local";

    /// <summary>El designado de Ana: aprueba y rechaza en su lugar cuando ella no está.</summary>
    public const string NombreDeDiego = "Diego Ríos";

    public const string CorreoDeDiego = "diego.rios@ejemplo.local";

    public const string NombreDeBruno = "Bruno Paz";

    public const string CorreoDeBruno = "bruno.paz@ejemplo.local";

    public const string NombreDeCarla = "Carla Vega";

    public const string CorreoDeCarla = "carla.vega@ejemplo.local";

    /// <summary>
    /// Los correos de la nómina sembrada. Es la forma de localizarla sin depender de los <c>Id</c>,
    /// que son <c>identity</c> y cambian entre bases.
    /// </summary>
    public static readonly string[] CorreosDeLaNomina =
        [CorreoDeAna, CorreoDeDiego, CorreoDeBruno, CorreoDeCarla];

    private readonly IDbContextFactory<VacacionesDbContext> _fabrica;
    private readonly CatalogosSembrables _catalogosSembrables;
    private readonly ILogger<SeedDatos> _registro;

    /// <param name="fabrica">
    /// Fábrica de contextos. Siempre <see cref="IDbContextFactory{TContext}"/> y nunca un
    /// <c>DbContext</c> inyectado (NFR-05, <c>AGENTS.md</c>).
    /// </param>
    /// <param name="catalogosSembrables">
    /// Catálogos sobre los que se permite escribir. La composición del host declara
    /// <see cref="CatalogosSembrables.DeLaAplicacion"/>; no hay valor por defecto, para que ningún
    /// lugar del código herede el permiso sin nombrarlo.
    /// </param>
    /// <param name="registro">Registro de diagnóstico. Nunca recibe PII (R-12).</param>
    public SeedDatos(
        IDbContextFactory<VacacionesDbContext> fabrica,
        CatalogosSembrables catalogosSembrables,
        ILogger<SeedDatos> registro)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(catalogosSembrables);
        ArgumentNullException.ThrowIfNull(registro);

        _fabrica = fabrica;
        _catalogosSembrables = catalogosSembrables;
        _registro = registro;
    }

    /// <summary>
    /// Siembra la nómina si el catálogo lo permite y la tabla está vacía. Es idempotente: invocarla
    /// dos veces deja exactamente la misma nómina que la primera.
    /// </summary>
    /// <exception cref="DbUpdateException">
    /// Si falla la escritura. <b>Se propaga a propósito</b>: una nómina a medias deja el desarrollo en
    /// un estado que nadie puede diagnosticar, y un <c>catch</c> silencioso lo escondería.
    /// </exception>
    public async Task<ResultadoDeSemilla> SembrarAsync(CancellationToken cancelacion = default)
    {
        await using var contexto = await _fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        // El catálogo se le pregunta a la conexión —que todavía no está abierta— en vez de reinterpretar
        // la cadena: es el propio proveedor el que dice adónde apunta, y así no hay dos lecturas de la
        // misma cadena que puedan discrepar.
        var catalogo = contexto.Database.GetDbConnection().Database;

        if (!_catalogosSembrables.Admite(catalogo))
        {
            // Aborta ANTES de abrir la conexión, no después de fallar: para cuando el motor contesta,
            // la escritura sobre la base equivocada ya habría ocurrido (R-03).
            //
            // Se registra el catálogo encontrado, que es el único dato accionable para quien depura, y
            // los declarados, que son nombres de bases. La cadena de conexión no aparece: lleva
            // credenciales y esto termina en un log (R-02, F-SAST-10).
            _registro.LogWarning(
                "La semilla de desarrollo no escribió nada: la conexión apunta al catálogo «{Catalogo}» " +
                "y el único declarado sembrable es «{Sembrables}». La aplicación sigue arrancando.",
                catalogo,
                _catalogosSembrables);

            return ResultadoDeSemilla.AbortadaPorCatalogo;
        }

        if (await contexto.Empleados.AnyAsync(cancelacion).ConfigureAwait(false))
        {
            return ResultadoDeSemilla.YaHabiaNomina;
        }

        // Mitigación 2 de R-03: queda escrito sobre qué base se sembró y cuándo, ANTES de escribir. El
        // STRIDE de la semilla anota justamente «sin registro de qué base se sembró ni cuándo».
        _registro.LogInformation(
            "Sembrando la nómina de desarrollo ({Cantidad} empleados) sobre el catálogo «{Catalogo}».",
            CorreosDeLaNomina.Length,
            catalogo);

        await EscribirLaNominaAsync(contexto, cancelacion).ConfigureAwait(false);

        return ResultadoDeSemilla.Sembrada;
    }

    /// <summary>
    /// Escribe la nómina en dos viajes dentro de <b>una</b> transacción.
    /// </summary>
    /// <remarks>
    /// Dos viajes porque la relación es circular y las claves son <c>identity</c>: hasta que Ana y
    /// Diego no existen no hay <c>Id</c> con el que enlazarlos. Una transacción porque, sin ella, un
    /// fallo en el segundo viaje dejaría cuatro empleados sin manager ni designado —una nómina parcial,
    /// que son cuatro identidades que el selector aceptaría igual— y la segunda corrida no la
    /// arreglaría: encontraría la tabla con filas y no haría nada.
    /// </remarks>
    private static async Task EscribirLaNominaAsync(VacacionesDbContext contexto, CancellationToken cancelacion)
    {
        await using var transaccion = await contexto.Database
            .BeginTransactionAsync(cancelacion)
            .ConfigureAwait(false);

        var ana = new Empleado { Nombre = NombreDeAna, Correo = CorreoDeAna };
        var diego = new Empleado { Nombre = NombreDeDiego, Correo = CorreoDeDiego };
        var bruno = new Empleado { Nombre = NombreDeBruno, Correo = CorreoDeBruno };
        var carla = new Empleado { Nombre = NombreDeCarla, Correo = CorreoDeCarla };

        contexto.Empleados.AddRange(ana, diego, bruno, carla);
        await contexto.SaveChangesAsync(cancelacion).ConfigureAwait(false);

        // Segundo viaje: las relaciones, ya con los Id asignados. Ana no tiene manager —es la cabeza
        // de la nómina sembrada— y delega en Diego, que además le reporta: ese ciclo es el que obliga
        // a las FK ON DELETE NO ACTION del modelo.
        ana.DesignadoId = diego.Id;
        diego.ManagerId = ana.Id;
        bruno.ManagerId = ana.Id;
        carla.ManagerId = ana.Id;

        await contexto.SaveChangesAsync(cancelacion).ConfigureAwait(false);

        await transaccion.CommitAsync(cancelacion).ConfigureAwait(false);
    }
}
