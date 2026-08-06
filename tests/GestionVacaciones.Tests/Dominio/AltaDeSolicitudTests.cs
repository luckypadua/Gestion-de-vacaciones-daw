using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// B5-T5 y B5-T9: el camino feliz del alta —persistir en <c>Pendiente</c> con período, días corridos y
/// fecha de creación (AC-04)— y la propagación del fallo de una constraint.
/// </summary>
/// <remarks>
/// Necesitan la instancia SQL Server 2022 del entorno: lo que se afirma es qué quedó escrito en la base
/// y qué hace la base cuando lo escrito es inválido. Si no hay cadena configurada se saltean con motivo
/// explícito. Cada caso crea su propio empleado y lo borra al terminar (regla #0).
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class AltaDeSolicitudTests
{
    /// <summary>
    /// Reloj detenido, con desplazamiento distinto de cero: así el aserto sobre la fecha de creación dice
    /// algo sobre el huso y no solo sobre el instante.
    /// </summary>
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 30, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public AltaDeSolicitudTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task B5_T5_Una_solicitud_valida_se_persiste_en_Pendiente_con_periodo_dias_y_fecha_de_creacion()
    {
        // AC-04. Los siete campos de la fila salen del servicio, no del test: el test solo aporta las dos
        // fechas, que es exactamente lo que aporta el empleado en el formulario.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);
        var fin = inicio.AddDays(2);

        var resultado = await servicio.CrearAsync(inicio, fin, Cancelacion);

        Assert.True(resultado.FueCreada, resultado.MensajeDeError);
        Assert.NotEqual(0, resultado.SolicitudId);
        Assert.Equal(string.Empty, resultado.MensajeDeError);

        // Contexto nuevo: con el mismo se releería la instancia rastreada en memoria y el test no diría
        // nada sobre lo que quedó en la base.
        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == resultado.SolicitudId, Cancelacion);

        Assert.Equal(empleado.Id, persistida.EmpleadoId);
        Assert.Equal(inicio, persistida.FechaInicio);
        Assert.Equal(fin, persistida.FechaFin);

        // Días corridos inclusivos, y calculados por el dominio: 10-ene → 12-ene son 3, no 2. La tercera
        // check constraint del Bloque 2 rechazaría cualquier otro número, así que si el servicio contara
        // distinto esto no llegaría ni a insertarse.
        Assert.Equal(3, persistida.DiasCorridos);

        // Nace y permanece en Pendiente: el flujo de aprobación es de un ticket futuro y ninguna
        // solicitud produce efectos hasta que exista.
        Assert.Equal(EstadoSolicitud.Pendiente, persistida.Estado);

        // La fecha de creación sale del TimeProvider inyectado, no del reloj de la máquina. Es el aserto
        // que se rompe si alguien vuelve a poner DateTimeOffset.UtcNow en el servicio.
        Assert.Equal(tiempo.Ahora, persistida.FechaCreacion);

        // Y conserva el desplazamiento local. DateTimeOffset.Equals compara el instante e ignora el
        // huso, así que sin esta línea el aserto de arriba pasaría igual guardando el valor en UTC: el
        // historial que ve una persona muestra la hora a la que ella pidió las vacaciones.
        Assert.Equal(tiempo.Ahora.Offset, persistida.FechaCreacion.Offset);
    }

    [Fact]
    public async Task Una_solicitud_que_arranca_hoy_mismo_se_acepta()
    {
        // El borde exacto de AC-02: el criterio dice «anterior a hoy», no «anterior o igual». Sin este
        // caso, una comparación con <= dejaría B5-T3 en verde e impediría pedirse el día de hoy, que es
        // el uso más común de todos —te enfermás y lo cargás a la mañana—.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioPara(empleado.Id, tiempo);

        var resultado = await servicio.CrearAsync(tiempo.Hoy, tiempo.Hoy, Cancelacion);

        Assert.True(resultado.FueCreada, resultado.MensajeDeError);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == resultado.SolicitudId, Cancelacion);

        Assert.Equal(tiempo.Hoy, persistida.FechaInicio);
        Assert.Equal(1, persistida.DiasCorridos);
    }

    [Fact]
    public async Task Un_periodo_de_300_dias_ahora_se_rechaza_por_el_tope()
    {
        // JUBILA A Un_periodo_de_duracion_arbitraria_se_acepta_en_este_ticket, que documentaba que el
        // tope anual de 14 días era de FEAT-001b y no de este ticket, y por eso un período de 300 días
        // se aceptaba en FEAT-001a. FEAT-001b es justamente el que lo entrega: el mismo período de 300
        // días ahora tiene que rechazarse con el literal de AC-02/AC-03 —el prefijo es el mismo en los
        // dos casos, y con el reloj fijo de esta clase el período cruza el 31 de diciembre, así que cae
        // en el caso de dos años—. No se borra en silencio: este caso es la prueba de que el
        // comportamiento documentado como «pendiente» cambió a propósito. El literal exacto, para uno y
        // para dos años, lo verifica TopeAnualEnElAltaTests.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(1);

        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(299), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.StartsWith(
            "No dispones de días suficientes.", resultado.MensajeDeError, StringComparison.Ordinal);

        await using var relectura = _baseDeDatos.CrearContexto();
        Assert.False(
            await relectura.Solicitudes.AnyAsync(solicitud => solicitud.EmpleadoId == empleado.Id, Cancelacion),
            "El período de 300 días quedó persistido pese a superar el tope anual.");
    }

    [Fact]
    public async Task B5_T9_Un_fallo_de_constraint_se_propaga_y_no_se_traga()
    {
        // La tabla de errores del bloque: «Fallo al persistir (constraint, conexión) → la excepción se
        // propaga. La constraint que salta indica un bug de validación, no un error del usuario». Un
        // catch que lo convirtiera en un ResultadoDelAlta rechazado le mostraría al empleado un mensaje
        // de validación por un defecto del código, y el bug quedaría invisible.
        //
        // La constraint que se viola es la FK a Empleado: la identidad apunta a un empleado que no está
        // en la nómina. Es el único fallo de persistencia alcanzable desde el servicio sin romper a mano
        // sus propias validaciones, y es exactamente lo que ocurriría con un Id de empleado ya dado de
        // baja.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        var tiempo = new TiempoFijo(_instanteFijo);

        // int.MaxValue no lo alcanza ninguna clave identity de esta base.
        const int empleadoInexistente = int.MaxValue;
        var servicio = ServicioPara(empleadoInexistente, tiempo);

        var inicio = tiempo.Hoy.AddDays(5);

        var excepcion = await Assert.ThrowsAsync<DbUpdateException>(
            () => servicio.CrearAsync(inicio, inicio.AddDays(1), Cancelacion));

        Assert.NotNull(excepcion.InnerException);

        // Y no quedó nada escrito: la propagación no puede haber dejado una fila a medias.
        await using var relectura = _baseDeDatos.CrearContexto();

        Assert.False(
            await relectura.Solicitudes.AnyAsync(
                solicitud => solicitud.EmpleadoId == empleadoInexistente, Cancelacion),
            "Quedó una solicitud a nombre de un empleado que no existe en la nómina.");
    }

    private SolicitudesService ServicioPara(int empleadoId, TimeProvider tiempo)
    {
        var fabrica = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService();
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempo);

        // Bloque 3 de FEAT-001b: quinta y sexta dependencia de SolicitudesService. Ningún caso de esta
        // clase afirma sobre el log, así que un NullLogger alcanza; TopeAnualEnElAltaTests es quien
        // ejercita el registro de verdad (R-18).
        return new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, NullLogger<SolicitudesService>.Instance);
    }
}
