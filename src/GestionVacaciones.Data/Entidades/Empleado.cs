namespace GestionVacaciones.Data.Entidades;

/// <summary>Persona de la nómina.</summary>
public class Empleado
{
    public int Id { get; set; }

    public required string Nombre { get; set; }

    public required string Correo { get; set; }

    public int? ManagerId { get; set; }

    public int? DesignadoId { get; set; }

    public Empleado? Manager { get; set; }

    public Empleado? Designado { get; set; }
}
