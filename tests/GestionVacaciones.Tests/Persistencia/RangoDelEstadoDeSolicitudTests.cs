using GestionVacaciones.Data.Entidades;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// El rango del enum de estado y el que declara <c>CK_Solicitud_EstadoValido</c> tienen que moverse
/// juntos.
/// </summary>
/// <remarks>
/// Vive fuera de <see cref="EsquemaDeSolicitudTests"/> y fuera de
/// <see cref="ColeccionDeBaseDeDatos"/> a propósito: no toca la base ni necesita el fixture, así que
/// <b>no</b> lleva la categoría de integración y sigue corriendo en cualquier máquina, incluida una
/// sin motor. Dentro de aquella clase heredaría la categoría —los traits se aplican a nivel de clase—
/// y <c>--filter Category!=Integracion</c> lo dejaría fuera sin que nadie lo note.
/// </remarks>
public sealed class RangoDelEstadoDeSolicitudTests
{
    [Fact]
    public void El_rango_del_enum_de_estado_sigue_siendo_el_que_la_constraint_declara()
    {
        // CK_Solicitud_EstadoValido lleva escrito «BETWEEN 0 AND 2». Agregar un cuarto estado al enum
        // sin tocar la constraint dejaría filas legítimas imposibles de guardar, y el fallo aparecería
        // en producción, no acá. Este test es el que obliga a que las dos cosas se muevan juntas.
        var valores = Enum.GetValues<EstadoSolicitud>().Select(estado => (int)estado).Order().ToArray();

        Assert.Equal(new[] { 0, 1, 2 }, valores);
        Assert.Equal(0, (int)EstadoSolicitud.Pendiente);
        Assert.Equal(1, (int)EstadoSolicitud.Aprobada);
        Assert.Equal(2, (int)EstadoSolicitud.Rechazada);
    }
}
