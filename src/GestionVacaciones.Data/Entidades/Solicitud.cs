namespace GestionVacaciones.Data.Entidades;

/// <summary>Pedido de vacaciones de un empleado.</summary>
public class Solicitud
{
    public int Id { get; set; }

    public int EmpleadoId { get; set; }

    public DateOnly FechaInicio { get; set; }

    public DateOnly FechaFin { get; set; }

    public int DiasCorridos { get; set; }

    public EstadoSolicitud Estado { get; set; }

    public DateTimeOffset FechaCreacion { get; set; }

    /// <summary>Quién resolvió la solicitud (aprobó o rechazó). <c>null</c> mientras está Pendiente.</summary>
    public int? ResueltoPorId { get; set; }

    /// <summary>
    /// Cuándo se resolvió, con el mismo desplazamiento local que <see cref="FechaCreacion"/>.
    /// <c>null</c> mientras está Pendiente.
    /// </summary>
    public DateTimeOffset? FechaResolucion { get; set; }

    /// <summary>Motivo del rechazo. Solo tiene valor cuando el resultado de la resolución fue un rechazo.</summary>
    public string? MotivoDeRechazo { get; set; }

    public Empleado? Empleado { get; set; }

    /// <summary>Empleado que resolvió la solicitud (el manager o su designado).</summary>
    public Empleado? ResueltoPor { get; set; }
}
