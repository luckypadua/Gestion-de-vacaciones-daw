using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// B5-T7: el listado devuelve <b>solo</b> las solicitudes del empleado actual, en orden descendente por
/// fecha de creación, cada una con su estado (AC-05).
/// </summary>
/// <remarks>
/// Necesita la instancia SQL Server 2022 del entorno: el filtro y el orden los resuelve el motor —es lo
/// que sirve el índice <c>IX_Solicitud_EmpleadoId_FechaCreacion</c> del Bloque 2— y un orden verificado
/// en memoria no diría nada sobre el SQL que se emite. Si no hay cadena configurada se saltea con motivo
/// explícito. Cada caso crea sus propios empleados y los borra al terminar, con sus solicitudes
/// (regla #0).
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class ListadoPropioTests
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 30, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public ListadoPropioTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task B5_T7_El_listado_devuelve_solo_las_propias_en_orden_descendente_y_con_su_estado()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var propio = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var ajeno = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // Las tres del empleado actual, con fechas de creación distintas y sembradas EN DESORDEN: si el
        // servicio no ordenara, la base podría devolverlas en el orden de inserción y un test que las
        // cargara ya ordenadas pasaría igual.
        var laDelMedio = new DateTimeOffset(2026, 5, 2, 10, 0, 0, TimeSpan.FromHours(-3));
        var laMasNueva = new DateTimeOffset(2026, 6, 20, 8, 30, 0, TimeSpan.FromHours(-3));
        var laMasVieja = new DateTimeOffset(2026, 4, 11, 17, 45, 0, TimeSpan.FromHours(-3));

        await SembrarAsync(
            (propio.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3), EstadoSolicitud.Pendiente, laDelMedio),
            (propio.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5), EstadoSolicitud.Aprobada, laMasNueva),
            (propio.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 6), EstadoSolicitud.Rechazada, laMasVieja),

            // Y una del otro empleado, con la fecha de creación MÁS NUEVA de todas: si el filtro faltara,
            // aparecería primera en el listado y el fallo sería inconfundible.
            (ajeno.Id, new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 2), EstadoSolicitud.Pendiente,
                new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.FromHours(-3))));

        var servicio = ServicioPara(propio.Id);

        var listado = await servicio.ListarPropiasAsync(Cancelacion);

        // Solo las propias: tres, no cuatro.
        Assert.Equal(3, listado.Count);
        Assert.All(listado, solicitud => Assert.Equal(propio.Id, solicitud.EmpleadoId));

        // Orden descendente por fecha de creación, que es lo que sirve el índice del Bloque 2.
        Assert.Equal([laMasNueva, laDelMedio, laMasVieja], listado.Select(solicitud => solicitud.FechaCreacion));

        // Cada una con SU estado: los tres del enum aparecen y no colapsan en Pendiente. Sin esto, el
        // listado podría estar proyectando un estado fijo y el orden seguiría verde.
        Assert.Equal(
            [EstadoSolicitud.Aprobada, EstadoSolicitud.Pendiente, EstadoSolicitud.Rechazada],
            listado.Select(solicitud => solicitud.Estado));

        // Y el período con sus días corridos viaja completo: el listado de AC-05 es lo que el empleado
        // usa para saber qué pidió, no solo cuándo lo pidió.
        var laPrimera = listado[0];
        Assert.Equal(new DateOnly(2026, 10, 5), laPrimera.FechaInicio);
        Assert.Equal(new DateOnly(2026, 10, 5), laPrimera.FechaFin);
        Assert.Equal(1, laPrimera.DiasCorridos);
        Assert.NotEqual(0, laPrimera.Id);
    }

    [Fact]
    public async Task Un_empleado_sin_solicitudes_recibe_una_lista_vacia_y_no_un_error()
    {
        // El estado vacío legítimo, y el que el Bloque 6 tiene que poder distinguir del error y de la
        // falta de identidad. Sin este caso, «devuelve solo las propias» lo cumpliría también un servicio
        // que lanzara cuando no hay ninguna.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var reciencito = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var listado = await ServicioPara(reciencito.Id).ListarPropiasAsync(Cancelacion);

        Assert.Empty(listado);
    }

    [Fact]
    public async Task Lo_que_el_alta_crea_aparece_en_el_listado_del_mismo_empleado()
    {
        // La costura entre las dos mitades del bloque. Por separado, el alta puede escribir con un
        // EmpleadoId y el listado filtrar por otro —o el alta persistir un estado y el listado proyectar
        // otro— y los dos tests quedarían en verde. Es además el recorrido literal de AC-04 + AC-05: el
        // empleado envía una solicitud y la ve en su listado.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(20);
        var alta = await servicio.CrearAsync(inicio, inicio.AddDays(4), Cancelacion);

        Assert.True(alta.FueCreada, alta.MensajeDeError);

        var listado = await servicio.ListarPropiasAsync(Cancelacion);

        var laSuya = Assert.Single(listado);
        Assert.Equal(alta.SolicitudId, laSuya.Id);
        Assert.Equal(empleado.Id, laSuya.EmpleadoId);
        Assert.Equal(inicio, laSuya.FechaInicio);
        Assert.Equal(inicio.AddDays(4), laSuya.FechaFin);
        Assert.Equal(5, laSuya.DiasCorridos);
        Assert.Equal(EstadoSolicitud.Pendiente, laSuya.Estado);
        Assert.Equal(tiempo.Ahora, laSuya.FechaCreacion);
    }

    /// <remarks>
    /// FEAT-001b, Bloque 3: <c>SolicitudesService</c> ganó <c>SaldoService</c> como quinta dependencia.
    /// <see cref="Lo_que_el_alta_crea_aparece_en_el_listado_del_mismo_empleado"/> ejercita
    /// <c>CrearAsync</c> de verdad contra la base descartable, así que acá el <c>SaldoService</c> tiene
    /// que ser uno de verdad —con la misma fábrica— y no uno que reviente.
    /// </remarks>
    private SolicitudesService ServicioPara(int empleadoId, TimeProvider? tiempo = null)
    {
        var fabrica = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService();
        var tiempoEfectivo = tiempo ?? new TiempoFijo(_instanteFijo);
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempoEfectivo);

        return new SolicitudesService(
            fabrica, identidad, permisos, tiempoEfectivo, saldo, NullLogger<SolicitudesService>.Instance);
    }

    /// <summary>
    /// Siembra solicitudes por el contexto y no por el servicio: los estados <c>Aprobada</c> y
    /// <c>Rechazada</c> no los produce ningún camino de FEAT-001a —el flujo de aprobación es de un ticket
    /// futuro— y AC-05 exige mostrar «su estado actual», o sea los tres.
    /// </summary>
    private async Task SembrarAsync(
        params (int EmpleadoId, DateOnly Inicio, DateOnly Fin, EstadoSolicitud Estado, DateTimeOffset Creacion)[] solicitudes)
    {
        await using VacacionesDbContext contexto = _baseDeDatos.CrearContexto();

        foreach (var (empleadoId, inicio, fin, estado, creacion) in solicitudes)
        {
            contexto.Solicitudes.Add(new Solicitud
            {
                EmpleadoId = empleadoId,
                FechaInicio = inicio,
                FechaFin = fin,
                // Coincide con el período por obligación: CK_Solicitud_DiasCoincidenConPeriodo rechaza
                // cualquier otro número, así que la siembra usa el mismo cálculo que el dominio.
                DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
                Estado = estado,
                FechaCreacion = creacion,

                // Desde el Bloque 1 de FEAT-002, CK_Solicitud_ResolucionCoherente exige estos tres
                // datos cuando el estado no es Pendiente. Quién "resolvió" no es lo que este test
                // verifica (el listado propio), así que se reutiliza el propio empleado.
                ResueltoPorId = estado == EstadoSolicitud.Pendiente ? null : empleadoId,
                FechaResolucion = estado == EstadoSolicitud.Pendiente ? null : creacion,
                MotivoDeRechazo = estado == EstadoSolicitud.Rechazada ? "Motivo de prueba" : null,
            });
        }

        await contexto.SaveChangesAsync(Cancelacion);
    }
}
