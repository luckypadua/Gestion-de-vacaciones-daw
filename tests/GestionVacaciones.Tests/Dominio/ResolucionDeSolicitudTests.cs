using System.Data.Common;
using System.Reflection;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Bloque 3 de FEAT-002: <see cref="SolicitudesService.ResolverAsync"/> aprueba o rechaza una solicitud
/// <c>Pendiente</c> (FR-01 a FR-04), únicamente para quien <see cref="PermisosService"/> autoriza
/// (Bloque 2), dentro de una transacción serializable con el mismo reintento dirigido que FEAT-001c ya
/// usa para su propia carrera de concurrencia (R-25).
/// </summary>
/// <remarks>
/// La mayoría necesita la instancia SQL Server 2022 del entorno: lo que se afirma es el resultado de
/// escribir y leer filas reales, y la carrera de
/// <see cref="Dos_resoluciones_concurrentes_de_la_misma_solicitud_una_gana_y_la_otra_ve_ya_resuelta"/>
/// solo es observable contra el motor de verdad.
/// <see cref="Rechazar_sin_motivo_se_impide_con_el_mensaje_de_AC_03"/> usa
/// <see cref="FabricaQueNadieDebeUsar"/>, igual que <c>TopeAnualEnElAltaTests.ServicioSinBase</c>.
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class ResolucionDeSolicitudTests
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 7, 9, 15, 30, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public ResolucionDeSolicitudTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Un_manager_aprueba_una_solicitud_pendiente_de_su_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var (servicio, _) = ServicioPara(manager.Id, tiempo);

        var resultado = await servicio.ResolverAsync(solicitudId, aprobar: true, motivo: null, Cancelacion);

        Assert.True(resultado.FueResuelta, resultado.MensajeDeError);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);

        Assert.Equal(EstadoSolicitud.Aprobada, persistida.Estado);
        Assert.Equal(manager.Id, persistida.ResueltoPorId);
        Assert.Null(persistida.MotivoDeRechazo);
        Assert.NotNull(persistida.FechaResolucion);
    }

    [Fact]
    public async Task Un_designado_aprueba_una_solicitud_pendiente_del_equipo_de_su_manager()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var designado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerYDesignadoAsync(subordinado.Id, manager.Id, designado.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var (servicio, _) = ServicioPara(designado.Id, tiempo);

        var resultado = await servicio.ResolverAsync(solicitudId, aprobar: true, motivo: null, Cancelacion);

        Assert.True(resultado.FueResuelta, resultado.MensajeDeError);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);

        Assert.Equal(EstadoSolicitud.Aprobada, persistida.Estado);
        Assert.Equal(designado.Id, persistida.ResueltoPorId);
    }

    [Fact]
    public async Task Un_manager_rechaza_una_solicitud_pendiente_indicando_un_motivo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var (servicio, _) = ServicioPara(manager.Id, tiempo);

        const string Motivo = "No hay cobertura de equipo en esas fechas";
        var resultado = await servicio.ResolverAsync(solicitudId, aprobar: false, motivo: Motivo, Cancelacion);

        Assert.True(resultado.FueResuelta, resultado.MensajeDeError);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);

        Assert.Equal(EstadoSolicitud.Rechazada, persistida.Estado);
        Assert.Equal(manager.Id, persistida.ResueltoPorId);
        Assert.Equal(Motivo, persistida.MotivoDeRechazo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rechazar_sin_motivo_se_impide_con_el_mensaje_de_AC_03(string? motivo)
    {
        // Sin abrir contexto: la fábrica revienta si alguien la usa. La solicitud no necesita existir de
        // verdad, porque el flujo tiene que impedirse ANTES de leerla.
        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioSinBase(tiempo);

        var resultado = await servicio.ResolverAsync(931, aprobar: false, motivo, Cancelacion);

        Assert.False(resultado.FueResuelta);
        Assert.Equal(ErroresDeSolicitud.MotivoDeRechazoObligatorio, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Resolver_una_solicitud_ya_aprobada_se_impide_con_el_mensaje_de_AC_04()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarResueltaAsync(
            subordinado.Id, manager.Id, tiempo, EstadoSolicitud.Aprobada, motivo: null);

        var (servicio, _) = ServicioPara(manager.Id, tiempo);

        var resultado = await servicio.ResolverAsync(solicitudId, aprobar: true, motivo: null, Cancelacion);

        Assert.False(resultado.FueResuelta);
        Assert.Equal(ErroresDeSolicitud.SolicitudYaResuelta, resultado.MensajeDeError);

        // Y el reintento del manager no pisó la resolución que ya estaba.
        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);
        Assert.Equal(EstadoSolicitud.Aprobada, persistida.Estado);
    }

    [Fact]
    public async Task Resolver_una_solicitud_ya_rechazada_se_impide_con_el_mensaje_de_AC_04()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarResueltaAsync(
            subordinado.Id, manager.Id, tiempo, EstadoSolicitud.Rechazada, motivo: "Motivo original");

        var (servicio, _) = ServicioPara(manager.Id, tiempo);

        var resultado = await servicio.ResolverAsync(
            solicitudId, aprobar: false, motivo: "Segundo intento", Cancelacion);

        Assert.False(resultado.FueResuelta);
        Assert.Equal(ErroresDeSolicitud.SolicitudYaResuelta, resultado.MensajeDeError);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);
        Assert.Equal("Motivo original", persistida.MotivoDeRechazo);
    }

    [Fact]
    public async Task Un_empleado_sin_relacion_no_puede_resolver_y_la_excepcion_se_propaga()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var quienConsulta = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var ajeno = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(ajeno.Id, tiempo);

        var (servicio, _) = ServicioPara(quienConsulta.Id, tiempo);

        // NO es un ResultadoDeLaResolucion.Rechazada: es un 403 propagado tal cual (AC-08).
        await Assert.ThrowsAsync<AccesoASolicitudesDenegadoException>(
            () => servicio.ResolverAsync(solicitudId, aprobar: true, motivo: null, Cancelacion));

        // Y la solicitud sigue Pendiente: el intento denegado no dejó ningún rastro de resolución.
        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);
        Assert.Equal(EstadoSolicitud.Pendiente, persistida.Estado);
        Assert.Null(persistida.ResueltoPorId);
    }

    [Fact]
    public async Task La_resolucion_registra_quien_y_cuando()
    {
        // AC-05, con el mismo cuidado que AltaDeSolicitudTests le presta al desplazamiento: la fecha de
        // resolución tiene que ser EXACTAMENTE la del reloj inyectado, con su huso, no la del sistema.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var (servicio, _) = ServicioPara(manager.Id, tiempo);

        var resultado = await servicio.ResolverAsync(
            solicitudId, aprobar: false, motivo: "Ya hay demasiada gente de licencia", Cancelacion);

        Assert.True(resultado.FueResuelta, resultado.MensajeDeError);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);

        Assert.Equal(manager.Id, persistida.ResueltoPorId);
        Assert.Equal(tiempo.Ahora, persistida.FechaResolucion);
        Assert.Equal(tiempo.Ahora.Offset, persistida.FechaResolucion!.Value.Offset);
    }

    [Fact]
    public async Task El_empleado_ve_quien_resolvio_y_el_motivo_en_su_listado()
    {
        // AC-06, vía ListarPropiasAsync: lo que el empleado ve de su propia solicitud ya resuelta.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var (servicioDelManager, _) = ServicioPara(manager.Id, tiempo);
        const string Motivo = "Necesitamos tu cobertura esa semana";
        var resolucion = await servicioDelManager.ResolverAsync(
            solicitudId, aprobar: false, motivo: Motivo, Cancelacion);
        Assert.True(resolucion.FueResuelta, resolucion.MensajeDeError);

        var (servicioDelEmpleado, _) = ServicioPara(subordinado.Id, tiempo);
        var propias = await servicioDelEmpleado.ListarPropiasAsync(Cancelacion);

        var vista = Assert.Single(propias, solicitud => solicitud.Id == solicitudId);
        Assert.Equal(EstadoSolicitud.Rechazada, vista.Estado);
        Assert.Equal(manager.Id, vista.ResueltoPorId);
        Assert.Equal(Motivo, vista.MotivoDeRechazo);
    }

    [Fact]
    public async Task Dos_resoluciones_concurrentes_de_la_misma_solicitud_una_gana_y_la_otra_ve_ya_resuelta()
    {
        // R-25: dos ResolverAsync en paralelo contra la instancia real, sobre la MISMA solicitud. El
        // criterio es estricto, igual que en SuperposicionDePeriodosTests: NUNCA una excepción sin
        // capturar — el reintento dirigido tiene que resolver siempre en exactamente una aplicada y la
        // otra Rechazada(SolicitudYaResuelta).
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var (servicioA, _) = ServicioPara(manager.Id, tiempo);
        var (servicioB, _) = ServicioPara(manager.Id, tiempo);

        var tareaA = servicioA.ResolverAsync(solicitudId, aprobar: true, motivo: null, Cancelacion);
        var tareaB = servicioB.ResolverAsync(
            solicitudId, aprobar: false, motivo: "Llega tarde", Cancelacion);

        // Sin try/catch a propósito: si cualquiera de las dos propaga una excepción sin capturar, este
        // test tiene que fallar ruidosamente y no confundirlo con un resultado válido.
        var resultados = await Task.WhenAll(tareaA, tareaB);

        var resueltas = resultados.Count(resultado => resultado.FueResuelta);
        var yaResueltas = resultados.Count(resultado =>
            !resultado.FueResuelta && resultado.MensajeDeError == ErroresDeSolicitud.SolicitudYaResuelta);

        Assert.True(
            resueltas == 1 && yaResueltas == 1,
            "Se esperaba exactamente una resolución aplicada y la otra rechazada por 'ya resuelta'. " +
            "Resultados: " + string.Join(
                " | ",
                resultados.Select(r => r.FueResuelta ? "Resuelta" : $"Rechazada: {r.MensajeDeError}")));
    }

    [Fact]
    public async Task Un_conflicto_de_serializacion_que_no_es_una_carrera_de_resolucion_se_propaga()
    {
        // Mismo mecanismo de inyección que SuperposicionDePeriodosTests: se rompe la ÚNICA vez que se lee
        // la solicitud, con un SqlException(1205) simulado. Como nadie más resolvió nada, al reintentar
        // sigue Pendiente y la excepción ORIGINAL se repropaga sin convertir (R-16).
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var servicio = ServicioConConflictoDeSerializacionUnaVez(manager.Id, tiempo);

        var excepcion = await Record.ExceptionAsync(
            () => servicio.ResolverAsync(solicitudId, aprobar: true, motivo: null, Cancelacion));

        Assert.NotNull(excepcion);
        var sql = BuscarSqlException(excepcion);
        Assert.NotNull(sql);
        Assert.Equal(1205, sql!.Number);

        // Y la solicitud sigue Pendiente: el conflicto no se convirtió en ninguna resolución.
        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);
        Assert.Equal(EstadoSolicitud.Pendiente, persistida.Estado);
    }

    [Fact]
    public async Task El_motivo_se_muestra_como_texto_plano_sin_interpretarse()
    {
        // Mitigación de R-24: el centinela viaja y vuelve LITERAL, carácter por carácter. Que no se
        // INTERPRETE es responsabilidad de Blazor al renderizarlo (Bloque 5, sin MarkupString); lo que
        // este bloque garantiza es que el servicio no lo altera —ni lo escapa, ni lo recorta— en ningún
        // punto del camino de escritura o de lectura.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        const string Centinela = "<script>alert('xss')</script>";

        var (servicioDelManager, _) = ServicioPara(manager.Id, tiempo);
        var resolucion = await servicioDelManager.ResolverAsync(
            solicitudId, aprobar: false, motivo: Centinela, Cancelacion);
        Assert.True(resolucion.FueResuelta, resolucion.MensajeDeError);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);
        Assert.Equal(Centinela, persistida.MotivoDeRechazo);

        var (servicioDelEmpleado, _) = ServicioPara(subordinado.Id, tiempo);
        var propias = await servicioDelEmpleado.ListarPropiasAsync(Cancelacion);
        var vista = Assert.Single(propias, solicitud => solicitud.Id == solicitudId);
        Assert.Equal(Centinela, vista.MotivoDeRechazo);
    }

    [Fact]
    public async Task Un_fallo_al_guardar_la_resolucion_revierte_la_transaccion_y_se_propaga()
    {
        // Mismo patrón que AltaDeSolicitudTests/TopeAnualEnElAltaTests: el UPDATE final se rompe con un
        // interceptor de EF Core. La excepción se propaga y la solicitud sigue Pendiente, sin ningún
        // dato de resolución a medias.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var tiempo = new TiempoFijo(_instanteFijo);
        var solicitudId = await SembrarPendienteAsync(subordinado.Id, tiempo);

        var servicio = ServicioConFabricaEspecial(
            manager.Id,
            tiempo,
            comando => comando.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
                && comando.Contains("Solicitudes", StringComparison.OrdinalIgnoreCase));

        var excepcion = await Record.ExceptionAsync(
            () => servicio.ResolverAsync(solicitudId, aprobar: true, motivo: null, Cancelacion));

        Assert.NotNull(excepcion);
        Assert.Contains(InterceptorDeFalloInducido.Marca, excepcion.ToString(), StringComparison.Ordinal);

        await using var relectura = _baseDeDatos.CrearContexto();
        var persistida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == solicitudId, Cancelacion);

        Assert.Equal(EstadoSolicitud.Pendiente, persistida.Estado);
        Assert.Null(persistida.ResueltoPorId);
        Assert.Null(persistida.FechaResolucion);
        Assert.Null(persistida.MotivoDeRechazo);
    }

    /// <summary>
    /// Camina la cadena de <see cref="Exception.InnerException"/> buscando un <see cref="SqlException"/>.
    /// Mismo recorrido que <c>SolicitudesService.EsConflictoDeSerializacion</c>.
    /// </summary>
    private static SqlException? BuscarSqlException(Exception? excepcion)
    {
        for (var actual = excepcion; actual is not null; actual = actual.InnerException)
        {
            if (actual is SqlException sql)
            {
                return sql;
            }
        }

        return null;
    }

    private async Task<int> SembrarPendienteAsync(int empleadoId, TiempoFijo tiempo)
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        var inicio = tiempo.Hoy.AddDays(10);
        var fin = inicio.AddDays(4);

        var solicitud = new Solicitud
        {
            EmpleadoId = empleadoId,
            FechaInicio = inicio,
            FechaFin = fin,
            DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
            Estado = EstadoSolicitud.Pendiente,
            FechaCreacion = tiempo.Ahora,
        };

        contexto.Solicitudes.Add(solicitud);
        await contexto.SaveChangesAsync(Cancelacion);

        return solicitud.Id;
    }

    private async Task<int> SembrarResueltaAsync(
        int empleadoId, int resueltoPorId, TiempoFijo tiempo, EstadoSolicitud estado, string? motivo)
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        var inicio = tiempo.Hoy.AddDays(10);
        var fin = inicio.AddDays(4);

        var solicitud = new Solicitud
        {
            EmpleadoId = empleadoId,
            FechaInicio = inicio,
            FechaFin = fin,
            DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
            Estado = estado,
            FechaCreacion = tiempo.Ahora,
            ResueltoPorId = resueltoPorId,
            FechaResolucion = tiempo.Ahora,
            MotivoDeRechazo = motivo,
        };

        contexto.Solicitudes.Add(solicitud);
        await contexto.SaveChangesAsync(Cancelacion);

        return solicitud.Id;
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

    private (SolicitudesService Servicio, RegistroDePrueba Registro) ServicioPara(int empleadoId, TimeProvider tiempo)
    {
        var fabrica = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService(fabrica);
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempo);
        var registro = new RegistroDePrueba();

        return (new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, registro), registro);
    }

    /// <summary>
    /// Servicio con identidad resuelta y una fábrica que revienta si alguien la usa: es «este caso no
    /// llega a la base» hecho código, igual que <c>TopeAnualEnElAltaTests.ServicioSinBase</c>.
    /// </summary>
    private SolicitudesService ServicioSinBase(TimeProvider tiempo)
    {
        var fabrica = new FabricaQueNadieDebeUsar();
        var identidad = IdentidadDePrueba.De(7);
        var permisos = new PermisosService(fabrica);
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempo);

        return new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, new RegistroDePrueba());
    }

    private SolicitudesService ServicioConFabricaEspecial(
        int empleadoId, TimeProvider tiempo, Func<string, bool> debeFallar)
    {
        using var sonda = _baseDeDatos.CrearContexto();
        var cadena = sonda.Database.GetConnectionString()!;

        var fabricaConInterceptor = new FabricaDeContextosConInterceptor(cadena, new InterceptorDeFalloInducido(debeFallar));

        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService(fabricaConInterceptor);
        var saldo = new SaldoService(fabricaConInterceptor, identidad, permisos, tiempo);

        return new SolicitudesService(
            fabricaConInterceptor, identidad, permisos, tiempo, saldo, new RegistroDePrueba());
    }

    /// <summary>
    /// Servicio contra la base real cuya PRIMERA lectura de la solicitud revienta con un conflicto de
    /// serialización simulado (<see cref="SqlExceptionDePrueba"/>), y cuyas lecturas siguientes —la del
    /// reintento— corren normalmente. Misma instancia de fábrica para el contexto original y el del
    /// reintento, igual que en <c>SuperposicionDePeriodosTests</c>.
    /// </summary>
    private SolicitudesService ServicioConConflictoDeSerializacionUnaVez(int empleadoId, TimeProvider tiempo)
    {
        using var sonda = _baseDeDatos.CrearContexto();
        var cadena = sonda.Database.GetConnectionString()!;

        var interceptor = new InterceptorDeConflictoDeSerializacionUnaVez(EsLaConsultaDeLaSolicitud);
        var fabricaConInterceptor = new FabricaDeContextosConInterceptor(cadena, interceptor);

        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService(fabricaConInterceptor);
        var saldo = new SaldoService(new FabricaQueNadieDebeUsar(), identidad, permisos, tiempo);

        return new SolicitudesService(
            fabricaConInterceptor, identidad, permisos, tiempo, saldo, new RegistroDePrueba());
    }

    private static bool EsLaConsultaDeLaSolicitud(string comandoSql) =>
        comandoSql.Contains("Solicitudes", StringComparison.Ordinal);

    private sealed class FabricaDeContextosConInterceptor(string cadena, DbCommandInterceptor interceptor)
        : IDbContextFactory<VacacionesDbContext>
    {
        public VacacionesDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<VacacionesDbContext>()
                .UseSqlServer(cadena)
                .AddInterceptors(interceptor)
                .Options);
    }

    /// <summary>
    /// Revienta con un conflicto de serialización simulado la PRIMERA vez que un comando cumple el
    /// predicado, y deja pasar todas las siguientes. Mismo mecanismo que
    /// <c>SuperposicionDePeriodosTests.InterceptorDeConflictoDeSerializacionUnaVez</c>.
    /// </summary>
    private sealed class InterceptorDeConflictoDeSerializacionUnaVez(Func<string, bool> esLaConsultaObjetivo)
        : DbCommandInterceptor
    {
        private bool _yaFallo;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!_yaFallo && esLaConsultaObjetivo(command.CommandText))
            {
                _yaFallo = true;
                throw SqlExceptionDePrueba.ConNumero(
                    1205, $"conflicto de serialización simulado por {nameof(ResolucionDeSolicitudTests)}");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Rompe el primer comando que cumpla el predicado. Mismo patrón que
    /// <c>TopeAnualEnElAltaTests.InterceptorDeFalloInducido</c>.
    /// </summary>
    private sealed class InterceptorDeFalloInducido(Func<string, bool> debeFallar) : DbCommandInterceptor
    {
        public const string Marca = "fallo-inducido-por-ResolucionDeSolicitudTests";

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

    /// <summary>
    /// Construye un <see cref="SqlException"/> real con el <see cref="SqlException.Number"/> elegido.
    /// Copia exacta del helper de <c>SuperposicionDePeriodosTests</c> (mismo motivo, misma versión del
    /// driver: ver los comentarios de aquel para el porqué de la reflexión).
    /// </summary>
    private static class SqlExceptionDePrueba
    {
        public static SqlException ConNumero(int numero, string mensaje)
        {
            var error = CrearSqlError(numero, mensaje);
            var coleccion = CrearColeccionCon(error);

            var metodoCreateException = typeof(SqlException).GetMethod(
                "CreateException",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: [typeof(SqlErrorCollection), typeof(string)],
                modifiers: null)
                ?? throw new InvalidOperationException(
                    "Microsoft.Data.SqlClient ya no expone SqlException.CreateException(SqlErrorCollection, " +
                    "string) con esa firma interna. Este helper depende de un detalle interno del driver " +
                    "(versión 6.1.1) y hay que revisarlo contra la versión instalada.");

            return (SqlException)metodoCreateException.Invoke(null, [coleccion, "11.0.0"])!;
        }

        private static object CrearSqlError(int numero, string mensaje)
        {
            var constructor = typeof(SqlError).GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types:
                [
                    typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string),
                    typeof(string), typeof(int), typeof(Exception),
                ],
                modifiers: null)
                ?? throw new InvalidOperationException(
                    "Microsoft.Data.SqlClient ya no expone el constructor interno de SqlError con esa " +
                    "firma. Este helper depende de un detalle interno del driver (versión 6.1.1) y hay " +
                    "que revisarlo contra la versión instalada.");

            return constructor.Invoke([
                numero,
                (byte)1,
                (byte)13, // Clase de severidad real de un 1205 (deadlock/conflicto de serialización).
                "servidor-de-prueba",
                mensaje,
                "procedimiento-de-prueba",
                1,
                null,
            ]);
        }

        private static SqlErrorCollection CrearColeccionCon(object error)
        {
            var coleccion = (SqlErrorCollection)Activator.CreateInstance(
                typeof(SqlErrorCollection), nonPublic: true)!;

            var metodoAdd = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    "Microsoft.Data.SqlClient ya no expone SqlErrorCollection.Add(SqlError). Este helper " +
                    "depende de un detalle interno del driver (versión 6.1.1) y hay que revisarlo contra " +
                    "la versión instalada.");

            metodoAdd.Invoke(coleccion, [error]);

            return coleccion;
        }
    }

    /// <summary>Registro en memoria. Mismo patrón que <c>TopeAnualEnElAltaTests.RegistroDePrueba</c>.</summary>
    private sealed class RegistroDePrueba : ILogger<SolicitudesService>
    {
        private readonly List<EntradaDeRegistro> _entradas = [];

        public IReadOnlyList<EntradaDeRegistro> Entradas => _entradas;

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
            ArgumentNullException.ThrowIfNull(formatter);
            _entradas.Add(new EntradaDeRegistro(logLevel, formatter(state, exception)));
        }
    }

    private sealed record EntradaDeRegistro(LogLevel Nivel, string Mensaje);
}
