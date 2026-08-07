using System.Data.Common;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// FEAT-002, Bloque 4: <see cref="SolicitudesService.ListarPendientesDelEquipoAsync"/> — el listado que
/// alimenta la pantalla de autorizaciones del Bloque 5 (FR-05, AC-07).
/// </summary>
/// <remarks>
/// La mayoría necesita la instancia SQL Server 2022 del entorno: lo que se afirma es el resultado de un
/// filtro y un orden que resuelve el motor sobre el organigrama real (Bloque 2). Los dos casos que fijan
/// el criterio de «nunca se degrada» —<see cref="Un_fallo_de_persistencia_al_listar_el_equipo_se_propaga"/>
/// y <see cref="Sin_identidad_el_listado_no_se_degrada_a_vacio"/>— y
/// <see cref="Quien_no_tiene_equipo_recibe_una_lista_vacia_sin_consultar_solicitudes"/> usan
/// <see cref="FabricaQueNadieDebeUsar"/>, mismo patrón que <c>AutorizacionDeResolucionTests</c> y
/// <c>ResolucionDeSolicitudTests</c>.
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class ListadoDelEquipoTests
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 7, 9, 15, 30, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public ListadoDelEquipoTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_manager_ve_las_pendientes_de_su_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var inicio = new DateOnly(2026, 9, 1);
        var fin = new DateOnly(2026, 9, 3);
        await SembrarAsync(
            (subordinado.Id, inicio, fin, EstadoSolicitud.Pendiente, _instanteFijo));

        var servicio = ServicioPara(manager.Id);

        var listado = await servicio.ListarPendientesDelEquipoAsync(Cancelacion);

        var vista = Assert.Single(listado);
        Assert.Equal(subordinado.Id, vista.EmpleadoId);
        Assert.Equal(inicio, vista.FechaInicio);
        Assert.Equal(fin, vista.FechaFin);
        Assert.Equal(3, vista.DiasCorridos);
        Assert.Equal(_instanteFijo, vista.FechaCreacion);
        Assert.False(string.IsNullOrWhiteSpace(vista.NombreDelEmpleado));
    }

    [Fact]
    public async Task El_designado_ve_las_pendientes_del_equipo_de_su_manager()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var designado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerYDesignadoAsync(subordinado.Id, manager.Id, designado.Id);

        await SembrarAsync(
            (subordinado.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), EstadoSolicitud.Pendiente, _instanteFijo));

        var servicio = ServicioPara(designado.Id);

        var listado = await servicio.ListarPendientesDelEquipoAsync(Cancelacion);

        var vista = Assert.Single(listado);
        Assert.Equal(subordinado.Id, vista.EmpleadoId);
    }

    [Fact]
    public async Task El_listado_no_incluye_aprobadas_ni_rechazadas()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        await SembrarAsync(
            (subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), EstadoSolicitud.Pendiente, _instanteFijo),
            (subordinado.Id, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 12), EstadoSolicitud.Aprobada, _instanteFijo),
            (subordinado.Id, new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 21), EstadoSolicitud.Rechazada, _instanteFijo));

        var servicio = ServicioPara(manager.Id);

        var listado = await servicio.ListarPendientesDelEquipoAsync(Cancelacion);

        var vista = Assert.Single(listado);
        Assert.Equal(new DateOnly(2026, 9, 1), vista.FechaInicio);
    }

    [Fact]
    public async Task Quien_no_tiene_equipo_recibe_una_lista_vacia_sin_consultar_solicitudes()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var solo = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // PermisosService consulta el organigrama de verdad (fábrica real): el equipo vacío es la
        // respuesta legítima de quien no tiene autoridad sobre nadie. La fábrica PROPIA del servicio
        // revienta si se llega a usar: si el equipo vacío no cortara el camino ANTES de abrir un
        // contexto para Solicitud, este test fallaría con la marca de FabricaQueNadieDebeUsar.
        var fabricaReal = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var identidad = IdentidadDePrueba.De(solo.Id);
        var permisos = new PermisosService(fabricaReal);
        var tiempo = new TiempoFijo(_instanteFijo);
        var saldo = new SaldoService(fabricaReal, identidad, permisos, tiempo);

        var servicio = new SolicitudesService(
            new FabricaQueNadieDebeUsar(), identidad, permisos, tiempo, saldo, NuevoRegistro());

        var listado = await servicio.ListarPendientesDelEquipoAsync(Cancelacion);

        Assert.Empty(listado);
    }

    [Fact]
    public async Task El_listado_no_muestra_solicitudes_de_otro_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var managerA = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinadoA = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var managerB = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinadoB = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinadoA.Id, managerA.Id);
        await AsignarManagerAsync(subordinadoB.Id, managerB.Id);

        await SembrarAsync(
            (subordinadoA.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), EstadoSolicitud.Pendiente, _instanteFijo),
            (subordinadoB.Id, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 6), EstadoSolicitud.Pendiente, _instanteFijo));

        var servicio = ServicioPara(managerA.Id);

        var listado = await servicio.ListarPendientesDelEquipoAsync(Cancelacion);

        var vista = Assert.Single(listado);
        Assert.Equal(subordinadoA.Id, vista.EmpleadoId);
        Assert.DoesNotContain(listado, solicitud => solicitud.EmpleadoId == subordinadoB.Id);
    }

    [Fact]
    public async Task El_orden_es_por_fecha_de_creacion_ascendente()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var laDelMedio = new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.FromHours(-3));
        var laMasNueva = new DateTimeOffset(2026, 6, 20, 8, 30, 0, TimeSpan.FromHours(-3));
        var laMasVieja = new DateTimeOffset(2026, 4, 11, 17, 45, 0, TimeSpan.FromHours(-3));

        // Sembradas en desorden: si el servicio no ordenara, la base podría devolverlas en el orden de
        // inserción y un test que las cargara ya ordenadas pasaría igual.
        await SembrarAsync(
            (subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), EstadoSolicitud.Pendiente, laDelMedio),
            (subordinado.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), EstadoSolicitud.Pendiente, laMasNueva),
            (subordinado.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6), EstadoSolicitud.Pendiente, laMasVieja));

        var servicio = ServicioPara(manager.Id);

        var listado = await servicio.ListarPendientesDelEquipoAsync(Cancelacion);

        // Ascendente: la más antigua primero — a diferencia de ListarPropiasAsync, que ordena descendente.
        Assert.Equal([laMasVieja, laDelMedio, laMasNueva], listado.Select(solicitud => solicitud.FechaCreacion));
    }

    [Fact]
    public async Task Un_fallo_de_persistencia_al_listar_el_equipo_se_propaga()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        await SembrarAsync(
            (subordinado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2), EstadoSolicitud.Pendiente, _instanteFijo));

        var servicio = ServicioConFabricaEspecial(
            manager.Id,
            comando => comando.Contains("Solicitudes", StringComparison.OrdinalIgnoreCase));

        var excepcion = await Record.ExceptionAsync(() => servicio.ListarPendientesDelEquipoAsync(Cancelacion));

        Assert.NotNull(excepcion);
        Assert.Contains(InterceptorDeFalloInducido.Marca, excepcion.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sin_identidad_el_listado_no_se_degrada_a_vacio()
    {
        // Sin abrir ningún contexto: SinEmpleadoSeleccionadoException sale de
        // PermisosService.EmpleadosBajoAutoridadDeAsync antes de que nadie pregunte por el organigrama.
        var fabrica = new FabricaQueNadieDebeUsar();
        var identidad = IdentidadDePrueba.SinSeleccionar();
        var permisos = new PermisosService(fabrica);
        var tiempo = new TiempoFijo(_instanteFijo);
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempo);

        var servicio = new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, NuevoRegistro());

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(
            () => servicio.ListarPendientesDelEquipoAsync(Cancelacion));
    }

    private async Task SembrarAsync(
        params (int EmpleadoId, DateOnly Inicio, DateOnly Fin, EstadoSolicitud Estado, DateTimeOffset Creacion)[] solicitudes)
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        foreach (var (empleadoId, inicio, fin, estado, creacion) in solicitudes)
        {
            contexto.Solicitudes.Add(new Solicitud
            {
                EmpleadoId = empleadoId,
                FechaInicio = inicio,
                FechaFin = fin,
                DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
                Estado = estado,
                FechaCreacion = creacion,

                // CK_Solicitud_ResolucionCoherente exige estos tres datos cuando el estado no es
                // Pendiente. Quién "resolvió" no es lo que este bloque verifica, así que se reutiliza
                // el propio empleado.
                ResueltoPorId = estado == EstadoSolicitud.Pendiente ? null : empleadoId,
                FechaResolucion = estado == EstadoSolicitud.Pendiente ? null : creacion,
                MotivoDeRechazo = estado == EstadoSolicitud.Rechazada ? "Motivo de prueba" : null,
            });
        }

        await contexto.SaveChangesAsync(Cancelacion);
    }

    private async Task AsignarManagerAsync(int empleadoId, int managerId)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        var empleado = await contexto.Empleados
            .SingleAsync(candidato => candidato.Id == empleadoId, Cancelacion);
        empleado.ManagerId = managerId;
        await contexto.SaveChangesAsync(Cancelacion);
    }

    private async Task AsignarManagerYDesignadoAsync(int empleadoId, int managerId, int designadoId)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        var empleado = await contexto.Empleados
            .SingleAsync(candidato => candidato.Id == empleadoId, Cancelacion);
        empleado.ManagerId = managerId;
        empleado.DesignadoId = designadoId;
        await contexto.SaveChangesAsync(Cancelacion);
    }

    private SolicitudesService ServicioPara(int empleadoId)
    {
        var fabrica = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService(fabrica);
        var tiempo = new TiempoFijo(_instanteFijo);
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempo);

        return new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, NuevoRegistro());
    }

    /// <summary>
    /// Servicio contra la base real, con una fábrica compartida por <see cref="PermisosService"/> y por
    /// el propio servicio, cuyo interceptor rompe el primer comando que contenga «Solicitudes». La
    /// consulta del organigrama (tabla <c>Empleados</c>) no la toca, así que el equipo se resuelve
    /// normalmente y solo el listado de solicitudes falla. Mismo patrón que
    /// <c>ResolucionDeSolicitudTests.ServicioConFabricaEspecial</c>.
    /// </summary>
    private SolicitudesService ServicioConFabricaEspecial(int empleadoId, Func<string, bool> debeFallar)
    {
        using var sonda = _baseDeDatos.CrearContexto();
        var cadena = sonda.Database.GetConnectionString()!;

        var fabricaConInterceptor = new FabricaDeContextosConInterceptor(cadena, debeFallar);

        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService(fabricaConInterceptor);
        var tiempo = new TiempoFijo(_instanteFijo);
        var saldo = new SaldoService(fabricaConInterceptor, identidad, permisos, tiempo);

        return new SolicitudesService(
            fabricaConInterceptor, identidad, permisos, tiempo, saldo, NuevoRegistro());
    }

    private static ILogger<SolicitudesService> NuevoRegistro() => new RegistroDePrueba();

    private sealed class FabricaDeContextosConInterceptor(string cadena, Func<string, bool> debeFallar)
        : IDbContextFactory<VacacionesDbContext>
    {
        public VacacionesDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<VacacionesDbContext>()
                .UseSqlServer(cadena)
                .AddInterceptors(new InterceptorDeFalloInducido(debeFallar))
                .Options);
    }

    /// <summary>
    /// Rompe el primer comando que cumpla el predicado. Mismo patrón que
    /// <c>TopeAnualEnElAltaTests.InterceptorDeFalloInducido</c> y
    /// <c>ResolucionDeSolicitudTests.InterceptorDeFalloInducido</c>.
    /// </summary>
    private sealed class InterceptorDeFalloInducido(Func<string, bool> debeFallar) : DbCommandInterceptor
    {
        public const string Marca = "fallo-inducido-por-ListadoDelEquipoTests";

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            RomperSiCorresponde(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            RomperSiCorresponde(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            RomperSiCorresponde(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void RomperSiCorresponde(DbCommand command)
        {
            if (debeFallar(command.CommandText))
            {
                throw new InvalidOperationException(Marca);
            }
        }
    }

    /// <summary>Registro en memoria. Mismo patrón que <c>ResolucionDeSolicitudTests.RegistroDePrueba</c>.</summary>
    private sealed class RegistroDePrueba : ILogger<SolicitudesService>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }
    }
}
