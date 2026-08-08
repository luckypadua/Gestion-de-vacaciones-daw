using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;

namespace GestionVacaciones.Data;

/// <summary>
/// Contexto de persistencia de la aplicación. Se registra <b>siempre</b> con
/// <c>AddDbContextFactory</c> y <b>nunca</b> con <c>AddDbContext</c> (NFR-05): en Blazor Server un
/// contexto scoped vive todo el circuito, y dos componentes que consultan a la vez lo usan
/// concurrentemente hasta que EF lanza «A second operation was started on this context instance».
/// <c>AGENTS.md</c> lo registra como cicatriz del proyecto.
/// </summary>
public class VacacionesDbContext : DbContext
{
    /// <summary>Índice que sirve al listado propio de FR-04.</summary>
    public const string IndiceDelListado = "IX_Solicitud_EmpleadoId_FechaCreacion";

    /// <summary>Índice único del correo, que identifica a la persona en la nómina.</summary>
    public const string IndiceDeCorreoUnico = "IX_Empleado_Correo";

    public const string PeriodoCoherente = "CK_Solicitud_PeriodoCoherente";
    public const string DiasPositivos = "CK_Solicitud_DiasPositivos";
    public const string DiasCoincidenConPeriodo = "CK_Solicitud_DiasCoincidenConPeriodo";
    public const string EstadoValido = "CK_Solicitud_EstadoValido";

    /// <summary>Longitud máxima del nombre del empleado.</summary>
    private const int LargoDelNombre = 200;

    /// <summary>
    /// Longitud máxima de una dirección de correo: 64 de parte local + «@» + 255 de dominio, según
    /// el límite que fija el RFC 5321 para el camino de reversa.
    /// </summary>
    private const int LargoDelCorreo = 320;

    public VacacionesDbContext(DbContextOptions<VacacionesDbContext> opciones)
        : base(opciones)
    {
    }

    public DbSet<Empleado> Empleados => Set<Empleado>();

    public DbSet<Solicitud> Solicitudes => Set<Solicitud>();

    protected override void OnModelCreating(ModelBuilder constructor)
    {
        ArgumentNullException.ThrowIfNull(constructor);

        ConfigurarEmpleado(constructor);
        ConfigurarSolicitud(constructor);
    }

    private static void ConfigurarEmpleado(ModelBuilder constructor) =>
        constructor.Entity<Empleado>(entidad =>
        {
            entidad.HasKey(empleado => empleado.Id);

            entidad.Property(empleado => empleado.Nombre)
                .IsRequired()
                .HasMaxLength(LargoDelNombre);

            entidad.Property(empleado => empleado.Correo)
                .IsRequired()
                .HasMaxLength(LargoDelCorreo);

            // El correo identifica a la persona en la nómina que alimenta el selector de identidad:
            // duplicarlo crearía dos identidades indistinguibles entre sí.
            entidad.HasIndex(empleado => empleado.Correo)
                .IsUnique()
                .HasDatabaseName(IndiceDeCorreoUnico);

            // Las dos son autorreferencias, y el borrado en cascada es OBLIGATORIO evitarlo: SQL
            // Server rechaza crear una FK cíclica en cascada («may cause cycles or multiple cascade
            // paths») y la migración no se podría ni aplicar. `WithMany()` sin navegación inversa es
            // deliberado: con dos relaciones hacia el mismo tipo, dejar que EF empareje por
            // convención produce el error de «no se pudo determinar el lado dependiente».
            entidad.HasOne(empleado => empleado.Manager)
                .WithMany()
                .HasForeignKey(empleado => empleado.ManagerId)
                .OnDelete(DeleteBehavior.NoAction);

            entidad.HasOne(empleado => empleado.Designado)
                .WithMany()
                .HasForeignKey(empleado => empleado.DesignadoId)
                .OnDelete(DeleteBehavior.NoAction);
        });

    private static void ConfigurarSolicitud(ModelBuilder constructor) =>
        constructor.Entity<Solicitud>(entidad =>
        {
            entidad.HasKey(solicitud => solicitud.Id);

            entidad.Property(solicitud => solicitud.EmpleadoId).IsRequired();
            entidad.Property(solicitud => solicitud.FechaInicio).IsRequired();
            entidad.Property(solicitud => solicitud.FechaFin).IsRequired();
            entidad.Property(solicitud => solicitud.DiasCorridos).IsRequired();

            // El estado se persiste como int, no como texto: es lo que hace comprobable el rango en
            // CK_Solicitud_EstadoValido, y lo que obliga a que esa constraint exista.
            entidad.Property(solicitud => solicitud.Estado)
                .IsRequired()
                .HasConversion<int>();

            entidad.Property(solicitud => solicitud.FechaCreacion).IsRequired();

            // El historial de una persona no se borra en silencio al darla de baja: la trazabilidad
            // de quién pidió qué y cuándo es parte del dominio. Si alguna vez hay que eliminar un
            // empleado, sus solicitudes se resuelven antes, a mano y a la vista.
            entidad.HasOne(solicitud => solicitud.Empleado)
                .WithMany()
                .HasForeignKey(solicitud => solicitud.EmpleadoId)
                .OnDelete(DeleteBehavior.NoAction);

            // El índice del listado de FR-04: filtra por empleado y ordena descendente por fecha de
            // creación. Sin la dirección descendente el índice existe pero el motor ordena aparte,
            // que es justo el escaneo que NFR-01 quiere evitar.
            entidad.HasIndex(solicitud => new { solicitud.EmpleadoId, solicitud.FechaCreacion })
                .HasDatabaseName(IndiceDelListado)
                .IsDescending(false, true);

            ConfigurarInvariantesDeNFR04(entidad);
        });

    /// <summary>
    /// Las cuatro check constraints que exige NFR-04. Son <b>defensa en profundidad</b>: la
    /// validación de negocio vive en los servicios, y si una fila inválida llega hasta acá es que
    /// aquella falló. La diferencia entre validar solo en C# y validar también acá es que un bug en
    /// C# —o una carga externa, o un script— no puede corromper la invariante.
    /// </summary>
    /// <remarks>
    /// Tres de las cuatro no son aislables entre sí: <see cref="PeriodoCoherente"/> se deduce de las
    /// otras dos y <see cref="DiasPositivos"/> también. Se escriben las tres igual porque cada una
    /// nombra su invariante, y el mensaje de SQL Server que llega al log dice cuál se violó.
    /// </remarks>
    private static void ConfigurarInvariantesDeNFR04(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<Solicitud> entidad) =>
        entidad.ToTable(tabla =>
        {
            tabla.HasCheckConstraint(PeriodoCoherente, "[FechaFin] >= [FechaInicio]");

            tabla.HasCheckConstraint(DiasPositivos, "[DiasCorridos] > 0");

            // La que impide la incoherencia entre lo que la interfaz muestra (AC-01) y lo que se
            // guarda (AC-04): sin ella se puede persistir 3-ene → 5-ene con DiasCorridos = 99.
            tabla.HasCheckConstraint(
                DiasCoincidenConPeriodo,
                "[DiasCorridos] = DATEDIFF(day, [FechaInicio], [FechaFin]) + 1");

            // Cubre el hueco que dejan las otras tres: protegen el período, ninguna protegía el
            // estado. El rango literal está atado al enum por un test —si aparece un cuarto estado
            // sin tocar esta línea, la suite lo dice acá y no en producción—.
            tabla.HasCheckConstraint(EstadoValido, "[Estado] BETWEEN 0 AND 2");
        });
}
