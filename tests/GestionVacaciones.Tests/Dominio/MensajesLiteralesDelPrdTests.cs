using System.Text.RegularExpressions;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Guardarraíl del criterio de cierre del bloque: los mensajes de <see cref="ErroresDeSolicitud"/>
/// coinciden <b>carácter por carácter</b> con los literales entrecomillados del PRD.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué se lee el PRD y no se repite el texto acá.</b> Un test que compare la constante contra un
/// literal escrito en el propio test verifica que el código y el test coinciden, que es lo que ya
/// hacen B5-T3 y B5-T4. Lo que el criterio de cierre exige es otra cosa: que coincidan con el
/// <b>documento</b>. Una tilde perdida, un punto final agregado o un «hoy» capitalizado se ven idénticos
/// en una revisión y rompen AC-02 y AC-03, que son criterios sobre el texto exacto que ve el empleado.
/// </para>
/// <para>
/// <b>Lo que este test resuelve del desajuste entre la spec y el PRD.</b> El criterio de cierre del
/// Bloque 5 habla de «los tres mensajes literales», mientras su propia tabla de errores lista
/// <b>dos</b>. El tercer literal entrecomillado del PRD es <c>"Pendiente"</c>, que no es un mensaje sino
/// el nombre de un estado —el que ya fija <see cref="EstadoSolicitud"/>—. Este test cuenta los tres,
/// separa los dos mensajes del nombre del estado, y deja el desajuste documentado en código en vez de
/// resuelto inventando un tercer mensaje que el PRD no pide.
/// </para>
/// </remarks>
public sealed partial class MensajesLiteralesDelPrdTests
{
    private const string RutaDelPrd = "docs/daw/prd/prd-FEAT-001a.md";
    private const string EncabezadoDeLosCriterios = "## Acceptance Criteria";

    [Fact]
    public void Los_dos_mensajes_de_validacion_estan_en_el_PRD_caracter_por_caracter()
    {
        var criterios = CriteriosDeAceptacionDelPrd();

        // Se busca el literal ENTRECOMILLADO, no la subcadena: así una constante que agregara un punto
        // final —«…anterior a hoy.»— no encontraría su par en el documento.
        Assert.Contains($"\"{ErroresDeSolicitud.FechaDeInicioAnteriorAHoy}\"", criterios, StringComparison.Ordinal);
        Assert.Contains($"\"{ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio}\"", criterios, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_literales_del_PRD_son_exactamente_dos_mensajes_y_el_nombre_de_un_estado()
    {
        // La dirección que falta en el test de arriba: aquel comprueba que cada constante está en el PRD,
        // este que el PRD no pide ningún mensaje que el código no tenga. Sin él, agregar un cuarto
        // criterio con un mensaje nuevo no rompería nada y la validación quedaría a medias.
        var literales = LiteralesEntrecomillados()
            .Matches(CriteriosDeAceptacionDelPrd())
            .Select(coincidencia => coincidencia.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        string[] esperados =
        [
            ErroresDeSolicitud.FechaDeInicioAnteriorAHoy,
            ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio,

            // El tercero NO es un mensaje: es el nombre del estado en el que nace toda solicitud (AC-04),
            // y quien lo fija es el enum del Bloque 2. Se lo nombra desde ahí, no como texto suelto.
            nameof(EstadoSolicitud.Pendiente),
        ];

        Assert.Equal(
            esperados.Order(StringComparer.Ordinal),
            literales.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Los_dos_mensajes_son_distintos_y_ninguno_esta_vacio()
    {
        // Parece obvio y no lo es: dos constantes vacías, o las dos con el mismo texto, dejarían en verde
        // los dos tests de arriba en cuanto el PRD cambiara, y el empleado vería el mensaje equivocado
        // sin que nada se rompiera.
        Assert.NotEmpty(ErroresDeSolicitud.FechaDeInicioAnteriorAHoy);
        Assert.NotEmpty(ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio);
        Assert.NotEqual(
            ErroresDeSolicitud.FechaDeInicioAnteriorAHoy,
            ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio);
    }

    /// <summary>
    /// La sección de criterios de aceptación del PRD <b>versionado</b>, no una copia del directorio de
    /// salida: una copia no es evidencia de lo que se commiteó.
    /// </summary>
    private static string CriteriosDeAceptacionDelPrd()
    {
        var prd = File.ReadAllText(Path.Combine(RaizDelRepositorio.Localizar(), RutaDelPrd));

        var desde = prd.IndexOf(EncabezadoDeLosCriterios, StringComparison.Ordinal);
        Assert.True(desde >= 0, $"El PRD «{RutaDelPrd}» no tiene la sección «{EncabezadoDeLosCriterios}».");

        var siguienteSeccion = prd.IndexOf("\n## ", desde + EncabezadoDeLosCriterios.Length, StringComparison.Ordinal);

        return siguienteSeccion < 0
            ? prd[desde..]
            : prd[desde..siguienteSeccion];
    }

    /// <summary>Literales entre comillas dobles rectas, que es como el PRD escribe los mensajes.</summary>
    [GeneratedRegex("\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex LiteralesEntrecomillados();
}
