using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;

namespace GestionVacaciones.Data.Services;

/// <summary>
/// El saldo de un empleado en un año calendario: cuántos días usó o reservó, y cuántos le quedan.
/// </summary>
/// <param name="Anio">El año al que corresponde el saldo.</param>
/// <param name="DiasUsados">
/// Días imputados a ese año por solicitudes <c>Pendiente</c> o <c>Aprobada</c>. <b>Puede superar el
/// tope</b>: si eso pasa, es una anomalía de los datos y el número la muestra en vez de taparla.
/// </param>
/// <param name="DiasDisponibles">
/// <c>TopeAnual.Dias</c> menos los usados, <b>acotado a 0</b>. Un saldo negativo en pantalla —«te
/// quedan −3 días»— no significa nada para quien lo lee; la anomalía queda visible en
/// <paramref name="DiasUsados"/>, que es donde se puede interpretar.
/// </param>
/// <remarks>
/// <b>Lleva el año adentro a propósito.</b> Es lo que permite que la pantalla muestre el saldo sin
/// saber en qué año estamos: un componente <c>.razor</c> no puede nombrar <see cref="TimeProvider"/>
/// —lo prohíbe un test de escaneo sobre todos los <c>.razor</c>— así que una firma
/// <c>SaldoDe(int anio)</c> invocada desde el marcado sería imposible de escribir sin que el marcado
/// inventara el año.
/// </remarks>
public sealed record SaldoDelAnio(int Anio, int DiasUsados, int DiasDisponibles)
{
    /// <summary>Descripción para diagnóstico: el año, y ninguna cantidad de días (R-12, R-14).</summary>
    /// <remarks>
    /// <b>Por qué se escribe a mano, como en <c>SolicitudDelListado</c> y <c>EmpleadoDeLaNomina</c>.</b> El
    /// <c>ToString()</c> que genera un <c>record</c> imprime todas sus propiedades, y los días que usó o le
    /// quedan a alguien son un dato personal suyo: dicen cuánto se ausentó y cuánto puede ausentarse. Este
    /// tipo no lleva el sujeto adentro, pero el camino corto para loguear es interpolar el objeto en una
    /// línea que sí lo lleva —el <c>EmpleadoId</c> ya se registra—, y ahí queda armado exactamente el par
    /// que R-14 saca del <c>ToString()</c> de <c>ResultadoDelAlta</c>. El año sí viaja: es política de
    /// calendario, no dato de nadie, y sin él la descripción no dice de qué saldo hablaba.
    /// </remarks>
    public override string ToString() => $"{nameof(SaldoDelAnio)} {{ {nameof(Anio)} = {Anio} }}";
}

/// <summary>
/// Calcula el saldo de días del <b>empleado actual</b> en uno o dos años calendario (FR-03).
/// </summary>
/// <remarks>
/// <para>
/// <b>Punto único del cálculo (NFR-04).</b> El saldo que se le muestra al empleado y el saldo contra el
/// que se bloquea su solicitud salen de acá, de la misma función. El PRD registra como primer riesgo
/// que los dos números no coincidan, y la mitigación es que no haya dos números.
/// </para>
/// <para>
/// <b>El filtro viaja a la base; el reparto por año ocurre en memoria.</b> Escribir la imputación
/// dentro del <c>Where</c>/<c>Sum</c> de LINQ la convertiría en un árbol de expresión traducido a SQL,
/// y entonces la regla existiría dos veces: en SQL para el saldo y en C# para todo lo demás. Eso es
/// justamente lo que NFR-04 cuenta en cero. La consulta trae solo los períodos que solapan los años
/// pedidos —filtrados por empleado, estado y rango— y <see cref="ImputacionPorAnio"/> hace el resto. El
/// índice que va a servir ese filtro, <c>IX_Solicitud_EmpleadoId_Estado_FechaInicio</c>, <b>todavía no
/// existe</b>: lo declara el bloque 5 de este ticket con su migración. Hasta entonces, la consulta se
/// apoya en el único índice de hoy —<c>IX_Solicitud_EmpleadoId_FechaCreacion</c>, del listado de
/// FEAT-001a— que acota por empleado pero no por estado ni por fecha de inicio.
/// </para>
/// <para>
/// <b>«Días tomados» ≡ <c>Aprobada</c>.</b> El glosario habla de «tomados + aprobados + pendientes» y
/// el modelo tiene tres estados, ninguno «Tomada»: son dos palabras para una sola cosa. Las
/// <c>Pendiente</c> cuentan igual que las aprobadas —reservan saldo—, que es lo que impide la
/// sobreasignación mientras esperan resolución.
/// </para>
/// <para>
/// <b>Ninguna firma pública acepta un identificador de empleado</b>, y eso es una decisión de
/// seguridad. Un <c>SaldoDeAsync(int empleadoId, int anio)</c> dejaría que cualquier circuito
/// preguntara por cualquiera, con la única defensa de que todos los llamadores se acuerden de validar
/// antes: la referencia directa a objeto insegura de siempre. El sujeto sale de
/// <see cref="IEmpleadoActualProvider"/>, y el día que el manager necesite ver el saldo de su equipo,
/// esa capacidad se agrega <b>con</b> su comprobación en <see cref="PermisosService"/>, no destapando
/// un parámetro.
/// </para>
/// </remarks>
public sealed class SaldoService
{
    /// <summary>
    /// Años como máximo por consulta, y <b>consecutivos</b>: ver
    /// <see cref="ExigirUnRangoAcotado(IReadOnlyList{int})"/>, que es donde el control está escrito
    /// entero.
    /// </summary>
    public const int MaximoDeAniosPorConsulta = 2;

    private readonly IDbContextFactory<VacacionesDbContext> _fabrica;
    private readonly IEmpleadoActualProvider _empleadoActual;
    private readonly PermisosService _permisos;
    private readonly TimeProvider _tiempo;

    /// <param name="fabrica">
    /// Fábrica de contextos. Siempre <see cref="IDbContextFactory{TContext}"/> y nunca un
    /// <c>DbContext</c> inyectado (<c>AGENTS.md</c>): en Blazor Server un contexto scoped vive todo el
    /// circuito, y dos componentes que consultan a la vez lo usarían concurrentemente.
    /// </param>
    /// <param name="empleadoActual">
    /// Única sede de la identidad: de acá sale el sujeto de todo saldo. Es la mitad de R-13 que no se ve
    /// en las firmas — ninguna acepta un identificador porque el sujeto lo pone este proveedor.
    /// </param>
    /// <param name="permisos">
    /// Única sede de la decisión de visibilidad (<c>AGENTS.md</c>). El saldo de un empleado es tan suyo
    /// como su listado, y se le pregunta antes de toda consulta (mitigación 2 de R-13).
    /// </param>
    /// <param name="tiempo">
    /// Fuente de tiempo, inyectada y no leída del reloj del sistema: de ella sale «el año en curso», que
    /// es lo que hace verificable el reinicio del saldo cada 1 de enero sin esperar a que llegue.
    /// </param>
    public SaldoService(
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
    /// El saldo del empleado actual en el año en curso (FR-04, AC-05).
    /// </summary>
    /// <remarks>
    /// El año sale del <see cref="TimeProvider"/> inyectado y en hora <b>local</b>, por la misma razón
    /// que «hoy» lo es en el alta: con un reloj en UTC-3, a partir de las 21:00 del 31 de diciembre el
    /// año en UTC ya es el siguiente, y el empleado vería el saldo del año que todavía no empezó.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Si no hay empleado actual resuelto. <b>No se degrada a un saldo de cero</b>: un cero y un fallo
    /// se ven igual en pantalla y significan lo contrario — «no te quedan días» frente a «no pudimos
    /// averiguarlo» (AC-07).
    /// </exception>
    public async Task<SaldoDelAnio> DelAnioEnCursoAsync(CancellationToken cancelacion = default)
    {
        var anioEnCurso = _tiempo.GetLocalNow().Year;

        var saldos = await DeLosAniosAsync([anioEnCurso], cancelacion).ConfigureAwait(false);

        return saldos[0];
    }

    /// <summary>
    /// El saldo del empleado actual en cada uno de los años pedidos, en el mismo orden (FR-03, FR-05).
    /// </summary>
    /// <exception cref="ArgumentNullException">Si <paramref name="anios"/> es <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si se piden más de <see cref="MaximoDeAniosPorConsulta"/> años, si los dos que se piden no son
    /// consecutivos, o si alguno cae fuera del rango que admite <see cref="DateOnly"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">Si no hay empleado actual resuelto.</exception>
    /// <exception cref="AccesoASolicitudesDenegadoException">
    /// Si <see cref="PermisosService"/> niega la visibilidad. Tampoco se degrada a cero.
    /// </exception>
    public async Task<IReadOnlyList<SaldoDelAnio>> DeLosAniosAsync(
        IReadOnlyList<int> anios,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(anios);

        // Lo primero: de quién es el saldo, y si quien pregunta puede verlo. Va ANTES del retorno por
        // lista vacía, y el orden es deliberado —el hermano hace lo mismo con el autor del alta—: el
        // único camino en que este servicio contestaba con éxito una pregunta sobre «el empleado actual»
        // sin haber averiguado quién es era justamente ese retorno. Una lista vacía devuelta ante una
        // identidad sin resolver se lee como «no hay saldos que mostrar», y AC-07 existe para que un
        // fallo no se disfrace de dato. Resolver la identidad y preguntarle a PermisosService no cuesta
        // ningún viaje a la base, así que adelantarlo no le saca nada al camino de la lista vacía.
        var quienConsulta = _empleadoActual.Identidad;
        var sujeto = quienConsulta.Id;

        // El saldo de un empleado es tan suyo como su listado, así que la decisión de visibilidad se le
        // pregunta a su única sede en vez de darla por sentada acá. Antes de toda consulta, que es la
        // mitigación 2 de R-13.
        _permisos.ExigirPoderVerLasSolicitudesDe(quienConsulta, sujeto);

        if (anios.Count == 0)
        {
            // Sin años que calcular no hay nada que preguntarle a la base. Devolver vacío en vez de
            // abrir un contexto para no consultar nada mantiene la promesa de que cada viaje a la base
            // corresponde a una pregunta real.
            return [];
        }

        ExigirUnRangoAcotado(anios);

        var desde = new DateOnly(anios.Min(), 1, 1);
        var hasta = new DateOnly(anios.Max(), 12, 31);

        await using var contexto = await _fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        // Solo los períodos que solapan el rango pedido, y solo sus fechas: el reparto por año se hace
        // acá abajo, con la única función que sabe hacerlo.
        //
        // OJO CON LAS DOS ÚLTIMAS CONDICIONES: su valor es de PLAN DE EJECUCIÓN, no de resultado.
        // Quitarlas no cambiaría ningún número —DiasEnElAnio devuelve 0 para lo que no solapa— así que
        // ningún aserto de esta suite se pone rojo si desaparecen. Lo que cambiaría es cuántas filas
        // viajan: sin ellas, calcular el saldo de un año trae el historial completo del empleado y la
        // consulta deja de poder apoyarse en el índice del bloque 5. Sostienen NFR-03 y la parte de R-15
        // que el límite de años no cubre; no las borres porque «no rompen nada».
        var periodos = await contexto.Solicitudes
            .AsNoTracking()
            .Where(solicitud =>
                solicitud.EmpleadoId == sujeto
                && EstadosDeSolicitud.Vigentes.Contains(solicitud.Estado)
                && solicitud.FechaInicio <= hasta
                && solicitud.FechaFin >= desde)
            .Select(solicitud => new { solicitud.FechaInicio, solicitud.FechaFin })
            .ToListAsync(cancelacion)
            .ConfigureAwait(false);

        return [.. anios.Select(anio =>
        {
            var usados = periodos.Sum(periodo =>
                ImputacionPorAnio.DiasEnElAnio(periodo.FechaInicio, periodo.FechaFin, anio));

            return new SaldoDelAnio(anio, usados, Math.Max(0, TopeAnual.Dias - usados));
        })];
    }

    /// <summary>
    /// Exige que los años pedidos abarquen, como mucho, dos años calendario <b>contiguos</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Es un control, no una optimización</b> (mitigación de R-15). El conjunto de años lo determina el
    /// período que escribe el empleado, <see cref="DateOnly"/> llega hasta el año 9999 y los circuitos de
    /// esta aplicación no exigen credenciales.
    /// </para>
    /// <para>
    /// <b>Y por eso no alcanza con contar años: hay que acotar el rango.</b> Lo que la consulta lleva a la
    /// base no es la lista, es el intervalo <c>[1-ene-mínimo, 31-dic-máximo]</c>, así que
    /// <c>[1, 9999]</c> —dos años, guarda de cantidad satisfecha— produce un filtro temporal que no filtra
    /// nada y devuelve el historial entero del empleado. Contiguos es lo que el propio razonamiento del
    /// límite implica: dos es todo lo que necesita un período legítimo <i>porque</i> uno de tres años
    /// contendría un año calendario íntegro, y 365 días no caben en un tope de 14. Un par repetido tampoco
    /// es el conjunto de años de ningún período —devolvería dos veces el mismo saldo—, así que se exige
    /// diferencia de exactamente uno.
    /// </para>
    /// <para>
    /// El orden no se exige: <c>[2027, 2026]</c> es tan acotado como <c>[2026, 2027]</c>, y los resultados
    /// salen en el orden en que se pidieron los años. Lo que se acota es el rango, que es lo que le cuesta
    /// a la base.
    /// </para>
    /// </remarks>
    private static void ExigirUnRangoAcotado(IReadOnlyList<int> anios)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(anios.Count, MaximoDeAniosPorConsulta, nameof(anios));

        foreach (var anio in anios)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(anio, DateOnly.MinValue.Year, nameof(anios));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(anio, DateOnly.MaxValue.Year, nameof(anios));
        }

        if (anios.Count == MaximoDeAniosPorConsulta && Math.Abs(anios[1] - anios[0]) != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(anios),
                $"Los años pedidos son {anios[0]} y {anios[1]}, que no son consecutivos. Un período " +
                "legítimo abarca como mucho dos años calendario contiguos, y el rango que la consulta " +
                "lleva a la base va del 1 de enero del menor al 31 de diciembre del mayor: sin esta " +
                "comprobación, un par como [1, 9999] devolvería el historial completo del empleado.");
        }
    }
}
