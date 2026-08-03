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
/// <b>El PRD entrecomilla tres literales y acá hay dos.</b> El tercero es <c>"Pendiente"</c> (AC-04),
/// que no es un mensaje sino el nombre del estado en el que nace toda solicitud: lo fija
/// <c>EstadoSolicitud</c> en el modelo del Bloque 2, y duplicarlo acá como texto crearía justamente la
/// segunda copia que este tipo existe para evitar.
/// </para>
/// </remarks>
public static class ErroresDeSolicitud
{
    /// <summary>AC-02. Literal del PRD: no se reformula, no se le agrega puntuación.</summary>
    public const string FechaDeInicioAnteriorAHoy = "La fecha de inicio no puede ser anterior a hoy";

    /// <summary>AC-03. Literal del PRD: no se reformula, no se le agrega puntuación.</summary>
    public const string FechaDeFinAnteriorALaFechaDeInicio =
        "La fecha de fin no puede ser anterior a la fecha de inicio";
}
