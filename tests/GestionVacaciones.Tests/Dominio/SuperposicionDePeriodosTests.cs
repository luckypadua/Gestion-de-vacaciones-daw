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
/// Bloque único de FEAT-001c: <see cref="SolicitudesService.CrearAsync"/> impide dos solicitudes
/// vigentes con períodos que se tocan (FR-01, AC-01 a AC-04), validado ANTES del tope y dentro de la
/// misma transacción serializable que ya abre FEAT-001b (FR-02, NFR-01).
/// </summary>
/// <remarks>
/// La mayoría necesita la instancia SQL Server 2022 del entorno: lo que se afirma es el resultado de
/// comparar contra filas reales, y la carrera de
/// <see cref="Dos_altas_concurrentes_de_periodos_solapados_persisten_exactamente_una"/> solo es
/// observable contra el motor de verdad. <see cref="La_superposicion_se_rechaza_sin_consultar_el_saldo"/>
/// usa <see cref="FabricaQueNadieDebeUsar"/> para el saldo, igual que
/// <c>TopeAnualEnElAltaTests.ServicioSinBase</c>.
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class SuperposicionDePeriodosTests
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 30, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public SuperposicionDePeriodosTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Un_periodo_totalmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicioExistente = tiempo.Hoy.AddDays(10);
        var finExistente = inicioExistente.AddDays(9); // 10 días
        await SembrarAsync(empleado.Id, inicioExistente, finExistente, EstadoSolicitud.Pendiente);

        // El nuevo período CONTIENE por completo al existente.
        var inicioNuevo = inicioExistente.AddDays(-2);
        var finNuevo = finExistente.AddDays(2);

        var resultado = await servicio.CrearAsync(inicioNuevo, finNuevo, Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.SuperposicionDePeriodo, resultado.MensajeDeError);
    }

    [Theory]
    [InlineData(true)]  // el nuevo empieza antes y termina dentro del existente
    [InlineData(false)] // el nuevo empieza dentro del existente y termina después
    public async Task Un_periodo_parcialmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01(
        bool elNuevoEmpiezaAntes)
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicioExistente = tiempo.Hoy.AddDays(30);
        var finExistente = inicioExistente.AddDays(9); // 10 días
        await SembrarAsync(empleado.Id, inicioExistente, finExistente, EstadoSolicitud.Aprobada);

        var (inicioNuevo, finNuevo) = elNuevoEmpiezaAntes
            ? (inicioExistente.AddDays(-5), inicioExistente.AddDays(3))
            : (inicioExistente.AddDays(5), finExistente.AddDays(5));

        var resultado = await servicio.CrearAsync(inicioNuevo, finNuevo, Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.SuperposicionDePeriodo, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Un_periodo_contiguo_el_dia_siguiente_se_acepta()
    {
        // AC-02, literal del PRD: el nuevo período empieza el día siguiente al fin del existente.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicioExistente = tiempo.Hoy.AddDays(10);
        var finExistente = inicioExistente.AddDays(4); // 5 días
        await SembrarAsync(empleado.Id, inicioExistente, finExistente, EstadoSolicitud.Aprobada);

        var inicioNuevo = finExistente.AddDays(1);
        var resultado = await servicio.CrearAsync(inicioNuevo, inicioNuevo.AddDays(2), Cancelacion);

        Assert.True(resultado.FueCreada, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Un_periodo_contiguo_el_dia_anterior_se_acepta()
    {
        // Espejo de AC-02: el nuevo período termina el día anterior al inicio del existente.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicioExistente = tiempo.Hoy.AddDays(20);
        var finExistente = inicioExistente.AddDays(4);
        await SembrarAsync(empleado.Id, inicioExistente, finExistente, EstadoSolicitud.Aprobada);

        var finNuevo = inicioExistente.AddDays(-1);
        var inicioNuevo = finNuevo.AddDays(-2);
        var resultado = await servicio.CrearAsync(inicioNuevo, finNuevo, Cancelacion);

        Assert.True(resultado.FueCreada, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Una_solicitud_rechazada_no_bloquea_las_mismas_fechas()
    {
        // AC-03: la única solicitud que coincide en fechas está Rechazada, así que no cuenta.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);
        var fin = inicio.AddDays(4);
        await SembrarAsync(empleado.Id, inicio, fin, EstadoSolicitud.Rechazada);

        var resultado = await servicio.CrearAsync(inicio, fin, Cancelacion);

        Assert.True(resultado.FueCreada, resultado.MensajeDeError);
    }

    public static IEnumerable<object[]> EstadosVigentes() =>
        EstadosDeSolicitud.Vigentes.Select(estado => new object[] { estado });

    [Theory]
    [MemberData(nameof(EstadosVigentes))]
    public async Task Una_solicitud_pendiente_bloquea_igual_que_una_aprobada(EstadoSolicitud estado)
    {
        // Refuerzo de FR-01 sobre los dos estados de EstadosDeSolicitud.Vigentes.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);
        var fin = inicio.AddDays(4);
        await SembrarAsync(empleado.Id, inicio, fin, estado);

        var resultado = await servicio.CrearAsync(inicio, fin, Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.SuperposicionDePeriodo, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Un_periodo_que_no_se_superpone_con_nada_se_acepta()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);
        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(4), Cancelacion);

        Assert.True(resultado.FueCreada, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Dos_altas_concurrentes_de_periodos_solapados_persisten_exactamente_una()
    {
        // AC-04: dos CrearAsync en paralelo contra la instancia real, con períodos que se solapan entre
        // sí. A diferencia de TopeAnualEnElAltaTests.Dos_altas_concurrentes_no_superan_el_tope, acá el
        // criterio es más estricto: NUNCA una excepción sin capturar — el reintento dirigido tiene que
        // resolver siempre en exactamente una creada y la otra Rechazada(SuperposicionDePeriodo).
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicioA, _) = ServicioPara(empleado.Id, tiempo);
        var (servicioB, _) = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);

        var tareaA = servicioA.CrearAsync(inicio, inicio.AddDays(4), Cancelacion);
        var tareaB = servicioB.CrearAsync(inicio.AddDays(2), inicio.AddDays(6), Cancelacion);

        // Sin try/catch a propósito: si cualquiera de las dos propaga una excepción sin capturar, este
        // test tiene que fallar ruidosamente y no confundirlo con un resultado válido.
        var resultados = await Task.WhenAll(tareaA, tareaB);

        var creadas = resultados.Count(resultado => resultado.FueCreada);
        var rechazadasPorSuperposicion = resultados.Count(resultado =>
            !resultado.FueCreada && resultado.MensajeDeError == ErroresDeSolicitud.SuperposicionDePeriodo);

        Assert.True(
            creadas == 1 && rechazadasPorSuperposicion == 1,
            "Se esperaba exactamente una creada y la otra rechazada por superposición. Resultados: " +
            string.Join(
                " | ",
                resultados.Select(r => r.FueCreada ? "Creada" : $"Rechazada: {r.MensajeDeError}")));
    }

    [Fact]
    public async Task La_superposicion_se_rechaza_sin_consultar_el_saldo()
    {
        // Camino triste de orden: SaldoService se arma con FabricaQueNadieDebeUsar (mismo patrón que
        // TopeAnualEnElAltaTests.ServicioPara). Si hubiera superposición y el orden estuviera invertido,
        // esto revienta en cuanto DeLosAniosAsync abriera contexto; pasar confirma que nunca se llega a
        // preguntarle al saldo.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioConSaldoQueNadieDebeUsar(empleado.Id, tiempo);

        var inicioExistente = tiempo.Hoy.AddDays(10);
        var finExistente = inicioExistente.AddDays(4);
        await SembrarAsync(empleado.Id, inicioExistente, finExistente, EstadoSolicitud.Pendiente);

        var resultado = await servicio.CrearAsync(inicioExistente, finExistente, Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.SuperposicionDePeriodo, resultado.MensajeDeError);
    }

    [Fact]
    public async Task El_conflicto_de_serializacion_de_una_superposicion_real_se_convierte_en_rechazo()
    {
        // El conflicto de serialización se inyecta (interceptor de EF Core + SqlException real
        // construida por reflexión), y la fila que "ganó la carrera" ya existe al momento del reintento:
        // lo que se prueba es la conversión, no que SQL Server sepa detectar conflictos.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);

        var inicioExistente = tiempo.Hoy.AddDays(10);
        var finExistente = inicioExistente.AddDays(4);
        await SembrarAsync(empleado.Id, inicioExistente, finExistente, EstadoSolicitud.Pendiente);

        var (servicio, registro) = ServicioConConflictoDeSerializacionUnaVez(empleado.Id, tiempo);

        var resultado = await servicio.CrearAsync(inicioExistente, finExistente, Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.SuperposicionDePeriodo, resultado.MensajeDeError);

        // Y el log deja constancia de que esta vez vino del reintento (R-20).
        Assert.Contains(registro.Entradas, entrada => entrada.Nivel == LogLevel.Information
            && entrada.Mensaje.Contains("reintento", StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_conflicto_de_serializacion_que_no_es_superposicion_se_propaga()
    {
        // Mismo mecanismo de inyección, pero sin ninguna fila que superponga al reintentar: la
        // excepción original sale sin convertirse (mismo criterio que R-16).
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioConConflictoDeSerializacionUnaVez(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);

        var excepcion = await Record.ExceptionAsync(
            () => servicio.CrearAsync(inicio, inicio.AddDays(4), Cancelacion));

        // NO es un ResultadoDelAlta.Rechazada: es la excepción de conflicto, propagada tal cual —lo que
        // EF Core deja propagar es un InvalidOperationException de SqlServerExecutionStrategy que
        // ENVUELVE nuestro SqlException(1205), no el SqlException «pelado»; lo que importa es que ese
        // conflicto siga adentro, intacto, y no se haya convertido en un rechazo.
        Assert.NotNull(excepcion);
        var sql = BuscarSqlException(excepcion);
        Assert.NotNull(sql);
        Assert.Equal(1205, sql!.Number);
    }

    /// <summary>
    /// Camina la cadena de <see cref="Exception.InnerException"/> buscando un <see cref="SqlException"/>.
    /// Mismo recorrido que <c>SolicitudesService.EsConflictoDeSerializacion</c>, para poder afirmar sobre
    /// lo que ese método ve —no sobre el tipo exacto que EF Core deja en la superficie, que puede llegar
    /// envuelto en un <see cref="InvalidOperationException"/> propio de
    /// <c>SqlServerExecutionStrategy</c>—.
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

    [Fact]
    public async Task El_rechazo_por_superposicion_queda_registrado()
    {
        // R-20: hay una entrada de log con EmpleadoId, y ningún otro dígito — ni fechas ni el
        // identificador de la otra solicitud.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, registro) = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);
        var fin = inicio.AddDays(4);
        await SembrarAsync(empleado.Id, inicio, fin, EstadoSolicitud.Pendiente);

        var resultado = await servicio.CrearAsync(inicio, fin, Cancelacion);

        Assert.False(resultado.FueCreada);

        var entrada = Assert.Single(registro.Entradas, entrada => entrada.Nivel == LogLevel.Information);

        Assert.Contains(
            empleado.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            entrada.Mensaje,
            StringComparison.Ordinal);

        // Los únicos dígitos del mensaje son los del EmpleadoId: ni fechas ni el identificador de la
        // otra solicitud (fuera de alcance del PRD).
        Assert.Equal(
            empleado.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new string([.. entrada.Mensaje.Where(char.IsDigit)]));
    }

    private async Task SembrarAsync(int empleadoId, DateOnly inicio, DateOnly fin, EstadoSolicitud estado)
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        contexto.Solicitudes.Add(new Solicitud
        {
            EmpleadoId = empleadoId,
            FechaInicio = inicio,
            FechaFin = fin,
            DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
            Estado = estado,
            FechaCreacion = _instanteFijo,

            // Desde el Bloque 1 de FEAT-002, CK_Solicitud_ResolucionCoherente exige estos tres datos
            // cuando el estado no es Pendiente. Quién "resolvió" no es lo que este test verifica (la
            // superposición), así que se reutiliza el propio empleado.
            ResueltoPorId = estado == EstadoSolicitud.Pendiente ? null : empleadoId,
            FechaResolucion = estado == EstadoSolicitud.Pendiente ? null : _instanteFijo,
            MotivoDeRechazo = estado == EstadoSolicitud.Rechazada ? "Motivo de prueba" : null,
        });

        await contexto.SaveChangesAsync(Cancelacion);
    }

    private (SolicitudesService Servicio, RegistroDePrueba Registro) ServicioPara(int empleadoId, TimeProvider tiempo)
    {
        var fabrica = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService();
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempo);
        var registro = new RegistroDePrueba();

        return (new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, registro), registro);
    }

    /// <summary>
    /// Servicio contra la base real, con el saldo armado sobre una fábrica que revienta si alguien la
    /// usa: es «este caso no llega a preguntarle al saldo» hecho código, igual que
    /// <c>TopeAnualEnElAltaTests.ServicioSinBase</c> lo hace para «este caso no llega a la base».
    /// </summary>
    private (SolicitudesService Servicio, RegistroDePrueba Registro) ServicioConSaldoQueNadieDebeUsar(
        int empleadoId, TimeProvider tiempo)
    {
        var fabrica = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService();
        var saldo = new SaldoService(new FabricaQueNadieDebeUsar(), identidad, permisos, tiempo);
        var registro = new RegistroDePrueba();

        return (new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, registro), registro);
    }

    /// <summary>
    /// Servicio contra la base real cuya PRIMERA consulta de solapamiento revienta con un conflicto de
    /// serialización simulado (<see cref="SqlExceptionDePrueba"/>), y cuyas consultas siguientes —la del
    /// reintento— corren normalmente. El saldo se arma con <see cref="FabricaQueNadieDebeUsar"/>: ni el
    /// camino directo ni el de reintento tienen que llegar a preguntarle nada.
    /// </summary>
    private (SolicitudesService Servicio, RegistroDePrueba Registro) ServicioConConflictoDeSerializacionUnaVez(
        int empleadoId, TimeProvider tiempo)
    {
        using var sonda = _baseDeDatos.CrearContexto();
        var cadena = sonda.Database.GetConnectionString()!;

        var interceptor = new InterceptorDeConflictoDeSerializacionUnaVez(EsLaConsultaDeSolapamiento);
        var fabricaConInterceptor = new FabricaDeContextosConInterceptor(cadena, interceptor);

        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService();
        var saldo = new SaldoService(new FabricaQueNadieDebeUsar(), identidad, permisos, tiempo);
        var registro = new RegistroDePrueba();

        return (
            new SolicitudesService(fabricaConInterceptor, identidad, permisos, tiempo, saldo, registro),
            registro);
    }

    /// <summary>
    /// La consulta de solapamiento es la única, de las que <see cref="SolicitudesService.CrearAsync"/>
    /// ejecuta sobre esta conexión, que filtra por <c>Estado</c> Y por las dos fechas del período: la
    /// lectura <i>fence</i> solo filtra por <c>EmpleadoId</c>.
    /// </summary>
    private static bool EsLaConsultaDeSolapamiento(string comandoSql) =>
        comandoSql.Contains("Estado", StringComparison.Ordinal)
        && comandoSql.Contains("FechaInicio", StringComparison.Ordinal)
        && comandoSql.Contains("FechaFin", StringComparison.Ordinal);

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
    /// predicado, y deja pasar todas las siguientes. La MISMA instancia se reutiliza entre el contexto
    /// original y el del reintento —los crea los dos la misma fábrica—, así que "una sola vez" es una
    /// sola vez en todo el flujo, y no una vez por contexto.
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
                    1205, $"conflicto de serialización simulado por {nameof(SuperposicionDePeriodosTests)}");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Construye un <see cref="SqlException"/> real con el <see cref="SqlException.Number"/> elegido.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué hace falta reflexión.</b> <see cref="SqlException"/> no tiene ningún constructor
    /// público: los suyos son todos <c>internal</c> («Assembly», en el vocabulario de IL), porque el
    /// driver los reserva para sí mismo — no son parte de su contrato público. El único camino para
    /// construir uno con un <see cref="SqlException.Number"/> elegido a mano es replicar, por reflexión,
    /// la misma secuencia que <c>Microsoft.Data.SqlClient</c> usa puertas adentro: un
    /// <c>SqlError</c> con el número deseado, agregado a un <c>SqlErrorCollection</c>, materializado con
    /// <c>SqlException.CreateException(SqlErrorCollection, string)</c>.
    /// </para>
    /// <para>
    /// Reconstruido inspeccionando la metadata de <c>Microsoft.Data.SqlClient</c> 6.1.1 (la que trae
    /// transitivamente <c>Microsoft.EntityFrameworkCore.SqlServer</c> 10.0.10). Es deliberadamente
    /// frágil a un cambio de versión del driver: si esa forma interna cambia, este es el único archivo
    /// que se entera, y con un mensaje de excepción que lo dice.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Registro en memoria. Mismo patrón que <c>TopeAnualEnElAltaTests.RegistroDePrueba</c>.
    /// </summary>
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
