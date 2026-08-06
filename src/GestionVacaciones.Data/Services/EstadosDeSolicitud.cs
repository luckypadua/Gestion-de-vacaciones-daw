using GestionVacaciones.Data.Entidades;

namespace GestionVacaciones.Data.Services;

/// <summary>
/// Los estados de una <c>Solicitud</c> que <b>cuentan</b>: reservan saldo y bloquean la superposición
/// de otro período del mismo empleado. <b>Pendiente</b> y <b>Aprobada</b>, nunca <b>Rechazada</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué existe, igual que <see cref="TopeAnual"/>.</b> Hasta FEAT-001c el par
/// <c>{Pendiente, Aprobada}</c> era un campo privado de <see cref="SaldoService"/>. FEAT-001c necesita
/// exactamente la misma pregunta —¿qué estados de otra solicitud del empleado cuentan?— para decidir la
/// superposición, y la alternativa de escribir el par una segunda vez en
/// <see cref="SolicitudesService"/> es justamente el riesgo que el PRD registra como primero: dos
/// literales que hoy coinciden y que un tercer estado, el día que aparezca, podría dejar de actualizar
/// en uno de los dos lugares sin que nada avise.
/// </para>
/// <para>
/// <b>Sin dueño operativo</b>, como <see cref="TopeAnual"/>: no depende de nada, no abre ningún
/// contexto, y <see cref="SaldoService"/> y <see cref="SolicitudesService"/> lo consumen por igual, cada
/// uno con su propia pregunta —cuánto saldo reserva, qué otra solicitud bloquea— sobre la misma lista.
/// </para>
/// </remarks>
public static class EstadosDeSolicitud
{
    /// <summary>
    /// Los dos estados vigentes, en el orden en que <see cref="EstadoSolicitud"/> los declara. La
    /// <b>única</b> declaración de este par en todo <c>src/</c>.
    /// </summary>
    public static readonly IReadOnlyList<EstadoSolicitud> Vigentes =
        [EstadoSolicitud.Pendiente, EstadoSolicitud.Aprobada];
}
