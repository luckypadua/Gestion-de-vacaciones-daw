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

    public Empleado? Empleado { get; set; }
}
