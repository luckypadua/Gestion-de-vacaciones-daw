namespace GestionVacaciones.Data.Services;

/// <summary>
/// Cuenta los <b>días corridos</b> de un período: los del calendario, no los hábiles, y de forma
/// <b>inclusiva</b>. Del 3-ene al 5-ene son 3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Punto único del cálculo.</b> El PRD registra como riesgo que la interfaz muestre un número y el
/// servicio persista otro; la mitigación es que el cálculo viva en el dominio y la interfaz lo
/// <b>consuma</b> en lugar de reimplementarlo. La tercera check constraint del Bloque 2
/// —<c>CK_Solicitud_DiasCoincidenConPeriodo</c>, que compara contra <c>DATEDIFF(day, …) + 1</c>— es la
/// misma regla escrita en la base, y es la que vuelve imposible persistir una discrepancia.
/// </para>
/// <para>
/// <b>Por qué es <c>static</c> y no un servicio inyectable.</b> No tiene estado ni dependencias, y
/// <c>AGENTS.md</c> pide que las reglas de cálculo vivan en un punto único. Un tipo instanciable
/// admitiría un segundo registro en el contenedor —o una segunda implementación tras una interfaz— y con
/// eso volvería a existir la posibilidad de que el formulario y el servicio cuenten distinto, que es
/// exactamente el riesgo que este tipo cierra. Sin instancia no hay dos.
/// </para>
/// <para>
/// El nombre habla de «días corridos» y no de «días» por el glosario de <c>AGENTS.md</c>: la unidad del
/// dominio es esa, y el día que exista una regla de días hábiles va a ser otro tipo con otro nombre, no
/// una variante de este.
/// </para>
/// </remarks>
public static class CalculadorDeDiasCorridos
{
    /// <summary>
    /// Días corridos entre <paramref name="fechaInicio"/> y <paramref name="fechaFin"/>, contando ambos
    /// extremos. Un período de un solo día es 1.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si <paramref name="fechaFin"/> es anterior a <paramref name="fechaInicio"/>.
    /// <para>
    /// <b>Por qué lanza en vez de devolver 0 o un negativo.</b> Cualquiera de los dos sería un número
    /// mostrable en pantalla y persistible en la columna, y el rechazo llegaría recién desde las check
    /// constraints de la base, o sea como un error de la aplicación y no del período. Un período
    /// invertido no tiene días corridos: no es una cantidad negativa, es una precondición incumplida.
    /// </para>
    /// <para>
    /// Esto <b>no</b> le devuelve la comparación de fechas a la interfaz (R-10): el orden de las dos
    /// fechas lo decide <see cref="SolicitudesService"/>, que valida <b>antes</b> de contar y por eso
    /// nunca llega hasta acá con un período invertido. Quien muestre el número calculado tiene el mismo
    /// deber: pedirlo recién cuando el período está en orden.
    /// </para>
    /// </exception>
    public static int Contar(DateOnly fechaInicio, DateOnly fechaFin)
    {
        if (fechaFin < fechaInicio)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fechaFin),
                "La fecha de fin es anterior a la fecha de inicio, así que el período no tiene días " +
                "corridos que contar. El orden de las fechas se valida antes de llegar acá: si esta " +
                "excepción aparece, hay un llamador que se saltó esa validación.");
        }

        // DayNumber es el día absoluto del calendario propléptico gregoriano, así que la diferencia no
        // necesita saber nada de meses ni de años bisiestos. El «+ 1» es el conteo inclusivo, y es la
        // misma fórmula que la check constraint del Bloque 2.
        return fechaFin.DayNumber - fechaInicio.DayNumber + 1;
    }
}
