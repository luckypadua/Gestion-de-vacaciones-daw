using System.Data;
using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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

    /// <summary>
    /// FEAT-001b, mitigación de <b>R-14</b>. El rechazo por saldo insuficiente lleva, en
    /// <see cref="MensajeDeError"/>, el saldo real de una persona identificable. Ese texto es lo que se
    /// muestra al empleado —eso no cambia—, pero <see cref="ToString"/> es lo que un log interpola sin
    /// que nadie escriba una línea que lo delate, y un saldo junto al <c>EmpleadoId</c> que ya se
    /// registra dice cuánto se ausentó esa persona. Los rechazos por fecha no llevan este dato —el
    /// literal es igual para todo el mundo— y siguen apareciendo enteros en el diagnóstico.
    /// </summary>
    private readonly bool _elMotivoEsUnSaldo;

    private ResultadoDelAlta(int? solicitudId, string mensajeDeError, bool elMotivoEsUnSaldo)
    {
        _solicitudId = solicitudId;
        MensajeDeError = mensajeDeError;
        _elMotivoEsUnSaldo = elMotivoEsUnSaldo;
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

        return new ResultadoDelAlta(solicitudId, string.Empty, elMotivoEsUnSaldo: false);
    }

    /// <summary>El alta se rechazó por <paramref name="mensajeDeError"/>, que es un literal del PRD.</summary>
    /// <exception cref="ArgumentException">
    /// Si el mensaje está en blanco: un rechazo sin motivo dejaría al empleado sin saber qué corregir, y
    /// a la interfaz sin nada que mostrar junto al formulario.
    /// </exception>
    public static ResultadoDelAlta Rechazada(string mensajeDeError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeDeError);

        return new ResultadoDelAlta(null, mensajeDeError, elMotivoEsUnSaldo: false);
    }

    /// <summary>
    /// FEAT-001b. El alta se rechazó porque el saldo no alcanza —AC-02 o AC-03, ya compuesto con el
    /// saldo real por <see cref="ErroresDeSolicitud.ComponerSaldoInsuficiente"/> o
    /// <see cref="ErroresDeSolicitud.ComponerSaldoInsuficienteDeDosAnios"/>—. Se distingue de
    /// <see cref="Rechazada"/> porque <see cref="ToString"/> tiene que omitir este texto (R-14): es el
    /// único mensaje de rechazo que lleva un dato de la persona.
    /// </summary>
    /// <exception cref="ArgumentException">Si el mensaje está en blanco.</exception>
    public static ResultadoDelAlta RechazadaPorSaldoInsuficiente(string mensajeDeError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mensajeDeError);

        return new ResultadoDelAlta(null, mensajeDeError, elMotivoEsUnSaldo: true);
    }

    /// <summary>
    /// Descripción para diagnóstico. Lleva el identificador o el motivo del rechazo —que es un literal
    /// fijo del PRD— y ningún dato de la persona (R-12), salvo que el motivo lleve un saldo, en cuyo
    /// caso el texto se omite del todo (R-14).
    /// </summary>
    public override string ToString() =>
        _solicitudId is null
            ? (_elMotivoEsUnSaldo
                // El literal "14" no puede aparecer acá ni siquiera como referencia al riesgo: rompería
                // PuntoUnicoDelTopeTests.El_numero_del_tope_solo_aparece_en_TopeAnual (NFR-01), que
                // escanea las cadenas del código y no solo los comentarios.
                ? $"{nameof(ResultadoDelAlta)} {{ rechazada: saldo insuficiente (motivo omitido) }}"
                : $"{nameof(ResultadoDelAlta)} {{ rechazada: {MensajeDeError} }}")
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
/// <b>El tope anual, desde FEAT-001b (Bloque 3).</b> <see cref="CrearAsync"/> compara los días del
/// período contra el saldo de cada año calendario que afecta, provisto por <see cref="SaldoService"/> —
/// única fuente del cálculo (NFR-04)—, dentro de una transacción serializable que cierra la carrera de
/// dos altas concurrentes del mismo empleado (R-17). La detección de superposición sigue fuera de
/// alcance: la entrega FEAT-001c.
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
    private readonly SaldoService _saldo;
    private readonly ILogger<SolicitudesService> _registro;

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
    /// <param name="saldo">
    /// FEAT-001b. Única fuente del saldo por año (NFR-04): contra ella se compara el período antes de
    /// persistir.
    /// </param>
    /// <param name="registro">
    /// FEAT-001b. El rechazo por tope se registra acá, con <c>EmpleadoId</c> y año, nunca con el mensaje
    /// compuesto (R-18). Mismo patrón que <c>SeedDatos</c>.
    /// </param>
    public SolicitudesService(
        IDbContextFactory<VacacionesDbContext> fabrica,
        IEmpleadoActualProvider empleadoActual,
        PermisosService permisos,
        TimeProvider tiempo,
        SaldoService saldo,
        ILogger<SolicitudesService> registro)
    {
        ArgumentNullException.ThrowIfNull(fabrica);
        ArgumentNullException.ThrowIfNull(empleadoActual);
        ArgumentNullException.ThrowIfNull(permisos);
        ArgumentNullException.ThrowIfNull(tiempo);
        ArgumentNullException.ThrowIfNull(saldo);
        ArgumentNullException.ThrowIfNull(registro);

        _saldo = saldo;
        _registro = registro;
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
    /// <remarks>
    /// <para>
    /// FEAT-001b (Bloque 3). <b>El orden es contrato:</b> el tope se valida <i>después</i> de las dos
    /// fechas y <i>antes</i> de construir la entidad — con las dos fechas mal, o con un período invertido,
    /// no se abre ningún contexto (fijado por <c>ValidacionDelAltaTests</c> y por
    /// <c>El_tope_se_valida_despues_de_las_fechas_y_sin_tocar_la_base</c>, montados con
    /// <c>FabricaQueNadieDebeUsar</c>).
    /// </para>
    /// <para>
    /// <b>Más de dos años calendario</b> se rechaza en memoria, sin tocar la base (R-15): un año calendario
    /// íntegro dentro del período ya imputa 365 o 366 días, que supera el tope en cualquier saldo posible.
    /// </para>
    /// <para>
    /// <b>La transacción serializable</b> envuelve leer el saldo y guardar (R-17). Dentro de ella se hace
    /// además una lectura propia, de solo lectura y de resultado descartado, sobre <b>esta misma
    /// conexión</b>: es lo que fuerza el <i>lock</i> de rango serializable que <see cref="SaldoService"/>
    /// —que abre su propio contexto por diseño, NFR-05— no puede sostener por sí sola, porque su lectura
    /// termina (y libera el suyo) en cuanto vuelve, mucho antes de este guardado. Verificado empíricamente
    /// contra la instancia real: sin esta lectura acá, dos altas concurrentes con la transacción puesta
    /// igual suman más de <see cref="TopeAnual.Dias"/>.
    /// </para>
    /// <para>
    /// <b>El reparto entre dos años</b> no llama a <c>ImputacionPorAnio.DiasEnElAnio</c>: esa función está
    /// restringida a <see cref="SaldoService"/> (<c>PuntoUnicoDelTopeTests.Solo_SaldoService_consume_DiasEnElAnio</c>,
    /// Bloque 2). Acotado ya a como máximo dos años consecutivos, partir en el 31 de diciembre con
    /// <see cref="CalculadorDeDiasCorridos.Contar"/> —la misma función que hace todo el conteo del
    /// dominio— produce el mismo número sin una segunda implementación de la fórmula.
    /// </para>
    /// <para>
    /// Tope superado → <see cref="ResultadoDelAlta.RechazadaPorSaldoInsuficiente"/>, no excepción. Un
    /// fallo real de persistencia, o cualquier excepción del motor —incluido un conflicto de
    /// serialización, error 1205— se propaga tal cual, sin <c>catch</c> y sin reintento (R-16).
    /// </para>
    /// </remarks>
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

        var diasCorridos = CalculadorDeDiasCorridos.Contar(fechaInicio, fechaFin);
        var aniosAbarcados = ImputacionPorAnio.AniosAbarcados(fechaInicio, fechaFin);

        if (aniosAbarcados.Count > SaldoService.MaximoDeAniosPorConsulta)
        {
            // R-15: un año calendario íntegro entra en el período, y 365/366 días superan el tope en
            // cualquier saldo posible. Se rechaza SIN consultar la base: ni siquiera se abre un contexto.
            _registro.LogInformation(
                "Rechazo por tope: el período del empleado {EmpleadoId} abarca {CantidadDeAnios} años " +
                "calendario, más de los {MaximoDeAnios} que admite una única solicitud. Días pedidos: " +
                "{DiasSolicitados}. Se rechazó sin consultar el saldo.",
                autor, aniosAbarcados.Count, SaldoService.MaximoDeAniosPorConsulta, diasCorridos);

            return ResultadoDelAlta.RechazadaPorSaldoInsuficiente(
                ErroresDeSolicitud.ComponerSaldoInsuficiente(0));
        }

        await using var contexto = await _fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);
        await using var transaccion = await contexto.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancelacion)
            .ConfigureAwait(false);

        // Fuerza el lock de rango serializable de ESTA conexión sobre las filas del empleado, ANTES de
        // decidir. Ver el remarks de este método para el porqué: sin esto, dos altas concurrentes no
        // quedan protegidas aunque la transacción exista.
        await contexto.Solicitudes
            .AsNoTracking()
            .Where(solicitud => solicitud.EmpleadoId == autor)
            .AnyAsync(cancelacion)
            .ConfigureAwait(false);

        var saldos = await _saldo.DeLosAniosAsync(aniosAbarcados, cancelacion).ConfigureAwait(false);

        var diasPorAnio = aniosAbarcados.Count == 1
            ? [diasCorridos]
            : PartirEntreLosDosAnios(fechaInicio, diasCorridos, aniosAbarcados[0]);

        var indicesInsuficientes = new List<int>(saldos.Count);
        for (var indice = 0; indice < saldos.Count; indice++)
        {
            if (diasPorAnio[indice] > saldos[indice].DiasDisponibles)
            {
                indicesInsuficientes.Add(indice);
            }
        }

        if (indicesInsuficientes.Count > 0)
        {
            await transaccion.RollbackAsync(cancelacion).ConfigureAwait(false);

            // R-18: EmpleadoId y año, nunca el mensaje compuesto (R-14) — el log no puede llevar lo que
            // ToString() de ResultadoDelAlta ya se ocupa de no exponer.
            _registro.LogInformation(
                "Rechazo por tope: empleado {EmpleadoId}, año(s) afectado(s) {AniosAfectados}, " +
                "{DiasSolicitados} días pedidos.",
                autor,
                string.Join(",", indicesInsuficientes.Select(indice => saldos[indice].Anio)),
                diasCorridos);

            var mensaje = saldos.Count == 1
                ? ErroresDeSolicitud.ComponerSaldoInsuficiente(saldos[0].DiasDisponibles)
                : ErroresDeSolicitud.ComponerSaldoInsuficienteDeDosAnios(saldos[0], saldos[1]);

            return ResultadoDelAlta.RechazadaPorSaldoInsuficiente(mensaje);
        }

        var solicitud = new Solicitud
        {
            EmpleadoId = autor,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin,

            // El mismo cálculo que muestra la interfaz (AC-01): un punto único, y la check constraint
            // CK_Solicitud_DiasCoincidenConPeriodo rechazando cualquier discrepancia.
            DiasCorridos = diasCorridos,

            // Nace y permanece en Pendiente: el flujo de aprobación es de un ticket futuro, así que
            // ninguna solicitud produce efectos todavía.
            Estado = EstadoSolicitud.Pendiente,

            // Con el desplazamiento local, que la columna «datetimeoffset» conserva: el historial de
            // AC-05 muestra la hora a la que la persona pidió sus vacaciones, no su traducción a UTC.
            FechaCreacion = _tiempo.GetLocalNow(),
        };

        contexto.Solicitudes.Add(solicitud);
        await contexto.SaveChangesAsync(cancelacion).ConfigureAwait(false);
        await transaccion.CommitAsync(cancelacion).ConfigureAwait(false);

        return ResultadoDelAlta.Creada(solicitud.Id);
    }

    /// <summary>
    /// Cuántos de los <paramref name="diasCorridos"/> del período caen en <paramref name="primerAnio"/> y
    /// cuántos en el siguiente. Precondición del llamador: el período abarca <b>exactamente</b> dos años
    /// calendario consecutivos —ya lo garantizó <see cref="ImputacionPorAnio.AniosAbarcados"/> más arriba—,
    /// así que el reparto es una resta contra el 31 de diciembre del primero, sin volver a preguntarle a
    /// <c>ImputacionPorAnio</c> (ver el remarks de <see cref="CrearAsync"/>).
    /// </summary>
    private static IReadOnlyList<int> PartirEntreLosDosAnios(
        DateOnly fechaInicio, int diasCorridos, int primerAnio)
    {
        var finDelPrimerAnio = new DateOnly(primerAnio, 12, 31);
        var diasEnElPrimerAnio = CalculadorDeDiasCorridos.Contar(fechaInicio, finDelPrimerAnio);

        return [diasEnElPrimerAnio, diasCorridos - diasEnElPrimerAnio];
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
