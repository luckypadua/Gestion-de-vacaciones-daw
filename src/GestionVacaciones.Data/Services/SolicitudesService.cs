using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;

namespace GestionVacaciones.Data.Services;

/// <summary>
/// Qué pasó al intentar dar de alta una solicitud: o quedó creada, o se rechazó con un motivo. Nunca las
/// dos cosas y nunca ninguna.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué el rechazo de validación no es una excepción.</b> Un período con la fecha de inicio pasada
/// no es un fallo del sistema: es una respuesta prevista, y AC-02 y AC-03 exigen mostrarle al empleado un
/// mensaje concreto. Como excepción, la interfaz tendría que capturarla para renderizarla, o sea control
/// de flujo por excepción, y <c>AGENTS.md</c> prohíbe el <c>catch</c> silencioso justamente porque ese
/// camino se termina usando para tapar los fallos que sí lo son. Los fallos de verdad —la base caída, una
/// constraint violada, la identidad sin resolver— <b>sí</b> se propagan.
/// </para>
/// <para>
/// Mismo carácter que <c>IdentidadDelEmpleado</c>: se construye solo por
/// <see cref="Creada"/> o por <see cref="Rechazada"/>, y no existe ningún valor que signifique «no sé».
/// </para>
/// </remarks>
public sealed class ResultadoDelAlta
{
    private readonly int? _solicitudId;

    private ResultadoDelAlta(int? solicitudId, string mensajeDeError)
    {
        _solicitudId = solicitudId;
        MensajeDeError = mensajeDeError;
    }

    /// <summary>¿Quedó creada la solicitud? Lo que hay que preguntar antes de pedir el identificador.</summary>
    public bool FueCreada => _solicitudId is not null;

    /// <summary>
    /// Identificador de la solicitud creada.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si el alta se rechazó. Lanza en vez de devolver <c>0</c>: un cero se leería como una solicitud con
    /// identificador 0 y mandaría a buscar una fila que no existe.
    /// </exception>
    public int SolicitudId => _solicitudId ?? throw new InvalidOperationException(
        "El alta se rechazó, así que no hay ninguna solicitud creada de la que dar el identificador. " +
        $"Preguntá por {nameof(FueCreada)} y, si es false, mostrá {nameof(MensajeDeError)}.");

    /// <summary>
    /// Mensaje literal del PRD que hay que mostrarle al empleado, o cadena vacía si el alta salió bien.
    /// </summary>
    public string MensajeDeError { get; }

    /// <summary>La solicitud quedó persistida con el identificador <paramref name="solicitudId"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si el identificador no es positivo: la clave es <c>identity</c> y arranca en 1, así que un 0
    /// significa que la fila no se escribió y este resultado estaría afirmando lo contrario.
    /// </exception>
    public static ResultadoDelAlta Creada(int solicitudId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(solicitudId);

        return new ResultadoDelAlta(solicitudId, string.Empty);
    }

    /// <summary>El alta se rechazó por <paramref name="mensajeDeError"/>, que es un literal del PRD.</summary>
    /// <exception cref="ArgumentException">
    /// Si el mensaje está en blanco: un rechazo sin motivo dejaría al empleado sin saber qué corregir, y
    /// a la interfaz sin nada que mostrar junto al formulario.
    /// </exception>
    public static ResultadoDelAlta Rechazada(string mensajeDeError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeDeError);

        return new ResultadoDelAlta(null, mensajeDeError);
    }

    /// <summary>
    /// Descripción para diagnóstico. Lleva el identificador o el motivo del rechazo —que es un literal
    /// fijo del PRD— y ningún dato de la persona (R-12).
    /// </summary>
    public override string ToString() =>
        _solicitudId is null
            ? $"{nameof(ResultadoDelAlta)} {{ rechazada: {MensajeDeError} }}"
            : $"{nameof(ResultadoDelAlta)} {{ SolicitudId = {_solicitudId} }}";
}

/// <summary>
/// Solicitud tal como la necesita el listado propio de AC-05: el período, sus días corridos, su estado
/// actual y cuándo se creó.
/// </summary>
/// <remarks>
/// Es una proyección y no la entidad <c>Solicitud</c>, por el mismo motivo que <c>EmpleadoDeLaNomina</c>:
/// la entidad arrastra la navegación al <c>Empleado</c> —con su nombre y su correo, que son PII
/// (F-TM-05)— y el listado no necesita nada de eso. <c>EmpleadoId</c> sí viaja: es lo que permite afirmar
/// que el filtro de FR-04 se aplicó, sin tener que confiar en que se aplicó.
/// </remarks>
public sealed record SolicitudDelListado(
    int Id,
    int EmpleadoId,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    int DiasCorridos,
    EstadoSolicitud Estado,
    DateTimeOffset FechaCreacion)
{
    /// <summary>
    /// Descripción para diagnóstico: identificadores y estado, sin el período.
    /// </summary>
    /// <remarks>
    /// <b>Por qué se escribe a mano.</b> El <c>ToString()</c> que genera un <c>record</c> imprime todas
    /// sus propiedades, y las fechas de las vacaciones de una persona identificada son un dato personal
    /// suyo —cuándo no va a estar—. Nadie registra este objeto hoy, pero el camino corto para loguear es
    /// interpolarlo entero, y por ahí el dato vuelve sin que se escriba una línea que lo delate en la
    /// revisión. Misma mitigación, mismo motivo, que en <c>EmpleadoDeLaNomina</c> (R-12).
    /// </remarks>
    public override string ToString() =>
        $"{nameof(SolicitudDelListado)} {{ Id = {Id}, EmpleadoId = {EmpleadoId}, Estado = {Estado} }}";
}

/// <summary>
/// Alta de una solicitud de vacaciones y listado de las propias: las dos reglas de dominio de FEAT-001a.
/// </summary>
/// <remarks>
/// <para>
/// <b>La validación vive acá, en el servidor (mitigación R-10).</b> AC-02 y AC-03 se enuncian como
/// «impedir el envío y mostrar el mensaje», lo que invita a resolverlos deshabilitando un botón: un
/// cliente manipulado o un evento fuera de orden esquivarían la regla. El formulario del Bloque 6
/// <b>consume</b> el resultado de <see cref="CrearAsync"/> en lugar de reimplementar la comparación.
/// </para>
/// <para>
/// <b>El autor sale de <see cref="IEmpleadoActualProvider"/> y nunca de un parámetro</b> (NFR-06). No
/// existe una sobrecarga que reciba el empleado: la habría usado el primer llamador que tuviera el
/// identificador a mano, y con eso la identidad tendría dos sedes y cualquiera podría crear solicitudes a
/// nombre de otro.
/// </para>
/// <para>
/// <b>Quién ve qué lo decide <see cref="PermisosService"/></b>, no este servicio (<c>AGENTS.md</c>).
/// </para>
/// <para>
/// <b>Fuente de tiempo:</b> <see cref="TimeProvider"/> inyectado. Sin él, «hoy» sale del reloj de la
/// máquina, los tests de AC-02 dependen del día en que se corran y del huso configurado, y se rompen solos
/// al cruzar la medianoche.
/// </para>
/// <para>
/// <b>Fuera de alcance en este ticket:</b> el tope anual de 14 días (FEAT-001b) y la detección de
/// superposición (FEAT-001c). Un período de duración arbitraria <b>se acepta</b> en FEAT-001a, y el PRD lo
/// declara explícitamente.
/// </para>
/// <para>
/// Acceso a datos siempre con <see cref="IDbContextFactory{TContext}"/> (NFR-05): cada operación abre y
/// cierra el suyo. Y <b>sin ningún <c>catch</c></b>: los fallos de persistencia se propagan.
/// </para>
/// </remarks>
public sealed class SolicitudesService
{
    private readonly IDbContextFactory<VacacionesDbContext> _fabrica;
    private readonly IEmpleadoActualProvider _empleadoActual;
    private readonly PermisosService _permisos;
    private readonly TimeProvider _tiempo;

    /// <param name="fabrica">
    /// Fábrica de contextos. Siempre <see cref="IDbContextFactory{TContext}"/> y nunca un
    /// <c>DbContext</c> inyectado (NFR-05, <c>AGENTS.md</c>).
    /// </param>
    /// <param name="empleadoActual">
    /// Única sede de la identidad (NFR-06): de acá sale el autor de toda solicitud y el sujeto de todo
    /// listado.
    /// </param>
    /// <param name="permisos">Única sede de la decisión de visibilidad (<c>AGENTS.md</c>).</param>
    /// <param name="tiempo">
    /// Fuente de tiempo. Inyectada, no leída del reloj del sistema: es lo que hace verificable la regla de
    /// AC-02.
    /// </param>
    public SolicitudesService(
        IDbContextFactory<VacacionesDbContext> fabrica,
        IEmpleadoActualProvider empleadoActual,
        PermisosService permisos,
        TimeProvider tiempo)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(empleadoActual);
        ArgumentNullException.ThrowIfNull(permisos);
        ArgumentNullException.ThrowIfNull(tiempo);

        _fabrica = fabrica;
        _empleadoActual = empleadoActual;
        _permisos = permisos;
        _tiempo = tiempo;
    }

    /// <summary>
    /// Registra una solicitud del empleado actual en estado <c>Pendiente</c>, si el período supera las
    /// validaciones de fecha (FR-01, FR-03, AC-04).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>El orden de las validaciones es parte del contrato</b>, y es observable: con las dos fechas mal,
    /// el mensaje que sale es el de la fecha de inicio. Primero se comprueba que la fecha de inicio no sea
    /// anterior a hoy, después que la de fin no sea anterior a la de inicio, y <b>solo entonces</b> se
    /// persiste.
    /// </para>
    /// <para>
    /// «Hoy» es el día <b>local</b> y no el de UTC. Quien pide vacaciones vive en un huso: con un reloj en
    /// UTC-3, a partir de las 21:00 el día en UTC ya es el siguiente, y una solicitud que arranca hoy se
    /// rechazaría por «anterior a hoy» durante las últimas tres horas de cada día. El huso lo aporta el
    /// propio <see cref="TimeProvider"/>, así que en los tests es explícito y no ambiental.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Si no hay empleado actual resuelto —la excepción viene del proveedor y <b>se propaga</b>: no se
    /// crea nada anónimo—. <see cref="SinEmpleadoSeleccionadoException"/> cuando el circuito todavía no
    /// eligió a nadie.
    /// </exception>
    /// <exception cref="DbUpdateException">
    /// Si falla la persistencia. <b>Se propaga a propósito</b>: una check constraint que salta indica un
    /// bug de validación, no un error del usuario, y convertirla en un rechazo le mostraría al empleado un
    /// mensaje de validación por un defecto del código.
    /// </exception>
    public async Task<ResultadoDelAlta> CrearAsync(
        DateOnly fechaInicio,
        DateOnly fechaFin,
        CancellationToken cancelacion = default)
    {
        // Lo primero: de quién es la solicitud. Sin autor no hay nada que crear, y esto lanza antes de
        // mirar las fechas en lugar de devolver un rechazo de validación, que la interfaz mostraría junto
        // al formulario como si el problema fuera el período.
        var autor = _empleadoActual.Identidad.Id;

        var hoy = DateOnly.FromDateTime(_tiempo.GetLocalNow().DateTime);

        // AC-02. Estrictamente «anterior»: pedirse el día de hoy es válido y es el caso más común de
        // todos.
        if (fechaInicio < hoy)
        {
            return ResultadoDelAlta.Rechazada(ErroresDeSolicitud.FechaDeInicioAnteriorAHoy);
        }

        // AC-03, y solo después de la anterior: el orden es observable y está fijado por un test.
        if (fechaFin < fechaInicio)
        {
            return ResultadoDelAlta.Rechazada(ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio);
        }

        var solicitud = new Solicitud
        {
            EmpleadoId = autor,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin,

            // El mismo cálculo que muestra la interfaz (AC-01): un punto único, y la check constraint
            // CK_Solicitud_DiasCoincidenConPeriodo rechazando cualquier discrepancia.
            DiasCorridos = CalculadorDeDiasCorridos.Contar(fechaInicio, fechaFin),

            // Nace y permanece en Pendiente: el flujo de aprobación es de un ticket futuro, así que
            // ninguna solicitud produce efectos todavía.
            Estado = EstadoSolicitud.Pendiente,

            // Con el desplazamiento local, que la columna «datetimeoffset» conserva: el historial de
            // AC-05 muestra la hora a la que la persona pidió sus vacaciones, no su traducción a UTC.
            FechaCreacion = _tiempo.GetLocalNow(),
        };

        await using var contexto = await _fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        contexto.Solicitudes.Add(solicitud);
        await contexto.SaveChangesAsync(cancelacion).ConfigureAwait(false);

        return ResultadoDelAlta.Creada(solicitud.Id);
    }

    /// <summary>
    /// Las solicitudes del empleado actual, ordenadas de forma descendente por fecha de creación, cada una
    /// con su estado (FR-04, AC-05).
    /// </summary>
    /// <remarks>
    /// <para>
    /// El filtro y el orden son los que sirve el índice <c>IX_Solicitud_EmpleadoId_FechaCreacion</c> del
    /// Bloque 2, que existe para que el listado no escanee la tabla (NFR-01).
    /// </para>
    /// <para>
    /// La consulta viaja parametrizada por EF Core: no hay SQL concatenado (R-08).
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Si no hay empleado actual resuelto. <b>No se degrada a una lista vacía</b>, que se leería como
    /// «todavía no enviaste solicitudes»: son tres situaciones distintas —sin identidad, sin solicitudes,
    /// y error— que se ven parecidas en pantalla.
    /// </exception>
    /// <exception cref="AccesoASolicitudesDenegadoException">
    /// Si <see cref="PermisosService"/> niega la visibilidad. Tampoco se degrada a lista vacía.
    /// </exception>
    public async Task<IReadOnlyList<SolicitudDelListado>> ListarPropiasAsync(CancellationToken cancelacion = default)
    {
        var quienConsulta = _empleadoActual.Identidad;
        var sujeto = quienConsulta.Id;

        // La decisión de visibilidad se le PREGUNTA a su única sede, en vez de darla por sentada acá. Hoy
        // la respuesta no puede ser «no» —el sujeto es quien consulta—, y el valor de la línea es
        // precisamente que el punto de decisión ya exista cuando FEAT-001b, FEAT-001c y el ticket de
        // aprobación agreguen «y el manager ve las de su equipo»: esa regla se escribe allá, no acá.
        _permisos.ExigirPoderVerLasSolicitudesDe(quienConsulta, sujeto);

        await using var contexto = await _fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        return await contexto.Solicitudes
            .AsNoTracking()
            .Where(solicitud => solicitud.EmpleadoId == sujeto)
            .OrderByDescending(solicitud => solicitud.FechaCreacion)
            .Select(solicitud => new SolicitudDelListado(
                solicitud.Id,
                solicitud.EmpleadoId,
                solicitud.FechaInicio,
                solicitud.FechaFin,
                solicitud.DiasCorridos,
                solicitud.Estado,
                solicitud.FechaCreacion))
            .ToListAsync(cancelacion)
            .ConfigureAwait(false);
    }
}
