using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Hermano de <see cref="MensajesLiteralesDeFeat001cTests"/> para FEAT-002 (Bloque 3): los dos
/// literales de <see cref="SolicitudesService.ResolverAsync"/> coinciden, carácter por carácter, con el
/// entrecomillado de AC-03 y AC-04 en <c>prd-FEAT-002.md</c>.
/// </summary>
/// <remarks>
/// Mismo motivo que sus hermanos: comparar la constante contra un literal escrito en el propio test
/// verificaría que el código y el test coinciden, no que coinciden con el <b>documento</b>. Una tilde
/// perdida o un punto final agregado se ven idénticos en una revisión y romperían AC-03/AC-04, que son
/// criterios sobre el texto exacto que ve quien resuelve.
/// </remarks>
public sealed class MensajesLiteralesDeFeat002Tests
{
    private const string RutaDelPrd = "docs/daw/prd/prd-FEAT-002.md";
    private const string EncabezadoDeLosCriterios = "## Acceptance Criteria";

    [Fact]
    public void El_literal_del_motivo_obligatorio_coincide_con_prd_FEAT_002()
    {
        var criterios = CriteriosDeAceptacionDelPrd();

        // Se busca el literal ENTRECOMILLADO y no la subcadena: una constante que agregara un punto
        // final no encontraría su par en el documento.
        Assert.Contains(
            $"\"{ErroresDeSolicitud.MotivoDeRechazoObligatorio}\"",
            criterios,
            StringComparison.Ordinal);
    }

    [Fact]
    public void El_literal_de_solicitud_ya_resuelta_coincide_con_prd_FEAT_002()
    {
        var criterios = CriteriosDeAceptacionDelPrd();

        Assert.Contains(
            $"\"{ErroresDeSolicitud.SolicitudYaResuelta}\"",
            criterios,
            StringComparison.Ordinal);
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

        return siguienteSeccion < 0 ? prd[desde..] : prd[desde..siguienteSeccion];
    }
}
