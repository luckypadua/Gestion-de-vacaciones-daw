using System.Globalization;
using System.Text.RegularExpressions;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// NFR-01 y NFR-04: el tope vive en una única declaración, y las reglas de cálculo en una única
/// implementación.
/// </summary>
/// <remarks>
/// <para>
/// Son escaneos de fuente, en la misma línea que <see cref="FuenteDeTiempoTests"/> y los tests de
/// composición del acceso a datos. Un requisito de la forma «esto existe una sola vez» no se puede
/// verificar ejercitando el código: dos copias que hoy coinciden pasan cualquier test de
/// comportamiento, y el día que dejen de coincidir el síntoma es un empleado al que la pantalla le
/// promete un saldo y el servidor le niega otro.
/// </para>
/// <para>
/// Se escanea <c>src/</c>, nunca <c>tests/</c>: los tests <b>deben</b> escribir números concretos —es
/// su trabajo afirmar que 5 aprobados dejan 9 disponibles— y prohibírselos convertiría el guardarraíl
/// en un estorbo que alguien terminaría desactivando.
/// </para>
/// </remarks>
public sealed class PuntoUnicoDelTopeTests
{
    private const string ArchivoDelTope = "TopeAnual.cs";
    private const string ArchivoDelCalculador = "CalculadorDeDiasCorridos.cs";
    private const string ArchivoDelSaldo = "SaldoService.cs";
    private const string ArchivoDeLaImputacion = "ImputacionPorAnio.cs";

    [Fact]
    public void El_numero_del_tope_solo_aparece_en_TopeAnual()
    {
        // NFR-01: cambiar el tope tiene que requerir modificar exactamente una declaración. Un 14 en el
        // servicio que bloquea y otro en el que calcula el saldo son dos números que hoy coinciden y que
        // van a dejar de coincidir el día que cambie la normativa — en el peor momento posible.
        //
        // El número que se busca sale de TopeAnual.Dias y NO está escrito acá. La versión anterior
        // codificaba el 14: el día que el tope pase a 20, un guardarraíl que siga buscando 14 no
        // encuentra nada, se pone verde y deja de proteger sin que nada avise. Un guardarraíl que puede
        // dejar de cubrir en silencio es peor que ninguno, porque además da confianza.
        var infracciones = EnCadaLineaDeCodigoDeSrc((relativa, linea) =>
            !relativa.EndsWith(ArchivoDelTope, StringComparison.Ordinal)
            && _topeSuelto.IsMatch(linea));

        Assert.True(
            infracciones.Count == 0,
            $"El tope anual ({TopeAnual.Dias}) aparece fuera de TopeAnual.Dias: " +
            $"{string.Join(" · ", infracciones)}. " +
            "NFR-01 exige que cambiar el tope requiera modificar exactamente una declaración.");
    }

    [Fact]
    public void El_conteo_de_dias_no_se_reimplementa_fuera_del_calculador()
    {
        // NFR-04, en su forma más concreta. La aritmética del conteo inclusivo es
        // «fin.DayNumber - inicio.DayNumber + 1», y ese «+ 1» es exactamente el detalle que diverge
        // entre dos copias sin que nadie lo note. ImputacionPorAnio recorta el período y DELEGA el
        // conteo: si algún día restara por su cuenta, este test se pone en rojo.
        var infracciones = EnCadaLineaDeCodigoDeSrc((relativa, linea) =>
            !relativa.EndsWith(ArchivoDelCalculador, StringComparison.Ordinal)
            && linea.Contains("DayNumber", StringComparison.Ordinal));

        Assert.True(
            infracciones.Count == 0,
            $"Hay aritmética de días fuera de CalculadorDeDiasCorridos: {string.Join(" · ", infracciones)}. " +
            "El conteo de días corridos tiene una única implementación (NFR-04).");
    }

    [Fact]
    public void Solo_SaldoService_construye_un_SaldoDelAnio()
    {
        // La otra mitad de NFR-04: el saldo que se muestra y el saldo que bloquea tienen que venir de la
        // misma función. Con un único productor del tipo, un segundo camino de cálculo no puede existir
        // sin que este test lo diga — que es distinto de exigir un único CONSUMIDOR: que la pantalla, el
        // alta y quien venga después lo consulten es justamente lo que se busca.
        var infracciones = EnCadaLineaDeCodigoDeSrc((relativa, linea) =>
            !relativa.EndsWith(ArchivoDelSaldo, StringComparison.Ordinal)
            && linea.Contains("new SaldoDelAnio(", StringComparison.Ordinal));

        Assert.True(
            infracciones.Count == 0,
            $"Hay un segundo productor de SaldoDelAnio: {string.Join(" · ", infracciones)}. " +
            "El saldo mostrado y el saldo que bloquea salen de la misma función (NFR-04).");
    }

    /// <summary>
    /// NFR-04 sobre la <b>función de cálculo</b>: la única llamada a
    /// <c>ImputacionPorAnio.DiasEnElAnio</c> en <c>src/</c> está en <c>SaldoService</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué se vigila <c>DiasEnElAnio</c> y no el tipo entero.</b> De los dos miembros de
    /// <c>ImputacionPorAnio</c>, el que NFR-04 cuenta «en una sola» es este: es el que <i>calcula</i> días
    /// imputados, y un segundo archivo que lo llamara y armara su propio saldo sería exactamente el
    /// segundo camino de cálculo que el PRD registra como primer riesgo —la pantalla prometiendo un número
    /// y el servidor bloqueando contra otro—. Hoy nada lo impide, y de ahí este caso.
    /// </para>
    /// <para>
    /// <b><c>AniosAbarcados</c> queda deliberadamente afuera, y no es una concesión.</b> No calcula días:
    /// enumera los años que toca un período, que es aritmética de calendario sin ninguna regla de negocio
    /// adentro. Puede tener todos los llamadores que haga falta sin que exista una segunda imputación — y
    /// de hecho el bloque 4 la llama desde <c>SaldoDelEmpleado.razor</c> para saber si el período cruza el
    /// año. Un escaneo sobre el tipo completo se pondría rojo en ese bloque por diseño, y un guardarraíl
    /// que hay que aflojar en el bloque siguiente no protege nada.
    /// </para>
    /// <para>
    /// Se excluye también el archivo que la <b>declara</b>: su firma contiene el nombre y no es una
    /// llamada. El precio es que una llamada interna dentro de <c>ImputacionPorAnio</c> no se vería, y es
    /// aceptable — ahí es donde la función vive.
    /// </para>
    /// </remarks>
    [Fact]
    public void Solo_SaldoService_consume_DiasEnElAnio()
    {
        var infracciones = EnCadaLineaDeCodigoDeSrc((relativa, linea) =>
            !relativa.EndsWith(ArchivoDelSaldo, StringComparison.Ordinal)
            && !relativa.EndsWith(ArchivoDeLaImputacion, StringComparison.Ordinal)
            && linea.Contains($"{nameof(ImputacionPorAnio.DiasEnElAnio)}(", StringComparison.Ordinal));

        Assert.True(
            infracciones.Count == 0,
            $"Hay un segundo consumidor de DiasEnElAnio: {string.Join(" · ", infracciones)}. " +
            "La imputación de días a un año se consume desde SaldoService y desde ningún otro lado " +
            "(NFR-04): el saldo que se muestra y el que bloquea salen de la misma función.");
    }

    /// <summary>
    /// Recorre las líneas <b>de código</b> de <c>src/</c> —sin comentarios— y devuelve las que cumplen
    /// el predicado, con su ubicación.
    /// </summary>
    /// <remarks>
    /// Los comentarios se descartan con el criterio de <see cref="FuenteSinComentarios"/>, donde está
    /// escrito el por qué y también su hueco conocido. Es <b>indispensable</b> descartarlos acá: el propio
    /// <c>TopeAnual</c> explica de dónde sale el número, <c>SolicitudesService</c> cita «14 días» al
    /// declarar el tope fuera de su alcance y <c>ImputacionPorAnio</c> lo cita al explicar por qué un
    /// período de tres años no cabe. Un escaneo que los contara obligaría a escribir la documentación en
    /// clave para no romper el test.
    /// </remarks>
    private static List<string> EnCadaLineaDeCodigoDeSrc(Func<string, string, bool> esInfraccion)
    {
        var raiz = RaizDelRepositorio.Localizar();
        var src = Path.Combine(raiz, "src");

        var archivos = Directory
            .EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(ruta => ruta.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || ruta.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(ruta => !ruta.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !ruta.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var infracciones = new List<string>();

        foreach (var ruta in archivos)
        {
            var relativa = Path.GetRelativePath(raiz, ruta);
            var lineas = File.ReadAllLines(ruta);

            for (var numero = 0; numero < lineas.Length; numero++)
            {
                var codigo = FuenteSinComentarios.DeUnaLinea(lineas[numero]);

                if (codigo.Length > 0 && esInfraccion(relativa, codigo))
                {
                    infracciones.Add($"{relativa}:{numero + 1}");
                }
            }
        }

        return infracciones;
    }

    /// <summary>
    /// El tope como número suelto: no como parte de 2014, de 140 ni de una ruta.
    /// </summary>
    /// <remarks>
    /// Se construye en tiempo de ejecución y no con <c>[GeneratedRegex]</c> porque el patrón depende de
    /// <see cref="TopeAnual.Dias"/>, que es de lo que se trata: el número que se busca lo dicta la única
    /// declaración del tope, no una copia escrita en el test.
    /// </remarks>
    private static readonly Regex _topeSuelto = new(
        $@"(?<![\w.]){TopeAnual.Dias.ToString(CultureInfo.InvariantCulture)}(?![\w.])",
        RegexOptions.None,
        TimeSpan.FromMilliseconds(2000));
}
