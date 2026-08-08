using GestionVacaciones.Data.Services;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// B5-T3, B5-T4 y B5-T6: la validación de fechas del alta, <b>en el servidor</b> (mitigación R-10), y el
/// alta sin identidad resuelta.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ninguno necesita la instancia SQL Server del entorno, y eso es parte de lo que afirman.</b> La
/// fábrica de contextos que reciben los servicios <b>revienta si alguien la usa</b>: así «no persiste»
/// no queda demostrado por un aserto sobre una tabla vacía —que también pasaría si la fila se hubiera
/// escrito y borrado— sino por algo más estricto, que es que no hubo ningún viaje a la base.
/// </para>
/// <para>
/// El reloj está detenido (<see cref="TiempoFijo"/>): sin eso, un test que hable de «ayer» y de «hoy»
/// depende del día en que se corra y se rompe solo al cruzar la medianoche.
/// </para>
/// </remarks>
public sealed class ValidacionDelAltaTests
{
    /// <summary>
    /// Instante en el que se detiene el reloj de estos tests. Con desplazamiento distinto de cero a
    /// propósito: «hoy» es el día local de quien pide las vacaciones, y con un reloj en UTC-3 a las
    /// 22:00 el día en UTC ya es el siguiente. Si el servicio leyera el instante en UTC, una solicitud
    /// que arranca hoy se rechazaría por «anterior a hoy» durante las últimas tres horas de cada día.
    /// </summary>
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 22, 30, 0, TimeSpan.FromHours(-3));

    private const int EmpleadoActual = 7;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task B5_T3_Una_fecha_de_inicio_anterior_a_hoy_se_rechaza_con_el_mensaje_literal_y_no_persiste()
    {
        // AC-02. El literal se compara carácter por carácter contra la constante, y la constante contra
        // el PRD en MensajesLiteralesDelPrdTests: sin ese segundo eslabón, este test aprobaría cualquier
        // texto con tal de que el código y el test coincidan.
        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioSinBase(tiempo);

        var ayer = tiempo.Hoy.AddDays(-1);

        var resultado = await servicio.CrearAsync(ayer, ayer.AddDays(2), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.FechaDeInicioAnteriorAHoy, resultado.MensajeDeError);
    }

    [Fact]
    public async Task B5_T4_Una_fecha_de_fin_anterior_a_la_de_inicio_se_rechaza_con_el_mensaje_literal_y_no_persiste()
    {
        // AC-03. La fecha de inicio es válida —hoy mismo—, así que lo único que puede estar rechazando
        // el período es el orden de las dos fechas.
        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioSinBase(tiempo);

        var inicio = tiempo.Hoy.AddDays(10);

        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(-1), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Con_las_dos_fechas_mal_el_mensaje_es_el_de_la_fecha_de_inicio()
    {
        // El orden de validación que fija la spec —primero el inicio, después el fin— es OBSERVABLE, y
        // este es el único caso que lo observa. Sin él, invertir las dos comprobaciones dejaría B5-T3 y
        // B5-T4 en verde: cada uno satisface una sola de las dos reglas.
        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioSinBase(tiempo);

        var inicio = tiempo.Hoy.AddDays(-5);

        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(-1), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.FechaDeInicioAnteriorAHoy, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Un_rechazo_no_entrega_ningun_identificador_de_solicitud()
    {
        // La contracara del estado explícito: «rechazada» no puede devolver un 0 en el identificador,
        // que se leería como una solicitud creada con Id 0 y mandaría a buscar una fila que no existe.
        var servicio = ServicioSinBase(new TiempoFijo(_instanteFijo));

        var resultado = await servicio.CrearAsync(
            new DateOnly(2020, 1, 1), new DateOnly(2020, 1, 3), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Throws<InvalidOperationException>(() => _ = resultado.SolicitudId);
    }

    [Fact]
    public async Task B5_T6_Sin_empleado_actual_resuelto_el_alta_lanza_y_no_persiste()
    {
        // La tabla de errores del bloque: «Sin empleado actual resuelto → excepción propagada desde el
        // proveedor; no se crea nada anónimo». El proveedor no entregó identidad, así que no hay a
        // nombre de quién crear la solicitud, y el servicio NO inventa un autor ni devuelve un rechazo
        // de validación —que la interfaz mostraría junto al formulario como si fuera culpa de las
        // fechas—.
        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = new SolicitudesService(
            new FabricaQueNadieDebeUsar(),
            IdentidadDePrueba.SinSeleccionar(),
            new PermisosService(),
            tiempo);

        var inicio = tiempo.Hoy.AddDays(3);

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(
            () => servicio.CrearAsync(inicio, inicio.AddDays(1), Cancelacion));
    }

    [Fact]
    public async Task Sin_empleado_actual_resuelto_el_listado_tambien_lanza_en_vez_de_devolver_vacio()
    {
        // La otra mitad de B5-T6, y la peligrosa: un listado vacío se lee como «todavía no enviaste
        // solicitudes», que es exactamente lo que la tabla de errores prohíbe confundir con «no hay
        // identidad». Los tres estados —sin identidad, sin solicitudes, y error— se ven parecidos en
        // pantalla y significan cosas distintas.
        var servicio = new SolicitudesService(
            new FabricaQueNadieDebeUsar(),
            IdentidadDePrueba.SinSeleccionar(),
            new PermisosService(),
            new TiempoFijo(_instanteFijo));

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(() => servicio.ListarPropiasAsync(Cancelacion));
    }

    /// <summary>
    /// Servicio con identidad resuelta y con una fábrica de contextos que lanza si alguien la usa: es
    /// «este caso no llega a la base» hecho código.
    /// </summary>
    private static SolicitudesService ServicioSinBase(TimeProvider tiempo) =>
        new(new FabricaQueNadieDebeUsar(), IdentidadDePrueba.De(EmpleadoActual), new PermisosService(), tiempo);
}
