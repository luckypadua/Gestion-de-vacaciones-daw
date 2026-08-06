namespace GestionVacaciones.Data.Services;

/// <summary>
/// El tope de días de vacaciones por empleado y por año calendario: <b>14</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué un tipo entero para un solo número.</b> NFR-01 exige que cambiar el tope requiera
/// modificar exactamente <b>una</b> declaración. Un <c>14</c> escrito en el servicio que bloquea y otro
/// en el que calcula el saldo son dos números que hoy coinciden y que el día que la normativa cambie
/// van a dejar de coincidir en el peor momento posible: el empleado vería un saldo y el sistema lo
/// bloquearía contra otro. Un test de escaneo sobre todo <c>src/</c> verifica que esta es la única
/// aparición del número.
/// </para>
/// <para>
/// <b>Sin prorrateo y sin arrastre.</b> El tope no depende de la fecha de ingreso ni acumula lo no
/// usado del año anterior: el PRD los pone a los dos fuera de alcance, explícitamente. Si algún día
/// entran, entran acá — y esa es justamente la razón de que este tipo exista antes de necesitarlo.
/// </para>
/// <para>
/// <b>No es un dato personal.</b> El tope es política de la empresa, igual para todos, así que puede
/// aparecer en un mensaje o en un log sin ninguna precaución. El <i>saldo</i>, en cambio, sí dice algo
/// sobre una persona concreta: cuánto se ausentó y cuánto puede ausentarse. Los dos son números de días
/// y se parecen; no son la misma clase de cosa.
/// </para>
/// </remarks>
public static class TopeAnual
{
    /// <summary>
    /// Días de vacaciones por empleado y año calendario. <b>La única declaración de este número en
    /// todo <c>src/</c></b> (NFR-01).
    /// </summary>
    public const int Dias = 14;
}
