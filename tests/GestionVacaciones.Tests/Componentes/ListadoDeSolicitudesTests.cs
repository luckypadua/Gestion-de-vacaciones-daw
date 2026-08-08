using Bunit;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Web.Components.Solicitudes;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// B6-T4 y B6-T7: el listado renderiza lo que le entrega <c>SolicitudesService.ListarPropiasAsync</c>
/// —en el orden en que llega y con el estado de cada solicitud (AC-05)— y, cuando no hay ninguna,
/// muestra un estado vacío explícito.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué el listado recibe las solicitudes por parámetro y no las consulta.</b> El orden
/// descendente por fecha de creación lo produce el servicio, y lo verifica contra la instancia SQL
/// Server real <c>ListadoPropioTests</c> del Bloque 5: es el motor quien ordena, sirviendo el índice
/// <c>IX_Solicitud_EmpleadoId_FechaCreacion</c>. Lo que le toca a este componente es <b>no alterar</b>
/// ese orden, y eso se afirma entregándole una secuencia y comparando la que renderiza. Un componente
/// que consultara por su cuenta tendría además que inyectar la fábrica de contextos, que es
/// exactamente lo que B6-T8 cuenta en cero.
/// </para>
/// </remarks>
public sealed class ListadoDeSolicitudesTests : ContextoDeComponentes
{
    [Fact]
    public void B6_T4_Renderiza_las_solicitudes_en_el_orden_recibido_y_con_el_estado_de_cada_una()
    {
        // AC-05. Tal como las entrega el servicio: descendente por fecha de creación. Los
        // identificadores van a contramano del orden de creación a propósito —11, 47, 3— para que un
        // ordenamiento propio del componente, por Id o por fecha de inicio, se note.
        var solicitudes = new[]
        {
            Solicitud(11, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), EstadoSolicitud.Aprobada,
                new DateTimeOffset(2026, 6, 20, 8, 30, 0, TimeSpan.FromHours(-3))),
            Solicitud(47, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), EstadoSolicitud.Pendiente,
                new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.FromHours(-3))),
            Solicitud(3, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6), EstadoSolicitud.Rechazada,
                new DateTimeOffset(2026, 4, 11, 17, 45, 0, TimeSpan.FromHours(-3))),
        };

        var listado = Render<ListadoDeSolicitudes>(parametros => parametros.Add(
            componente => componente.Solicitudes, solicitudes));

        var filas = listado.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDeLaFila}']");

        Assert.Equal(3, filas.Count);

        // El orden recibido, intacto.
        Assert.Equal(
            ["11", "47", "3"],
            filas.Select(fila => fila.GetAttribute(ListadoDeSolicitudes.AtributoDelIdentificador)));

        // Cada una con SU estado: los tres del enum aparecen y no colapsan en «Pendiente». Sin esto, el
        // orden podría estar bien y el estado ser una columna fija.
        Assert.Equal(
            ["Aprobada", "Pendiente", "Rechazada"],
            filas.Select(fila => fila
                .QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDelEstado}']")!.TextContent.Trim()));

        // Y el período con sus días corridos: el listado de AC-05 es lo que el empleado usa para saber
        // qué pidió, no solo cuándo lo pidió.
        Assert.Equal(
            ["1", "3", "5"],
            filas.Select(fila => fila
                .QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDeLosDiasCorridos}']")!.TextContent.Trim()));

        var primera = filas[0];
        Assert.Contains(
            "05/10/2026",
            primera.QuerySelector($"[data-testid='{ListadoDeSolicitudes.IdDelPeriodo}']")!.TextContent,
            StringComparison.Ordinal);
    }

    [Fact]
    public void B6_T7_Sin_solicitudes_muestra_el_estado_vacio_explicito_y_ninguna_fila()
    {
        // El estado vacío legítimo, que la tabla de errores del bloque exige distinguir del fallo: «no
        // enviaste solicitudes» es una respuesta, no un problema. Un listado que se limitara a no
        // pintar filas dejaría la pantalla igual que un error tragado en silencio.
        var listado = Render<ListadoDeSolicitudes>(parametros => parametros.Add(
            componente => componente.Solicitudes, Array.Empty<SolicitudDelListado>()));

        Assert.Empty(listado.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDeLaFila}']"));

        var vacio = listado.Find($"[data-testid='{ListadoDeSolicitudes.IdDelEstadoVacio}']");

        Assert.Contains("todavía no enviaste solicitudes", vacio.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    private static SolicitudDelListado Solicitud(
        int id,
        DateOnly inicio,
        DateOnly fin,
        EstadoSolicitud estado,
        DateTimeOffset creacion) =>
        new(id, EmpleadoDeLasSolicitudes, inicio, fin, CalculadorDeDiasCorridos.Contar(inicio, fin), estado, creacion);

    private const int EmpleadoDeLasSolicitudes = 7;
}
