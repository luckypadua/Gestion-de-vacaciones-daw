using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Hermano de <see cref="MensajesLiteralesDelPrdTests"/> para FEAT-001b: los dos mensajes del tope
/// coinciden con los literales entrecomillados de <c>prd-FEAT-001b.md</c>, y el texto compuesto no
/// lleva nada que el formato no diga.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué hace falta un hermano y no bastaba con extender el que existe.</b> Aquel apunta a
/// <c>prd-FEAT-001a.md</c> y afirma que sus literales son exactamente tres. Los de este ticket están en
/// otro documento: sin esta clase, los dos mensajes nuevos serían las únicas cadenas del proyecto que
/// nadie compara contra el PRD que las exige.
/// </para>
/// <para>
/// <b>Y por qué no se puede comparar tal cual.</b> El PRD escribe los marcadores como <c>X</c>,
/// <c>{año1}</c>, <c>Y</c> y <c>{año2}</c>, mientras el código usa los índices de
/// <see cref="string.Format(IFormatProvider, string, object?[])"/>. Comparar las cadenas crudas —que es
/// lo que hace el test de FEAT-001a— daría siempre falso. Se normaliza el formato a la grafía del
/// documento, en un único lugar, y esa correspondencia queda escrita en vez de vivir en la cabeza de
/// quien la dedujo.
/// </para>
/// </remarks>
public sealed partial class MensajesLiteralesDeFeat001bTests
{
    private const string RutaDelPrd = "docs/daw/prd/prd-FEAT-001b.md";
    private const string EncabezadoDeLosCriterios = "## Acceptance Criteria";

    [Fact]
    public void Los_literales_coinciden_con_prd_FEAT_001b()
    {
        var criterios = CriteriosDeAceptacionDelPrd();

        // Se busca el literal ENTRECOMILLADO y no la subcadena: una constante que agregara un punto
        // final no encontraría su par en el documento.
        Assert.Contains(
            $"\"{ComoLoEscribeElPrd(ErroresDeSolicitud.SaldoInsuficiente)}\"",
            criterios,
            StringComparison.Ordinal);

        Assert.Contains(
            $"\"{ComoLoEscribeElPrd(ErroresDeSolicitud.SaldoInsuficienteDosAnios)}\"",
            criterios,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Los_dos_mensajes_comparten_su_primera_mitad_palabra_por_palabra()
    {
        // AC-03 no es un mensaje distinto de AC-02: es el mismo, desglosado por año. Que coincidan hasta
        // «de {0} días» fue una decisión deliberada del PRD —se emparejaron a propósito— y sin este test
        // la próxima persona que retoque uno de los dos no tiene forma de saberlo.
        const string Comun = "No dispones de días suficientes. Tu saldo actual es de {0} días";

        Assert.StartsWith(Comun, ErroresDeSolicitud.SaldoInsuficiente, StringComparison.Ordinal);
        Assert.StartsWith(Comun, ErroresDeSolicitud.SaldoInsuficienteDosAnios, StringComparison.Ordinal);
    }

    /// <summary>Saldos y años centinela. Ningún dígito se repite entre ellos, a propósito.</summary>
    private const int SaldoDelPrimerAnio = 7;
    private const int SaldoDelSegundoAnio = 3;
    private const int PrimerAnio = 2026;
    private const int SegundoAnio = 2027;

    /// <summary>
    /// Días <b>usados</b> de los saldos centinela: valores que el mensaje de AC-03 <b>no</b> tiene que
    /// mostrar. Si el compositor leyera la propiedad equivocada del <c>SaldoDelAnio</c>, los dígitos de la
    /// salida serían estos y el aserto lo dice con el número en la mano.
    /// </summary>
    private const int UsadosDelPrimerAnio = 8;
    private const int UsadosDelSegundoAnio = 9;

    [Fact]
    public void El_mensaje_compuesto_no_lleva_nada_que_el_formato_no_diga()
    {
        // Mitigación de R-14. El saldo de una persona identificable es PII, y el mensaje compuesto es el
        // que termina viajando. Lo que se afirma es que el compositor es una sustitución y nada más: no
        // agrega el nombre de nadie «para que se entienda mejor», y no deja marcadores sin sustituir —un
        // «{3}» suelto en pantalla sería el síntoma de un formato desalineado con sus argumentos.
        var unAnio = ErroresDeSolicitud.ComponerSaldoInsuficiente(SaldoDelPrimerAnio);
        var dosAnios = ErroresDeSolicitud.ComponerSaldoInsuficienteDeDosAnios(
            new SaldoDelAnio(PrimerAnio, UsadosDelPrimerAnio, SaldoDelPrimerAnio),
            new SaldoDelAnio(SegundoAnio, UsadosDelSegundoAnio, SaldoDelSegundoAnio));

        Assert.Equal("No dispones de días suficientes. Tu saldo actual es de 7 días", unAnio);
        Assert.Equal(
            "No dispones de días suficientes. Tu saldo actual es de 7 días en 2026 y de 3 días en 2027",
            dosAnios);

        Assert.DoesNotContain('{', unAnio);
        Assert.DoesNotContain('{', dosAnios);
    }

    [Fact]
    public void Los_mensajes_compuestos_pasan_por_el_criterio_del_guardarrail_de_diagnosticos()
    {
        // ESTE es el caso que cierra el agujero de R-14, y hay que decir exactamente cuál era. El
        // guardarraíl de diagnósticos sin PII —DiagnosticoSinPiiTests— lee los mensajes de
        // ErroresDeSolicitud por reflexión con el filtro que se reproduce abajo: solo ve los `const`, y
        // nunca invoca al compositor. Es decir, ve «…de {0} días» y lo aprueba, mientras el ÚNICO texto
        // que lleva el saldo real de una persona identificable —el compuesto— no pasaba por ningún
        // control. Que la composición esté centralizada no sirve de nada si nadie mira su salida.
        //
        // Lo que se afirma acá, con el mismo criterio de dígitos que aplica aquel: los únicos números del
        // mensaje compuesto son el saldo y los años. Cualquier otro dígito solo podría venir de un dato
        // que este mensaje no tiene por qué llevar —un legajo, un identificador, una fecha—.
        var literales = LiteralesLeidosPorElGuardarrail();

        // Sin esto, un ErroresDeSolicitud renombrado dejaría el aserto de pertenencia sin nada que
        // comprobar y el test pasaría en verde sin haber mirado nada.
        Assert.NotEmpty(literales);

        (string Formato, string Mensaje, string DigitosEsperados)[] compuestos =
        [
            (ErroresDeSolicitud.SaldoInsuficiente,
             ErroresDeSolicitud.ComponerSaldoInsuficiente(SaldoDelPrimerAnio),
             "7"),

            (ErroresDeSolicitud.SaldoInsuficienteDosAnios,
             ErroresDeSolicitud.ComponerSaldoInsuficienteDeDosAnios(
                 new SaldoDelAnio(PrimerAnio, UsadosDelPrimerAnio, SaldoDelPrimerAnio),
                 new SaldoDelAnio(SegundoAnio, UsadosDelSegundoAnio, SaldoDelSegundoAnio)),
             "7202632027"),
        ];

        foreach (var (formato, mensaje, digitosEsperados) in compuestos)
        {
            // El formato del que sale cada mensaje es una de las constantes que el guardarraíl SÍ ve: es
            // lo que ata las dos mitades. Un compositor que armara el texto con una cadena escrita en su
            // propio cuerpo dejaría al guardarraíl mirando una constante que ya no se usa.
            Assert.Contains(formato, literales);

            // Los únicos dígitos son el saldo y los años. Los días USADOS de los centinelas —8 y 9— no
            // aparecen: el mensaje de AC-03 habla de lo que queda, no de lo que se tomó.
            Assert.Equal(digitosEsperados, new string([.. mensaje.Where(char.IsDigit)]));
        }
    }

    /// <summary>
    /// Las constantes de <see cref="ErroresDeSolicitud"/>, leídas con <b>el mismo filtro</b> que
    /// <c>DiagnosticoSinPiiTests.LiteralesDeRechazo()</c>: campos públicos estáticos con
    /// <c>IsLiteral</c> y sin <c>IsInitOnly</c>.
    /// </summary>
    /// <remarks>
    /// Se duplica el filtro a propósito en vez de compartirlo: lo que este test necesita afirmar es qué
    /// ve <i>aquel</i> guardarraíl, así que si allá cambia el criterio, acá tiene que quedar en evidencia
    /// que los mensajes compuestos ya no están cubiertos por lo mismo.
    /// </remarks>
    private static IReadOnlyList<string> LiteralesLeidosPorElGuardarrail() =>
        [.. typeof(ErroresDeSolicitud)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(campo => campo is { IsLiteral: true, IsInitOnly: false })
            .Select(campo => campo.GetRawConstantValue())
            .OfType<string>()];

    /// <summary>
    /// Cultura cuyo signo negativo <b>no</b> es el guion ASCII, sino el signo menos tipográfico U+2212.
    /// <c>lt-LT</c> es la otra.
    /// </summary>
    private const string CulturaConSignoMenosTipografico = "sv-SE";

    [Fact]
    public void El_compositor_escribe_el_signo_menos_ASCII_en_cualquier_cultura()
    {
        // OJO AL FORMULARLO, porque la versión anterior de este caso NO PODÍA FALLAR. Decía que sin
        // InvariantCulture un año podría salir «2.026» por el separador de millares, y eso es falso:
        // string.Format con «{0}» sobre un int usa el formato «G», que no inserta separador de grupo en
        // NINGUNA cultura. Se comprobó contra ocho (es-AR, de-DE, en-IN, ar-SA, fr-FR, hi-IN, fa-IR e
        // invariante): salida idéntica. Un test que afirma lo que ya es cierto por construcción no fija
        // nada, y encima explicaba un mecanismo inexistente.
        //
        // InvariantCulture sigue siendo la elección correcta, pero por otro motivo: lo único
        // culturalmente sensible de un entero es el SIGNO NEGATIVO. sv-SE y lt-LT usan el signo menos
        // U+2212 en vez del guion ASCII, así que con la cultura de la máquina un saldo negativo saldría
        // con un carácter distinto —invisible en una revisión y distinto byte a byte en un log o en una
        // comparación—. Hoy el saldo está acotado a 0 y no puede ser negativo; lo que este caso fija es
        // que el compositor no dependa de eso.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(CulturaConSignoMenosTipografico);

        try
        {
            // Sin esta línea el test sería vacío en una máquina sin datos de ICU —o en modo de
            // globalización invariante—, donde la cultura pedida es la invariante disfrazada y el guion
            // ASCII sale por defecto.
            Assert.Equal("−", CultureInfo.CurrentCulture.NumberFormat.NegativeSign);

            var mensaje = ErroresDeSolicitud.ComponerSaldoInsuficiente(-3);

            Assert.Contains("de -3 días", mensaje, StringComparison.Ordinal);
            Assert.DoesNotContain('−', mensaje);
        }
        finally
        {
            // La cultura es estado del hilo, que es compartido: restaurarla no es prolijidad, es lo que
            // impide que este caso le cambie el formato a otro que corra después en el mismo hilo.
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// El formato del código, escrito como lo escribe el PRD: <c>X</c> e <c>Y</c> para los días,
    /// <c>{año1}</c> y <c>{año2}</c> para los años.
    /// </summary>
    private static string ComoLoEscribeElPrd(string formato) => formato
        .Replace("{0}", "X", StringComparison.Ordinal)
        .Replace("{1}", "{año1}", StringComparison.Ordinal)
        .Replace("{2}", "Y", StringComparison.Ordinal)
        .Replace("{3}", "{año2}", StringComparison.Ordinal);

    /// <summary>
    /// La sección de criterios del PRD <b>versionado</b>, con los espacios normalizados: el documento
    /// parte los literales largos en dos líneas para respetar el ancho de columna, y esa partición es
    /// tipográfica, no parte del mensaje.
    /// </summary>
    private static string CriteriosDeAceptacionDelPrd()
    {
        var prd = File.ReadAllText(Path.Combine(RaizDelRepositorio.Localizar(), RutaDelPrd));

        var desde = prd.IndexOf(EncabezadoDeLosCriterios, StringComparison.Ordinal);
        Assert.True(desde >= 0, $"El PRD «{RutaDelPrd}» no tiene la sección «{EncabezadoDeLosCriterios}».");

        var siguienteSeccion = prd.IndexOf("\n## ", desde + EncabezadoDeLosCriterios.Length, StringComparison.Ordinal);

        var seccion = siguienteSeccion < 0 ? prd[desde..] : prd[desde..siguienteSeccion];

        return EspaciosSeguidos().Replace(seccion, " ");
    }

    [GeneratedRegex(@"\s+", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex EspaciosSeguidos();
}
