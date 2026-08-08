namespace GestionVacaciones.Data.Services;

/// <summary>
/// Reparte los días de un período entre los <b>años calendario</b> que abarca. Del 28-dic-2026 al
/// 5-ene-2027 son 9 días corridos: 4 de 2026 y 5 de 2027.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué existe.</b> El tope anual es de 14 días «del año en curso» (FR-02) y el saldo se reinicia
/// cada 1 de enero, pero un período a caballo del 31 de diciembre pertenece a dos años a la vez. El PRD
/// resuelve que a cada año le corresponden los días que le pertenecen, y esta es la única sede de esa
/// regla: el bloqueo del alta y el saldo que ve el empleado la consumen los dos.
/// </para>
/// <para>
/// <b>Por qué es <c>static</c>, igual que <see cref="CalculadorDeDiasCorridos"/>.</b> No tiene estado ni
/// dependencias, y un tipo instanciable admitiría un segundo registro en el contenedor —o una segunda
/// implementación tras una interfaz—, que es exactamente lo que NFR-04 cuenta en cero. Sin instancia no
/// hay dos.
/// </para>
/// <para>
/// <b>Por qué delega el conteo en vez de restar por su cuenta.</b> Recortar el período contra el año y
/// después restar los días sería reescribir la fórmula del conteo inclusivo, y con eso volverían a
/// existir dos implementaciones que pueden divergir por un «+ 1». Acá se recorta y se le pregunta a
/// <see cref="CalculadorDeDiasCorridos.Contar"/>, que es la misma que persiste
/// <c>Solicitud.DiasCorridos</c> y la misma que la check constraint
/// <c>CK_Solicitud_DiasCoincidenConPeriodo</c> reproduce en la base.
/// </para>
/// <para>
/// <b>No sabe en qué año estamos.</b> No recibe ni consulta un reloj: el año se le pasa. Quién decide
/// cuál es «el año en curso» es <c>SaldoService</c>, con su <see cref="TimeProvider"/> inyectado, y por
/// eso este tipo se puede ejercitar sin fijar el tiempo.
/// </para>
/// </remarks>
public static class ImputacionPorAnio
{
    /// <summary>
    /// Los días del período que pertenecen a <paramref name="anio"/>. <c>0</c> si el período no lo toca.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si <paramref name="fechaFin"/> es anterior a <paramref name="fechaInicio"/>, o si
    /// <paramref name="anio"/> cae fuera del rango que admite <see cref="DateOnly"/>.
    /// <para>
    /// Un período invertido no tiene días que imputar, por la misma razón que no tiene días corridos: no
    /// es una cantidad negativa, es una precondición incumplida del llamador. La precondición es
    /// deliberadamente idéntica a la de <see cref="CalculadorDeDiasCorridos.Contar"/>, porque dos
    /// hermanas que se comportan distinto ante la misma entrada inválida son una trampa.
    /// </para>
    /// </exception>
    public static int DiasEnElAnio(DateOnly fechaInicio, DateOnly fechaFin, int anio)
    {
        if (fechaFin < fechaInicio)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fechaFin),
                "La fecha de fin es anterior a la fecha de inicio, así que el período no tiene días " +
                "que imputar. El orden de las fechas se valida antes de llegar acá.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(anio, DateOnly.MinValue.Year, nameof(anio));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(anio, DateOnly.MaxValue.Year, nameof(anio));

        // El recorte del período contra el año. Si lo que queda está invertido, es que no se tocan.
        var primerDiaDelAnio = new DateOnly(anio, 1, 1);
        var ultimoDiaDelAnio = new DateOnly(anio, 12, 31);

        var desde = fechaInicio > primerDiaDelAnio ? fechaInicio : primerDiaDelAnio;
        var hasta = fechaFin < ultimoDiaDelAnio ? fechaFin : ultimoDiaDelAnio;

        if (hasta < desde)
        {
            // Sin solapamiento. Es una respuesta legítima —«a ese año no le toca ninguno»— y no un
            // error: preguntar por el saldo de un año que el período no abarca es normal.
            return 0;
        }

        // El mismo conteo que se muestra en pantalla y que se persiste. Ver el remarks del tipo.
        return CalculadorDeDiasCorridos.Contar(desde, hasta);
    }

    /// <summary>
    /// Los años calendario que toca el período, en orden ascendente. Para un período dentro de un solo
    /// año devuelve ese único año.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si <paramref name="fechaFin"/> es anterior a <paramref name="fechaInicio"/>. Misma precondición
    /// que <see cref="DiasEnElAnio"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Obligación del llamador, no promesa de esta función.</b> Un período largo devuelve muchos años
    /// —es entrada de usuario y <see cref="DateOnly"/> llega hasta el 9999, así que del año 1 al 9999 son
    /// casi diez mil enteros—, y acá no se acota nada: esta función no sabe del tope anual y devolver «los
    /// años que toca el período» es exactamente su contrato.
    /// </para>
    /// <para>
    /// <b>Quién tiene que acotar, y dónde.</b> El primer llamador de <c>src/</c> va a ser
    /// <c>SolicitudesService.CrearAsync</c> en el <b>bloque 3</b> de este ticket, y le corresponde
    /// rechazar en memoria un período que abarque más de dos años <i>antes</i> de consultar la base: uno de
    /// tres años contiene un año calendario íntegro, y 365 días no caben en un tope de 14, así que ese
    /// rechazo no necesita saber ningún saldo. El límite paralelo del lado del saldo ya existe y vive en
    /// otro tipo: <c>SaldoService.MaximoDeAniosPorConsulta</c>. Los dos son mitigaciones de <b>R-15</b>, y
    /// ninguno está acá.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<int> AniosAbarcados(DateOnly fechaInicio, DateOnly fechaFin)
    {
        if (fechaFin < fechaInicio)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fechaFin),
                "La fecha de fin es anterior a la fecha de inicio, así que el período no abarca ningún " +
                "año. El orden de las fechas se valida antes de llegar acá.");
        }

        var anios = new List<int>(fechaFin.Year - fechaInicio.Year + 1);

        for (var anio = fechaInicio.Year; anio <= fechaFin.Year; anio++)
        {
            anios.Add(anio);
        }

        return anios;
    }
}
