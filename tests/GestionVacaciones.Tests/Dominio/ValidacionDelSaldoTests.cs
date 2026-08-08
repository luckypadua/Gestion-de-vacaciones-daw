using System.Reflection;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Bloque 2 de FEAT-001b: los caminos del saldo que <b>no llegan a la base</b> —validación de entrada,
/// falta de identidad y fallo de persistencia— y los guardarraíles de superficie que sostienen R-13 y
/// R-15 del modelo de amenazas.
/// </summary>
/// <remarks>
/// Separados de <see cref="SaldoDelAnioTests"/> por la misma razón que
/// <c>ValidacionDelAltaTests</c> lo está de <c>AltaDeSolicitudTests</c>: estos casos montan
/// <see cref="FabricaQueNadieDebeUsar"/>, que <b>lanza en cuanto alguien pide un contexto</b>. No es un
/// doble para simular la base: es la afirmación de que el caso no la necesita, y de que la validación
/// ocurrió <b>antes</b> de que hubiera un viaje capaz de costar algo.
/// </remarks>
public sealed class ValidacionDelSaldoTests
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 30, TimeSpan.FromHours(-3));

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Una_lista_de_anios_vacia_no_consulta_la_base()
    {
        // Sin años que calcular no hay nada que preguntar. Con la fábrica que lanza, «devuelve vacío»
        // y «devuelve vacío después de abrir un contexto para nada» dejan de verse igual.
        var saldos = await ServicioSinBase().DeLosAniosAsync([], Cancelacion);

        Assert.Empty(saldos);
    }

    [Fact]
    public async Task Una_lista_de_anios_vacia_sin_identidad_lanza_en_vez_de_contestar()
    {
        // El retorno temprano por lista vacía era el único camino en que el servicio contestaba con
        // ÉXITO una pregunta sobre «el empleado actual» sin haber averiguado quién es. Una lista vacía
        // devuelta ante una identidad sin resolver se lee igual que «este empleado no tiene saldos que
        // mostrar», que es la confusión que AC-07 y AGENTS.md prohíben: sin identidad, sin datos y error
        // se ven parecidos en pantalla y significan cosas opuestas.
        //
        // Que siga sin abrir contexto lo sostiene la misma fábrica que lanza: resolver la identidad no
        // cuesta un viaje a la base.
        var servicio = new SaldoService(
            new FabricaQueNadieDebeUsar(),
            IdentidadDePrueba.SinSeleccionar(),
            new PermisosService(),
            new TiempoFijo(_instanteFijo));

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(
            () => servicio.DeLosAniosAsync([], Cancelacion));
    }

    [Fact]
    public async Task Mas_de_dos_anios_por_llamada_lanza_sin_consultar()
    {
        // Mitigación de R-15. El conjunto de años lo determina el período que escribe el empleado y
        // DateOnly llega hasta el 9999: sin este límite, una petición puede pedir un rango que ningún
        // índice acota, desde un circuito que no exige credenciales. Que lance ANTES de abrir contexto
        // es lo que lo vuelve un control y no una optimización.
        var excepcion = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ServicioSinBase().DeLosAniosAsync([2025, 2026, 2027], Cancelacion));

        Assert.Equal("anios", excepcion.ParamName);
    }

    [Theory]
    [InlineData(1, 9999)]
    [InlineData(2026, 2028)]
    [InlineData(2026, 2026)]
    public async Task Dos_anios_no_consecutivos_lanzan_sin_consultar(int primero, int segundo)
    {
        // La otra mitad de R-15, y la que el límite de cantidad NO cubría: contar años no acota el rango.
        // Con [1, 9999] la guarda de cantidad pasa —son dos— y el filtro que viaja a la base queda
        // «entre el 1-ene-0001 y el 31-dic-9999», que es un WHERE sin restricción temporal sobre todo el
        // historial del empleado: exactamente la consulta que R-15 existe para impedir.
        //
        // Consecutivos es lo que el propio razonamiento del límite implica: dos es todo lo que necesita
        // un período legítimo porque uno de tres años contendría un año íntegro. Un par repetido
        // —[2026, 2026]— tampoco es el conjunto de años de ningún período, y admitirlo sería devolver dos
        // veces el mismo saldo.
        var excepcion = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ServicioSinBase().DeLosAniosAsync([primero, segundo], Cancelacion));

        Assert.Equal("anios", excepcion.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000)]
    public async Task Un_anio_invalido_lanza_indicando_el_parametro(int anioInvalido)
    {
        // Sin la guarda, el año 0 revienta al construir el 1 de enero con una excepción que no nombra
        // el parámetro, y quien la lea en un log va a buscar el problema en las fechas de una solicitud
        // en vez de en el año pedido.
        var excepcion = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ServicioSinBase().DeLosAniosAsync([anioInvalido], Cancelacion));

        Assert.Equal("anios", excepcion.ParamName);
    }

    [Fact]
    public async Task Sin_identidad_el_saldo_no_se_degrada_a_cero()
    {
        // Camino triste, y la mitad de AC-07 que vive en el dominio. Un saldo de cero y un «no pudimos
        // averiguarlo» se ven igual en pantalla y significan lo contrario: devolver 0 acá le diría al
        // empleado que se quedó sin días cuando lo que pasa es que todavía no eligió quién es.
        var servicio = new SaldoService(
            new FabricaQueNadieDebeUsar(),
            IdentidadDePrueba.SinSeleccionar(),
            new PermisosService(),
            new TiempoFijo(_instanteFijo));

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(
            () => servicio.DelAnioEnCursoAsync(Cancelacion));
    }

    [Fact]
    public async Task Un_fallo_de_persistencia_se_propaga_en_vez_de_devolver_saldo_cero()
    {
        // La otra mitad del mismo principio, un piso más abajo: si la base no contesta, la excepción
        // sale del servicio sin catch. Un cero silencioso ante una base caída es exactamente la trampa
        // que AGENTS.md prohíbe — le dice al empleado que no le quedan días mientras el motor está
        // apagado. La fábrica que lanza hace de base caída.
        var excepcion = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServicioSinBase().DeLosAniosAsync([2026], Cancelacion));

        Assert.Equal(FabricaQueNadieDebeUsar.Marca, excepcion.Message);
    }

    /// <summary>
    /// Palabras con las que un parámetro nombraría al <b>sujeto</b> del saldo. No es solo «empleado»:
    /// <c>SaldoDeAsync(int sujeto, int anio)</c> y <c>SaldoDeAsync(int id, int anio)</c> son la misma
    /// referencia directa a objeto insegura con otro rótulo.
    /// </summary>
    private static readonly string[] _formasDeNombrarAlSujeto =
        ["empleado", "sujeto", "persona", "usuario", "legajo"];

    [Fact]
    public void Ninguna_firma_publica_acepta_un_empleadoId()
    {
        // Mitigación de R-13, y el test que este ticket le deja al siguiente. La forma natural de
        // escribir esto es SaldoDeAsync(int empleadoId, int anio), y con esa firma cualquier circuito
        // pregunta por cualquiera: la referencia directa a objeto insegura de siempre. Cuando llegue el
        // ticket del manager, agregar la sobrecarga «para reusar» va a ser la tentación obvia — y la
        // capacidad de ver a otro tiene que llegar CON su comprobación en PermisosService, no
        // destapando un parámetro.
        //
        // SE MIRAN TAMBIÉN LOS ESTÁTICOS, y no es un detalle: sin BindingFlags.Static, un
        // «public static Task SaldoDeAsync(int empleadoId, …)» pasaba en verde, y un método estático que
        // recibe el sujeto es peor todavía —no puede ni consultarle la identidad al proveedor, así que
        // el parámetro es su única fuente—.
        var parametros = typeof(SaldoService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                        | BindingFlags.DeclaredOnly)
            .SelectMany(metodo => metodo
                .GetParameters()
                .Select(parametro => $"{metodo.Name}({parametro.Name})"))
            .ToList();

        // Sin esto, un tipo renombrado o una consulta mal escrita dejarían la lista vacía y el test
        // pasaría en verde sin haber mirado ninguna firma.
        Assert.NotEmpty(parametros);

        var sospechosos = parametros.Where(NombraAlSujeto).ToList();

        Assert.True(
            sospechosos.Count == 0,
            $"SaldoService expone parámetros que nombran al sujeto del saldo: {string.Join(", ", sospechosos)}. " +
            "El sujeto sale de IEmpleadoActualProvider, nunca de un argumento (R-13).");
    }

    /// <summary>
    /// ¿El nombre del parámetro designa a una persona? Cubre las formas de rotularlo y también el
    /// <c>Id</c> a secas, que es como se escribe cuando el tipo ya se llama <c>SaldoService</c>.
    /// </summary>
    /// <remarks>
    /// <b>El constructor queda deliberadamente fuera del escaneo, y hay que decirlo porque la exclusión es
    /// portante.</b> <c>GetMethods</c> no devuelve constructores, y si los devolviera este test estaría en
    /// rojo hoy mismo: <c>SaldoService(…, IEmpleadoActualProvider empleadoActual, …)</c> contiene
    /// «empleado». Y estaría en rojo por la razón opuesta a la que R-13 persigue — ese parámetro es
    /// justamente la <i>sede única de la identidad</i>, inyectada por el contenedor, no un sujeto que
    /// elige quien llama. La superficie que R-13 vigila es la que un llamador puede rellenar en cada
    /// invocación; la del constructor la rellena la composición. Si alguien «mejora» este escaneo
    /// agregando <c>GetConstructors</c>, lo va a romper sin entender por qué.
    /// </remarks>
    private static bool NombraAlSujeto(string firma)
    {
        var nombre = firma[(firma.IndexOf('(', StringComparison.Ordinal) + 1)..].TrimEnd(')');

        return _formasDeNombrarAlSujeto.Any(forma => nombre.Contains(forma, StringComparison.OrdinalIgnoreCase))
            || nombre.Equals("id", StringComparison.OrdinalIgnoreCase)
            || nombre.EndsWith("Id", StringComparison.Ordinal);
    }

    [Fact]
    public void SaldoService_le_pregunta_a_PermisosService_antes_de_consultar()
    {
        // Guardarraíl de fuente, y hay que decir por qué no es un test de comportamiento: con la regla
        // de hoy —cada uno ve las propias— PermisosService NO PUEDE negar acá, porque el sujeto sale de
        // la misma identidad que consulta. Un doble que niegue tampoco es montable: PermisosService es
        // sealed y no hay interfaz. Un test que afirmara «si niega, no se degrada a cero» sería un test
        // que no puede fallar, y eso es peor que no tenerlo.
        //
        // LO QUE SE FIJA ES EL «ANTES», que es lo que pide la mitigación 2 de R-13: la comprobación
        // ocurre antes de TODA consulta. Por eso las posiciones son ordinales y no un Assert.Contains:
        // con la subcadena buscada en el archivo entero, mover la llamada después del ToListAsync
        // quedaba en verde. Y por eso se lee el código SIN COMENTARIOS: sobre el texto crudo,
        // comentar la línea también quedaba en verde, que es la peor forma de pasar un guardarraíl.
        var codigo = FuenteSinComentarios.DeUnArchivo(Path.Combine(
            RaizDelRepositorio.Localizar(),
            "src/GestionVacaciones.Data/Services/SaldoService.cs"));

        var comprobacion = codigo.IndexOf("_permisos.ExigirPoderVerLasSolicitudesDe(", StringComparison.Ordinal);
        var primeraConsulta = codigo.IndexOf("CreateDbContextAsync", StringComparison.Ordinal);

        Assert.True(
            comprobacion >= 0,
            "SaldoService no le pregunta a PermisosService: la línea no está, o está comentada. La " +
            "decisión de visibilidad tiene una única sede y el saldo también pasa por ella (R-13).");

        Assert.True(
            primeraConsulta >= 0,
            "No se encontró ninguna apertura de contexto en SaldoService, así que el orden que este test " +
            "afirma no significa nada. Si el acceso a datos cambió de forma, hay que reescribir el test.");

        Assert.True(
            comprobacion < primeraConsulta,
            $"La comprobación de permisos está en la posición {comprobacion} y la primera apertura de " +
            $"contexto en la {primeraConsulta}: se consulta la base antes de preguntar si se puede. La " +
            "mitigación 2 de R-13 exige la comprobación ANTES de toda consulta.");
    }

    private static SaldoService ServicioSinBase() =>
        new(new FabricaQueNadieDebeUsar(),
            IdentidadDePrueba.De(1),
            new PermisosService(),
            new TiempoFijo(_instanteFijo));
}
