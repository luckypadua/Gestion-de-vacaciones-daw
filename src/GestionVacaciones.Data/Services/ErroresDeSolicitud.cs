using System.Globalization;

namespace GestionVacaciones.Data.Services;

/// <summary>
/// Los mensajes de validación que <b>ve el empleado</b>, tal como los fija el PRD, carácter por
/// carácter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué son constantes y no cadenas escritas donde se usan.</b> <c>AGENTS.md</c> lo exige: «los
/// mensajes de validación que ve el usuario son los literales definidos en los criterios de aceptación
/// del PRD». AC-02 y AC-03 son criterios sobre el <b>texto exacto</b>, así que el texto es parte del
/// contrato y no una cadena de presentación: escrito dos veces —una en el servicio y otra en el
/// componente— basta una tilde de diferencia para que uno de los dos deje de cumplir el criterio, y las
/// dos versiones se ven idénticas en una revisión.
/// </para>
/// <para>
/// Que coinciden con el documento y no solo entre sí lo verifica un test que lee el PRD versionado y
/// compara los literales entrecomillados de su sección de criterios de aceptación.
/// </para>
/// <para>
/// <b>El PRD de FEAT-001a entrecomilla tres literales y acá hay dos suyos.</b> El tercero es
/// <c>"Pendiente"</c> (AC-04), que no es un mensaje sino el nombre del estado en el que nace toda
/// solicitud: lo fija <c>EstadoSolicitud</c> en el modelo, y duplicarlo acá como texto crearía
/// justamente la segunda copia que este tipo existe para evitar.
/// </para>
/// <para>
/// <b>Por qué los dos mensajes de FEAT-001b se componen y los de FEAT-001a no.</b> Aquellos son texto
/// fijo; estos llevan el saldo del empleado, así que la constante es el <i>formato</i> y el texto final
/// se arma acá. Que se arme en un único lugar no es comodidad: el saldo de una persona identificable es
/// PII, y con un <c>string.Format</c> disperso por el código no habría un solo lugar donde comprobarlo.
/// </para>
/// <para>
/// <b>Qué mira cada guardarraíl, dicho con precisión.</b> El de diagnósticos sin PII
/// —<c>DiagnosticoSinPiiTests</c>— lee por reflexión <b>solo las constantes</b>: ve «…de {0} días» y lo
/// aprueba, así que por sí solo <i>no</i> cubre el texto compuesto, que es el único que lleva el número
/// real. Lo que cubre esa mitad es
/// <c>MensajesLiteralesDeFeat001bTests.Los_mensajes_compuestos_pasan_por_el_criterio_del_guardarrail_de_diagnosticos</c>,
/// que ejecuta los dos compositores con centinelas, les aplica el mismo criterio de dígitos y afirma que
/// los únicos números de la salida son el saldo y los años. Componer en un solo lugar es lo que hace que
/// ese control sea posible; no es el control.
/// </para>
/// </remarks>
public static class ErroresDeSolicitud
{
    /// <summary>AC-02. Literal del PRD: no se reformula, no se le agrega puntuación.</summary>
    public const string FechaDeInicioAnteriorAHoy = "La fecha de inicio no puede ser anterior a hoy";

    /// <summary>AC-03. Literal del PRD: no se reformula, no se le agrega puntuación.</summary>
    public const string FechaDeFinAnteriorALaFechaDeInicio =
        "La fecha de fin no puede ser anterior a la fecha de inicio";

    /// <summary>
    /// FEAT-001c AC-01. Literal del PRD: no se reformula, no se le agrega puntuación. A diferencia de
    /// los dos mensajes de FEAT-001b, no lleva ningún dato de la persona —es igual para cualquier
    /// empleado—, así que se construye con <see cref="ResultadoDelAlta.Rechazada"/> y no con
    /// <see cref="ResultadoDelAlta.RechazadaPorSaldoInsuficiente"/>.
    /// </summary>
    public const string SuperposicionDePeriodo =
        "Ya tenés una solicitud que se superpone con estas fechas";

    /// <summary>
    /// FEAT-002 AC-03. Literal del PRD: no se reformula, no se le agrega puntuación. Lo devuelve
    /// <see cref="SolicitudesService.ResolverAsync"/> cuando se intenta rechazar sin indicar un motivo.
    /// </summary>
    public const string MotivoDeRechazoObligatorio = "Indicá el motivo del rechazo";

    /// <summary>
    /// FEAT-002 AC-04. Literal del PRD: no se reformula, no se le agrega puntuación. Lo devuelve
    /// <see cref="SolicitudesService.ResolverAsync"/> cuando la solicitud ya no está en estado
    /// <c>Pendiente</c> —por el camino directo o por el reintento de R-25—.
    /// </summary>
    public const string SolicitudYaResuelta = "Esta solicitud ya fue resuelta";

    /// <summary>
    /// FEAT-001b AC-02, con el saldo del único año afectado. Literal del PRD, donde el marcador se
    /// escribe <c>X</c>.
    /// </summary>
    public const string SaldoInsuficiente =
        "No dispones de días suficientes. Tu saldo actual es de {0} días";

    /// <summary>
    /// FEAT-001b AC-03: el mismo mensaje desglosado, para un período a caballo de dos años. Literal del
    /// PRD, donde los marcadores se escriben <c>X</c>, <c>{año1}</c>, <c>Y</c> y <c>{año2}</c>.
    /// </summary>
    /// <remarks>
    /// Coincide con <see cref="SaldoInsuficiente"/> palabra por palabra hasta «Tu saldo actual es de X
    /// días», y eso es deliberado: no son dos mensajes distintos sino el mismo, desglosado por año.
    /// </remarks>
    public const string SaldoInsuficienteDosAnios =
        "No dispones de días suficientes. Tu saldo actual es de {0} días en {1} y de {2} días en {3}";

    /// <summary>El mensaje de AC-02, con el saldo del año afectado.</summary>
    /// <param name="diasDisponibles">Días que le quedan al empleado en el año afectado.</param>
    /// <remarks>
    /// <b>Con <see cref="CultureInfo.InvariantCulture"/>, y el motivo no es el separador de millares.</b>
    /// Un <c>int</c> formateado con <c>{0}</c> usa el formato «G», que no inserta separador de grupo en
    /// ninguna cultura: no existe una donde un año salga «2.026». Lo único culturalmente sensible de un
    /// entero es el <b>signo negativo</b>, y hay culturas —<c>sv-SE</c> y <c>lt-LT</c>— que usan el signo
    /// menos U+2212 en lugar del guion ASCII. El saldo está acotado a 0, así que hoy no puede salir
    /// negativo; el día que este compositor reciba una diferencia sin acotar, el mensaje no va a cambiar
    /// de forma según cómo esté configurada la máquina que lo compuso.
    /// </remarks>
    public static string ComponerSaldoInsuficiente(int diasDisponibles) =>
        string.Format(CultureInfo.InvariantCulture, SaldoInsuficiente, diasDisponibles);

    /// <summary>
    /// El mensaje desglosado de AC-03, con los dos años en el orden en que se los pasa.
    /// </summary>
    /// <param name="primero">Saldo del primer año que se nombra en el mensaje.</param>
    /// <param name="segundo">Saldo del segundo año que se nombra en el mensaje.</param>
    /// <remarks>
    /// <b>Por qué recibe dos <see cref="SaldoDelAnio"/> y no cuatro enteros, y por qué no es una
    /// sobrecarga.</b> Cuatro <c>int</c> que intercalan días y años —<c>(dias, anio, dias, anio)</c>— se
    /// pasan mal con total naturalidad: quien tiene dos saldos a mano escribe
    /// <c>(dias1, dias2, anio1, anio2)</c>, eso compila sin una sola advertencia y produce «Tu saldo actual
    /// es de 2026 días en 7». Con el tipo que ya transporta las dos cosas juntas, ese error deja de ser
    /// expresable. Y el nombre distinto en vez de una sobrecarga por aridad empareja además la
    /// nomenclatura de las constantes: <see cref="SaldoInsuficiente"/> /
    /// <see cref="SaldoInsuficienteDosAnios"/>. Sobre <see cref="CultureInfo.InvariantCulture"/>, ver
    /// <see cref="ComponerSaldoInsuficiente(int)"/>.
    /// </remarks>
    public static string ComponerSaldoInsuficienteDeDosAnios(SaldoDelAnio primero, SaldoDelAnio segundo)
    {
        ArgumentNullException.ThrowIfNull(primero);
        ArgumentNullException.ThrowIfNull(segundo);

        return string.Format(
            CultureInfo.InvariantCulture,
            SaldoInsuficienteDosAnios,
            primero.DiasDisponibles, primero.Anio,
            segundo.DiasDisponibles, segundo.Anio);
    }
}
