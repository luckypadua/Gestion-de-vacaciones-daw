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
/// Bloque 3 de FEAT-001b: <see cref="SolicitudesService.CrearAsync"/> hace cumplir el tope anual —FR-02,
/// AC-02, AC-03— sobre cada año que el período afecta, dentro de una transacción serializable que cierra
/// la carrera de dos altas concurrentes (R-17).
/// </summary>
/// <remarks>
/// La mayoría necesita la instancia SQL Server 2022 del entorno: lo que se afirma es el resultado de
/// compararse contra un saldo real, y la propia carrera de <see cref="Dos_altas_concurrentes_no_superan_el_tope"/>
/// solo es observable contra el motor de verdad. Los dos casos que fijan el orden como contrato —
/// <see cref="El_tope_se_valida_despues_de_las_fechas_y_sin_tocar_la_base"/> y
/// <see cref="Un_periodo_de_mas_de_dos_anios_se_rechaza_sin_tocar_la_base"/>— usan
/// <see cref="FabricaQueNadieDebeUsar"/> y por eso no la necesitan: si el servicio abriera un contexto,
/// esa fábrica revienta antes de que el test tenga que preguntarle nada a la base.
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class TopeAnualEnElAltaTests
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 30, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public TopeAnualEnElAltaTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Una_solicitud_que_cabe_en_el_saldo_se_crea()
    {
        // Camino feliz: 5 días contra un saldo de 14, sin nada previo.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);
        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(4), Cancelacion);

        Assert.True(resultado.FueCreada, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Una_solicitud_que_supera_el_tope_se_rechaza_con_el_mensaje_de_AC_02()
    {
        // Un único año afectado: el literal exacto de AC-02, con el saldo real de ese año.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        // 10 ya aprobados: quedan 4 disponibles. Pedir 6 no entra.
        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 11), EstadoSolicitud.Aprobada);

        var inicio = tiempo.Hoy.AddDays(30);
        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(5), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.ComponerSaldoInsuficiente(4), resultado.MensajeDeError);
    }

    [Fact]
    public async Task Una_solicitud_a_caballo_que_supera_en_un_anio_se_rechaza_con_el_desglose_de_AC_03()
    {
        // Dos años afectados, y SOLO uno de los dos no alcanza: el desglose aparece igual (AC-03), con
        // el saldo real de cada año.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        // 2026 queda con 4 disponibles (10 ya usados). 2027 sigue con los 14 completos.
        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 11), EstadoSolicitud.Aprobada);

        // Del 25-dic-2026 al 3-ene-2027: 7 días en 2026 (25 al 31) y 3 en 2027 (1 al 3). 7 > 4
        // disponibles en 2026; 3 <= 14 en 2027.
        var resultado = await servicio.CrearAsync(
            new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 3), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(
            ErroresDeSolicitud.ComponerSaldoInsuficienteDeDosAnios(
                new SaldoDelAnio(2026, 10, 4), new SaldoDelAnio(2027, 0, 14)),
            resultado.MensajeDeError);
    }

    [Fact]
    public async Task Las_pendientes_reservan_saldo_y_bloquean_la_siguiente()
    {
        // FR-02 sobre el caso que el PRD marca como riesgo: una Pendiente reserva, no solo una Aprobada.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        var primeraInicio = tiempo.Hoy.AddDays(10);
        var primera = await servicio.CrearAsync(primeraInicio, primeraInicio.AddDays(9), Cancelacion); // 10 días

        Assert.True(primera.FueCreada, primera.MensajeDeError);

        // Quedan 4 disponibles. Pedir 5 más, en fechas que no se superponen, no entra.
        var segundaInicio = tiempo.Hoy.AddDays(60);
        var segunda = await servicio.CrearAsync(segundaInicio, segundaInicio.AddDays(4), Cancelacion); // 5 días

        Assert.False(segunda.FueCreada);
        Assert.Equal(ErroresDeSolicitud.ComponerSaldoInsuficiente(4), segunda.MensajeDeError);
    }

    [Fact]
    public async Task El_tope_se_valida_despues_de_las_fechas_y_sin_tocar_la_base()
    {
        // Fija el orden como contrato: con FabricaQueNadieDebeUsar, un período invertido se rechaza
        // SIN abrir contexto. Si la validación del tope corriera antes que las de fecha, esto revienta.
        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioSinBase(tiempo);

        var inicio = tiempo.Hoy.AddDays(10);

        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(-1), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.Equal(ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio, resultado.MensajeDeError);
    }

    [Fact]
    public async Task Justo_14_dias_se_acepta_y_15_no()
    {
        // El borde exacto del tope, con dos empleados frescos para que no se interfieran entre sí.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleadoDe14 = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var empleadoDe15 = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);

        var (servicio14, _) = ServicioPara(empleadoDe14.Id, tiempo);
        var inicio14 = tiempo.Hoy.AddDays(10);
        var resultado14 = await servicio14.CrearAsync(inicio14, inicio14.AddDays(13), Cancelacion); // 14 días

        Assert.True(resultado14.FueCreada, resultado14.MensajeDeError);

        var (servicio15, _) = ServicioPara(empleadoDe15.Id, tiempo);
        var inicio15 = tiempo.Hoy.AddDays(10);
        var resultado15 = await servicio15.CrearAsync(inicio15, inicio15.AddDays(14), Cancelacion); // 15 días

        Assert.False(resultado15.FueCreada);
        Assert.Equal(ErroresDeSolicitud.ComponerSaldoInsuficiente(14), resultado15.MensajeDeError);
    }

    [Fact]
    public async Task Dos_altas_concurrentes_no_superan_el_tope()
    {
        // R-17: dos CrearAsync en paralelo contra la instancia real, cada uno pidiendo 8 días (16 en
        // total, por encima de 14). La transacción serializable tiene que impedir que las dos pasen. El
        // conflicto de serialización (R-16) no se reintenta: una de las dos puede terminar en excepción
        // en lugar de en un rechazo, y eso también es una forma válida de "no superar el tope".
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicioA, _) = ServicioPara(empleado.Id, tiempo);
        var (servicioB, _) = ServicioPara(empleado.Id, tiempo);

        var inicio = tiempo.Hoy.AddDays(10);

        // Períodos distintos (no superpuestos): lo único que está en juego acá es el TOPE, no la
        // superposición, que es de otro ticket (FEAT-001c).
        var tareaA = IntentarAsync(servicioA, inicio, inicio.AddDays(7)); // 8 días
        var tareaB = IntentarAsync(servicioB, inicio.AddDays(30), inicio.AddDays(37)); // 8 días

        var resultados = await Task.WhenAll(tareaA, tareaB);

        var diasCreados = resultados
            .Where(resultado => resultado.Resultado?.FueCreada == true)
            .Sum(_ => 8);

        Assert.True(
            diasCreados <= TopeAnual.Dias,
            $"Las dos altas concurrentes sumaron {diasCreados} días creados, por encima del tope de " +
            $"{TopeAnual.Dias}. Resultados: {string.Join(" | ", resultados.Select(r => r.Describir()))}");

        // Al menos una tuvo que sobrevivir: si las dos hubieran fallado, el caso no estaría probando la
        // mitigación de la carrera, sino otra cosa.
        Assert.Contains(resultados, r => r.Resultado?.FueCreada == true);
    }

    [Fact]
    public async Task Un_periodo_de_mas_de_dos_anios_se_rechaza_sin_tocar_la_base()
    {
        // R-15: un período de más de dos años calendario se rechaza en memoria, sin consultar el saldo.
        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioSinBase(tiempo);

        var inicio = tiempo.Hoy.AddDays(1);

        // De este año a dos años después: tres años calendario distintos.
        var resultado = await servicio.CrearAsync(inicio, inicio.AddYears(2), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.StartsWith(
            "No dispones de días suficientes.", resultado.MensajeDeError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_fallo_al_guardar_revierte_la_transaccion_y_se_propaga()
    {
        // El INSERT final se rompe con un interceptor de EF Core: la excepción se propaga y no queda
        // ninguna fila, ni siquiera a medias.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioConFabricaEspecial(
            empleado.Id,
            tiempo,
            comando => comando.Contains("INSERT", StringComparison.OrdinalIgnoreCase)
                && comando.Contains("Solicitudes", StringComparison.OrdinalIgnoreCase));

        var inicio = tiempo.Hoy.AddDays(10);

        var excepcion = await Record.ExceptionAsync(
            () => servicio.CrearAsync(inicio, inicio.AddDays(2), Cancelacion));

        Assert.NotNull(excepcion);
        Assert.Contains(InterceptorDeFalloInducido.Marca, excepcion.ToString(), StringComparison.Ordinal);

        await using var relectura = _baseDeDatos.CrearContexto();
        Assert.False(
            await relectura.Solicitudes.AnyAsync(solicitud => solicitud.EmpleadoId == empleado.Id, Cancelacion),
            "Quedó una solicitud persistida pese a que el guardado falló.");
    }

    [Fact]
    public async Task Una_excepcion_de_base_que_no_es_de_validacion_se_propaga_sin_convertirse_en_rechazo()
    {
        // El conflicto de serialización de R-16, simulado en vez de provocado: lo que se prueba es la
        // DECISIÓN de no envolver el bloque en un catch que lo convierta en un Rechazada, no que SQL
        // Server sepa detectar interbloqueos. Se rompe una lectura (no el guardado), para cubrir también
        // la mitad de la transacción que lee.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var servicio = ServicioConFabricaEspecial(
            empleado.Id,
            tiempo,
            comando => comando.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && comando.Contains("Solicitudes", StringComparison.OrdinalIgnoreCase));

        var inicio = tiempo.Hoy.AddDays(10);

        var excepcion = await Record.ExceptionAsync(
            () => servicio.CrearAsync(inicio, inicio.AddDays(2), Cancelacion));

        // NO es un ResultadoDelAlta.Rechazada: es una excepción de verdad, propagada tal cual.
        Assert.NotNull(excepcion);
        Assert.Contains(InterceptorDeFalloInducido.Marca, excepcion.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_ToString_del_resultado_no_expone_el_saldo()
    {
        // R-14: un saldo junto al EmpleadoId que ya se registra identifica cuánto se ausentó una
        // persona. El rechazo por tope no puede llevarlo al diagnóstico.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, _) = ServicioPara(empleado.Id, tiempo);

        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 16), EstadoSolicitud.Aprobada); // 15 días: saldo 0

        var inicio = tiempo.Hoy.AddDays(20);
        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(1), Cancelacion);

        Assert.False(resultado.FueCreada);
        Assert.NotEmpty(resultado.MensajeDeError);

        var descripcion = resultado.ToString();

        Assert.DoesNotContain(resultado.MensajeDeError, descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task El_rechazo_por_tope_queda_registrado_sin_el_mensaje()
    {
        // R-18: hay una entrada de log con EmpleadoId y el año afectado, y esa entrada NO lleva el
        // mensaje compuesto (que es lo que R-14 saca del diagnóstico).
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var tiempo = new TiempoFijo(_instanteFijo);
        var (servicio, registro) = ServicioPara(empleado.Id, tiempo);

        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 11), EstadoSolicitud.Aprobada); // 10 días: saldo 4

        var inicio = tiempo.Hoy.AddDays(20);
        var resultado = await servicio.CrearAsync(inicio, inicio.AddDays(5), Cancelacion); // pide 6, solo hay 4

        Assert.False(resultado.FueCreada);

        var entrada = Assert.Single(registro.Entradas, entrada => entrada.Nivel == LogLevel.Information
            && entrada.Mensaje.Contains(empleado.Id.ToString(), StringComparison.Ordinal)
            && entrada.Mensaje.Contains("2026", StringComparison.Ordinal));

        Assert.DoesNotContain(resultado.MensajeDeError, entrada.Mensaje, StringComparison.Ordinal);
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
    /// Servicio con identidad resuelta y una fábrica que revienta si alguien la usa —para
    /// <see cref="SolicitudesService"/> y para <see cref="SaldoService"/> por igual—: es «este caso no
    /// llega a la base» hecho código, igual que en <c>ValidacionDelAltaTests</c>.
    /// </summary>
    private (SolicitudesService Servicio, RegistroDePrueba Registro) ServicioSinBase(TimeProvider tiempo)
    {
        var fabrica = new FabricaQueNadieDebeUsar();
        var identidad = IdentidadDePrueba.De(7);
        var permisos = new PermisosService();
        var saldo = new SaldoService(fabrica, identidad, permisos, tiempo);
        var registro = new RegistroDePrueba();

        return (new SolicitudesService(fabrica, identidad, permisos, tiempo, saldo, registro), registro);
    }

    private SolicitudesService ServicioConFabricaEspecial(
        int empleadoId, TimeProvider tiempo, Func<string, bool> debeFallar)
    {
        // La cadena sale de un contexto del fixture y no de la configuración: así el guardarraíl del
        // sufijo «_Test» ya se aplicó, igual que en SemillaDeDesarrolloTests.FabricaQueFallaAlEscribir.
        using var sonda = _baseDeDatos.CrearContexto();
        var cadena = sonda.Database.GetConnectionString()!;

        var fabricaConInterceptor = new FabricaDeContextosConInterceptor(cadena, debeFallar);

        var identidad = IdentidadDePrueba.De(empleadoId);
        var permisos = new PermisosService();

        // SaldoService recibe la MISMA fábrica con interceptor: así "romper una lectura" también puede
        // romper la suya, sin que el test tenga que saber cuál de las dos hizo la consulta que falló.
        var saldo = new SaldoService(fabricaConInterceptor, identidad, permisos, tiempo);

        return new SolicitudesService(
            fabricaConInterceptor, identidad, permisos, tiempo, saldo, new RegistroDePrueba());
    }

    /// <summary>Resultado de un intento de alta concurrente: o el resultado, o la excepción que lanzó.</summary>
    private sealed record IntentoDeAlta(ResultadoDelAlta? Resultado, Exception? Excepcion)
    {
        public string Describir() => Resultado is not null
            ? $"FueCreada={Resultado.FueCreada}"
            : $"Excepción: {Excepcion!.GetType().Name}";
    }

    private static async Task<IntentoDeAlta> IntentarAsync(
        SolicitudesService servicio, DateOnly inicio, DateOnly fin)
    {
        try
        {
            return new IntentoDeAlta(await servicio.CrearAsync(inicio, fin, Cancelacion), null);
        }
        catch (Exception excepcion)
        {
            // R-16: el conflicto de serialización se propaga y NO se reintenta. Que una de las dos
            // ramas termine acá, en vez de en un ResultadoDelAlta.Rechazada, es exactamente lo esperado.
            return new IntentoDeAlta(null, excepcion);
        }
    }

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
    /// Rompe el primer comando que cumpla el predicado. El fallo ocurre en el motor —vía
    /// <c>AddInterceptors</c>— y no en un método del servicio que el test tendría que conocer, igual
    /// que <c>SemillaDeDesarrolloTests.InterceptorQueRompeLaEscritura</c>.
    /// </summary>
    private sealed class InterceptorDeFalloInducido(Func<string, bool> debeFallar) : DbCommandInterceptor
    {
        public const string Marca = "fallo-inducido-por-TopeAnualEnElAltaTests";

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
    /// Registro en memoria: existe porque <see cref="El_rechazo_por_tope_queda_registrado_sin_el_mensaje"/>
    /// afirma sobre lo que quedó en el log (R-18), y un <c>NullLogger</c> lo dejaría sin verificar. Mismo
    /// patrón que <c>SemillaDeDesarrolloTests.RegistroDePrueba</c>.
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
