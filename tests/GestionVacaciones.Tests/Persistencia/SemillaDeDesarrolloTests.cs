using System.Data.Common;
using GestionVacaciones.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// B3-T1 a B3-T4: la semilla de desarrollo, contra la instancia SQL Server 2022 del entorno.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué estos tests necesitan motor.</b> Los cuatro afirman sobre lo que quedó —o no quedó—
/// escrito en la tabla <c>Empleados</c>, incluido el que comprueba que la semilla <b>aborta</b>: «no
/// escribió nada» solo se puede verificar mirando una base de verdad. Con un doble en memoria, el
/// sad path de R-03 pasaría en verde sin haber ejercitado ninguna escritura.
/// </para>
/// <para>
/// <b>El conjunto de catálogos sembrables es explícito e inyectable</b>, y por eso estos tests pueden
/// existir: la composición productiva declara <c>GestionVacacionesV2</c>
/// (<see cref="CatalogosSembrables.DeLaAplicacion"/>) y esta clase declara el catálogo descartable de
/// la corrida. El guardarraíl no se afloja ni se saltea en los tests —eso lo volvería decorativo—:
/// se lo alimenta con un conjunto distinto, y B3-T3 lo ejercita con el conjunto <b>de producción</b>
/// para comprobar que sigue rechazando todo lo que no sea la base de la aplicación.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class SemillaDeDesarrolloTests
{
    private readonly BaseDeDatosDeTest _baseDeDatos;

    public SemillaDeDesarrolloTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task B3_T1_Sobre_base_vacia_siembra_los_cuatro_empleados_con_Diego_designado_de_Ana()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var limpieza = new NominaDescartable(_baseDeDatos);

        var registro = new RegistroDePrueba();
        var semilla = new SeedDatos(FabricaDeLaCorrida(), CatalogosDeLaCorrida(), registro);

        var resultado = await semilla.SembrarAsync(Cancelacion);

        Assert.Equal(ResultadoDeSemilla.Sembrada, resultado);

        await using var contexto = _baseDeDatos.CrearContexto();
        var nomina = await contexto.Empleados.AsNoTracking().ToListAsync(Cancelacion);

        Assert.Equal(4, nomina.Count);

        var ana = Assert.Single(nomina, empleado => empleado.Correo == SeedDatos.CorreoDeAna);
        var diego = Assert.Single(nomina, empleado => empleado.Correo == SeedDatos.CorreoDeDiego);
        var bruno = Assert.Single(nomina, empleado => empleado.Correo == SeedDatos.CorreoDeBruno);
        var carla = Assert.Single(nomina, empleado => empleado.Correo == SeedDatos.CorreoDeCarla);

        // Lo que el bloque fija explícitamente: Diego es el designado de Ana. Es la relación circular
        // —Ana designa a Diego y Diego reporta a Ana— por la que la semilla es código y no HasData,
        // que no puede ordenar esos dos INSERT.
        Assert.Equal(diego.Id, ana.DesignadoId);
        Assert.Equal(ana.Id, diego.ManagerId);

        // Ana es la manager del equipo; nadie está por encima de ella en la nómina sembrada.
        Assert.Equal(ana.Id, bruno.ManagerId);
        Assert.Equal(ana.Id, carla.ManagerId);
        Assert.Null(ana.ManagerId);

        // Mitigación 2 de R-03: queda registrado sobre qué base se sembró. El STRIDE de la semilla
        // anota «sin registro de qué base se sembró ni cuándo» como parte del riesgo.
        Assert.Contains(registro.Entradas, entrada =>
            entrada.Nivel == LogLevel.Information &&
            entrada.Mensaje.Contains(_baseDeDatos.Catalogo, StringComparison.Ordinal));

        // R-12: los logs registran identificadores, nunca nombre ni correo. Acá los datos sembrados
        // son ficticios, pero el hábito que fija este test es el que aplica a los datos reales.
        string[] datosPersonales =
        [
            SeedDatos.NombreDeAna, SeedDatos.CorreoDeAna,
            SeedDatos.NombreDeDiego, SeedDatos.CorreoDeDiego,
            SeedDatos.NombreDeBruno, SeedDatos.CorreoDeBruno,
            SeedDatos.NombreDeCarla, SeedDatos.CorreoDeCarla,
        ];

        foreach (var dato in datosPersonales)
        {
            Assert.DoesNotContain(registro.Entradas, entrada =>
                entrada.Mensaje.Contains(dato, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public async Task B3_T2_Invocada_dos_veces_la_segunda_no_escribe_nada()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var limpieza = new NominaDescartable(_baseDeDatos);

        var semilla = new SeedDatos(FabricaDeLaCorrida(), CatalogosDeLaCorrida(), new RegistroDePrueba());

        Assert.Equal(ResultadoDeSemilla.Sembrada, await semilla.SembrarAsync(Cancelacion));
        var despuesDeLaPrimera = await NominaPorCorreoAsync();

        Assert.Equal(ResultadoDeSemilla.YaHabiaNomina, await semilla.SembrarAsync(Cancelacion));
        var despuesDeLaSegunda = await NominaPorCorreoAsync();

        // Se comparan los Id, no la cantidad: cuatro filas nuevas en lugar de las cuatro anteriores
        // darían el mismo total y no serían lo mismo. Los Id son identity, así que un borrado y
        // reinserción se delata acá.
        Assert.Equal(despuesDeLaPrimera, despuesDeLaSegunda);
        Assert.Equal(4, despuesDeLaSegunda.Count);
    }

    [Fact]
    public async Task B3_T3_Con_un_catalogo_que_no_esta_declarado_sembrable_aborta_y_la_tabla_queda_vacia()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var limpieza = new NominaDescartable(_baseDeDatos);

        // El conjunto permitido es EL DE PRODUCCIÓN —solo GestionVacacionesV2— y el contexto apunta a
        // la base descartable de la corrida. Es el caso de R-03 puesto del derecho: una semilla
        // apuntada a una base que no es la suya. La base existe, responde y está vacía, así que si el
        // guardarraíl no estuviera, escribiría los cuatro empleados sin ningún obstáculo.
        var registro = new RegistroDePrueba();
        var semilla = new SeedDatos(FabricaDeLaCorrida(), CatalogosSembrables.DeLaAplicacion, registro);

        var resultado = await semilla.SembrarAsync(Cancelacion);

        Assert.Equal(ResultadoDeSemilla.AbortadaPorCatalogo, resultado);

        await using var contexto = _baseDeDatos.CrearContexto();
        Assert.Equal(0, await contexto.Empleados.CountAsync(Cancelacion));

        // La spec pide registrar el nombre del catálogo encontrado, en nivel warning, y que la
        // aplicación siga arrancando: sin el nombre, quien depura no sabe adónde estaba apuntando.
        var advertencia = Assert.Single(registro.Entradas, entrada => entrada.Nivel == LogLevel.Warning);
        Assert.Contains(_baseDeDatos.Catalogo, advertencia.Mensaje, StringComparison.Ordinal);
    }

    [Fact]
    public async Task B3_T4_Si_persistir_falla_la_excepcion_se_propaga_y_no_queda_una_nomina_parcial()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var limpieza = new NominaDescartable(_baseDeDatos);

        // El fallo se induce en el motor, no en la semilla: un interceptor de EF Core rompe el
        // comando de escritura. Es la forma que menos acopla el test a la implementación —no exige
        // que la semilla llame a un método concreto ni que escriba en un número dado de viajes—, y
        // reproduce lo que dice la tabla de errores de la spec: «fallo al persistir la nómina».
        var registro = new RegistroDePrueba();
        var semilla = new SeedDatos(
            FabricaQueFallaAlEscribir(comando => comando.Contains("INSERT", StringComparison.OrdinalIgnoreCase)),
            CatalogosDeLaCorrida(),
            registro);

        var excepcion = await Record.ExceptionAsync(() => semilla.SembrarAsync(Cancelacion));

        // Que se propague es la mitad: una semilla que se traga el fallo deja el desarrollo en un
        // estado que nadie puede diagnosticar. La marca confirma que la excepción que llegó es la
        // inducida, y no otra cosa que pasaba por ahí.
        Assert.NotNull(excepcion);
        Assert.Contains(InterceptorQueRompeLaEscritura.Marca, excepcion.ToString(), StringComparison.Ordinal);

        await using var contexto = _baseDeDatos.CrearContexto();
        Assert.Equal(0, await contexto.Empleados.CountAsync(Cancelacion));

        // Mitigación 2 de R-03: la semilla registra sobre qué catálogo va a sembrar ANTES de escribir.
        // Esta aserción vive acá y no en B3-T1 porque este es el único punto donde «se registró» y «se
        // escribió» se pueden separar: en el camino feliz ocurren las dos cosas, así que la entrada
        // aparecería igual con el LogInformation movido detrás de la escritura y el test seguiría verde.
        // Acá la escritura falla y no queda ni una fila, así que la entrada solo puede estar presente si
        // se emitió antes: mover el registro después de escribir pone roja esta línea y ninguna otra.
        Assert.Contains(registro.Entradas, entrada =>
            entrada.Nivel == LogLevel.Information &&
            entrada.Mensaje.Contains(_baseDeDatos.Catalogo, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Si_falla_el_enlace_de_las_relaciones_tampoco_quedan_los_empleados_sueltos()
    {
        // Complemento de B3-T4, y el único que mira la transacción. La nómina se escribe en dos
        // viajes —insertar y después enlazar manager/designado, porque la relación Ana↔Diego es
        // circular y los Id son identity—, así que existe una ventana en la que la tabla tiene cuatro
        // empleados sin relaciones. Sin transacción, un fallo en el segundo viaje deja exactamente esa
        // nómina parcial: cuatro identidades que el selector aceptaría, sin manager ni designado.
        //
        // Es deliberadamente de caja blanca: la ventana no se ve desde afuera y, si desaparece porque
        // alguien logra escribir todo en un solo viaje, este test se pone rojo y hay que retirarlo a
        // conciencia. B3-T4, que es el que la spec exige, no depende de esto.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var limpieza = new NominaDescartable(_baseDeDatos);

        var semilla = new SeedDatos(
            FabricaQueFallaAlEscribir(comando => comando.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)),
            CatalogosDeLaCorrida(),
            new RegistroDePrueba());

        var excepcion = await Record.ExceptionAsync(() => semilla.SembrarAsync(Cancelacion));

        Assert.NotNull(excepcion);
        Assert.Contains(InterceptorQueRompeLaEscritura.Marca, excepcion.ToString(), StringComparison.Ordinal);

        await using var contexto = _baseDeDatos.CrearContexto();
        Assert.Equal(0, await contexto.Empleados.CountAsync(Cancelacion));
    }

    /// <summary>
    /// Los catálogos que esta corrida declara sembrables: el descartable que preparó el fixture, y
    /// ninguno más. Lo declara el test, no la semilla, que es lo que permite que el guardarraíl siga
    /// siendo exacto en producción en vez de aflojarse a «cualquier cosa que termine en _Test».
    /// </summary>
    private CatalogosSembrables CatalogosDeLaCorrida() => CatalogosSembrables.Declarar(_baseDeDatos.Catalogo);

    private IDbContextFactory<VacacionesDbContext> FabricaDeLaCorrida() =>
        new FabricaDeContextos(_baseDeDatos.CrearContexto);

    /// <summary>
    /// Fábrica cuyos contextos revientan en el comando que decida <paramref name="debeFallar"/>.
    /// </summary>
    private IDbContextFactory<VacacionesDbContext> FabricaQueFallaAlEscribir(Func<string, bool> debeFallar) =>
        new FabricaDeContextos(() =>
        {
            // La cadena se toma de un contexto del fixture y no de la configuración: así el
            // guardarraíl del sufijo «_Test» ya se aplicó y este test no puede apuntar a otra base
            // por más que arme sus propias opciones.
            using var sonda = _baseDeDatos.CrearContexto();
            var cadena = sonda.Database.GetConnectionString()!;

            return new VacacionesDbContext(
                new DbContextOptionsBuilder<VacacionesDbContext>()
                    .UseSqlServer(cadena)
                    .AddInterceptors(new InterceptorQueRompeLaEscritura(debeFallar))
                    .Options);
        });

    /// <summary>Correo → Id de los empleados de la nómina sembrada que hoy están en la base.</summary>
    private async Task<IReadOnlyDictionary<string, int>> NominaPorCorreoAsync()
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        return await contexto.Empleados
            .AsNoTracking()
            .Where(empleado => SeedDatos.CorreosDeLaNomina.Contains(empleado.Correo))
            .ToDictionaryAsync(empleado => empleado.Correo, empleado => empleado.Id, Cancelacion);
    }

    private sealed class FabricaDeContextos(Func<VacacionesDbContext> crear)
        : IDbContextFactory<VacacionesDbContext>
    {
        public VacacionesDbContext CreateDbContext() => crear();
    }

    /// <summary>
    /// Rompe el primer comando que cumpla el predicado. Se enchufa por <c>AddInterceptors</c>, que es
    /// la costura que ofrece EF Core: el fallo ocurre donde ocurriría de verdad —en el motor— y no en
    /// un método de la semilla que el test tendría que conocer.
    /// </summary>
    private sealed class InterceptorQueRompeLaEscritura(Func<string, bool> debeFallar) : DbCommandInterceptor
    {
        /// <summary>Marca que identifica al fallo como inducido por el test y no como uno real.</summary>
        public const string Marca = "fallo-de-persistencia-inducido-por-el-test";

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

        private void RomperSiCorresponde(DbCommand command)
        {
            if (debeFallar(command.CommandText))
            {
                throw new InvalidOperationException(Marca);
            }
        }
    }

    /// <summary>
    /// Registro en memoria. Existe porque dos afirmaciones del bloque son sobre el log —el catálogo
    /// encontrado en el aborto (R-03) y la ausencia de PII (R-12)— y un <c>NullLogger</c> las dejaría
    /// sin verificar.
    /// </summary>
    private sealed class RegistroDePrueba : ILogger<SeedDatos>
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

    /// <summary>
    /// Borra la nómina sembrada al salir del alcance, pase lo que pase con el test (regla #0 de
    /// testing: cada test crea sus datos y los limpia).
    /// </summary>
    private sealed class NominaDescartable(BaseDeDatosDeTest baseDeDatos) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var contexto = baseDeDatos.CrearContexto();

            var nomina = await contexto.Empleados
                .Where(empleado => SeedDatos.CorreosDeLaNomina.Contains(empleado.Correo))
                .ToListAsync(Cancelacion);

            if (nomina.Count == 0)
            {
                return;
            }

            // Primero se cortan las relaciones y después se borra: las dos FK son autorreferencias
            // con ON DELETE NO ACTION —el ciclo Ana↔Diego obliga a NO ACTION— y SQL Server rechaza
            // borrar una fila a la que otra sigue apuntando.
            foreach (var empleado in nomina)
            {
                empleado.ManagerId = null;
                empleado.DesignadoId = null;
            }

            await contexto.SaveChangesAsync(Cancelacion);

            contexto.Empleados.RemoveRange(nomina);
            await contexto.SaveChangesAsync(Cancelacion);
        }
    }
}
