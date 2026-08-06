using GestionVacaciones.Data.Services;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Bloque 1 de FEAT-001b: el reparto de los días de un período entre los años calendario que abarca
/// (FR-01, AC-01).
/// </summary>
/// <remarks>
/// No tocan la base ni el reloj: es aritmética de fechas, y el año se le pasa en vez de que lo averigüe.
/// Esa es justamente la propiedad que hace verificable el caso del cruce de año sin depender de cuándo
/// se ejecute la suite — el PRD lo registra como riesgo, porque un cálculo que dependiera de la fecha
/// real fallaría recién el 28 de diciembre.
/// </remarks>
public sealed class ImputacionPorAnioTests
{
    [Fact]
    public void Un_periodo_a_caballo_de_dos_anios_imputa_a_cada_uno_lo_suyo()
    {
        // El ejemplo numérico de AC-01, literal: 28-dic-2026 al 5-ene-2027 son 9 días corridos, 4 de
        // 2026 (28, 29, 30 y 31) y 5 de 2027 (del 1 al 5). Es el caso que obligó a decidir contra qué
        // año cuenta un período que cae en los dos, y el que este ticket existe para resolver.
        var inicio = new DateOnly(2026, 12, 28);
        var fin = new DateOnly(2027, 1, 5);

        Assert.Equal(4, ImputacionPorAnio.DiasEnElAnio(inicio, fin, 2026));
        Assert.Equal(5, ImputacionPorAnio.DiasEnElAnio(inicio, fin, 2027));
    }

    [Theory]
    [InlineData(2026, 12, 28, 2027, 1, 5)]
    [InlineData(2026, 1, 1, 2026, 12, 31)]
    [InlineData(2026, 3, 15, 2026, 3, 15)]
    [InlineData(2024, 2, 20, 2024, 3, 5)]
    [InlineData(2026, 12, 31, 2027, 1, 1)]
    public void Los_dias_imputados_suman_los_dias_corridos_del_periodo(
        int anioInicio, int mesInicio, int diaInicio,
        int anioFin, int mesFin, int diaFin)
    {
        // AC-01 en su forma general, y la propiedad que impide el error más caro de este cálculo: que
        // un día se pierda o se cuente dos veces en la frontera del 31 de diciembre. Lo que este
        // aserto NO puede atrapar es el error compensado —el 1 de enero imputado a 2026 en vez de a
        // 2027—, porque conserva la suma: ese lo mata el test anterior, que fija 4 y 5 por separado.
        // Hacen falta los dos, y en ese orden de fuerzas.
        var inicio = new DateOnly(anioInicio, mesInicio, diaInicio);
        var fin = new DateOnly(anioFin, mesFin, diaFin);

        var repartidos = ImputacionPorAnio
            .AniosAbarcados(inicio, fin)
            .Sum(anio => ImputacionPorAnio.DiasEnElAnio(inicio, fin, anio));

        Assert.Equal(CalculadorDeDiasCorridos.Contar(inicio, fin), repartidos);
    }

    [Fact]
    public void Un_periodo_dentro_de_un_solo_anio_imputa_todo_a_ese_anio()
    {
        // El caso común, y el que tiene que seguir siendo trivial: sin cruce de año, imputar es no
        // hacer nada. Del 3 al 5 de enero son los mismos 3 días corridos de siempre.
        var inicio = new DateOnly(2026, 1, 3);
        var fin = new DateOnly(2026, 1, 5);

        Assert.Equal(3, ImputacionPorAnio.DiasEnElAnio(inicio, fin, 2026));
    }

    [Fact]
    public void Un_anio_ajeno_al_periodo_recibe_cero_dias()
    {
        // Camino triste. Preguntar por un año que el período no toca es normal —el saldo del año en
        // curso se consulta con independencia de qué período se esté tipeando— y la respuesta es 0, no
        // una excepción. Se comprueban los dos lados: el año anterior y el posterior.
        var inicio = new DateOnly(2026, 6, 1);
        var fin = new DateOnly(2026, 6, 10);

        Assert.Equal(0, ImputacionPorAnio.DiasEnElAnio(inicio, fin, 2025));
        Assert.Equal(0, ImputacionPorAnio.DiasEnElAnio(inicio, fin, 2027));
    }

    [Fact]
    public void Un_anio_bisiesto_no_altera_el_conteo()
    {
        // 2024 es bisiesto y este período incluye el 29 de febrero. Un reparto hecho con aritmética de
        // meses lo perdería; delegar en Contar, que resta números de día absolutos, no puede perderlo.
        var inicio = new DateOnly(2024, 2, 28);
        var fin = new DateOnly(2024, 3, 1);

        Assert.Equal(3, ImputacionPorAnio.DiasEnElAnio(inicio, fin, 2024));
    }

    [Fact]
    public void Un_anio_completo_bisiesto_imputa_366_dias()
    {
        // El complemento del anterior sobre el año entero: 2024 imputa 366 y 2026, 365. Fija que el
        // recorte contra [1-ene, 31-dic] no asume un largo de año fijo.
        Assert.Equal(366, ImputacionPorAnio.DiasEnElAnio(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31), 2024));
        Assert.Equal(365, ImputacionPorAnio.DiasEnElAnio(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 2026));
    }

    [Fact]
    public void Un_periodo_invertido_lanza()
    {
        // Camino triste, y la razón de que lance en vez de devolver 0: un 0 sería un número mostrable y
        // persistible, y el rechazo llegaría recién desde la base como un error de la aplicación en vez
        // de como un período mal escrito. La precondición es la misma que la de CalculadorDeDiasCorridos
        // a propósito: dos hermanas que se comportan distinto ante la misma entrada inválida son una
        // trampa.
        var inicio = new DateOnly(2026, 1, 5);
        var fin = new DateOnly(2026, 1, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => ImputacionPorAnio.DiasEnElAnio(inicio, fin, 2026));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImputacionPorAnio.AniosAbarcados(inicio, fin));
    }

    [Fact]
    public void Un_anio_fuera_del_rango_de_DateOnly_lanza_indicando_el_parametro()
    {
        // Camino triste. Sin esta guarda, el año 0 revienta al construir el 1 de enero con una excepción
        // que no dice de qué parámetro vino, y quien la lea en un log va a buscar el error en las fechas
        // del período en vez de en el año pedido.
        var inicio = new DateOnly(2026, 6, 1);
        var fin = new DateOnly(2026, 6, 10);

        var porDebajo = Assert.Throws<ArgumentOutOfRangeException>(
            () => ImputacionPorAnio.DiasEnElAnio(inicio, fin, 0));
        var porEncima = Assert.Throws<ArgumentOutOfRangeException>(
            () => ImputacionPorAnio.DiasEnElAnio(inicio, fin, 10_000));

        Assert.Equal("anio", porDebajo.ParamName);
        Assert.Equal("anio", porEncima.ParamName);
    }

    [Theory]
    [InlineData(2026, 6, 1, 2026, 6, 10, new[] { 2026 })]
    [InlineData(2026, 12, 28, 2027, 1, 5, new[] { 2026, 2027 })]
    [InlineData(2025, 12, 31, 2027, 1, 1, new[] { 2025, 2026, 2027 })]
    public void AniosAbarcados_devuelve_los_anios_en_orden_ascendente(
        int anioInicio, int mesInicio, int diaInicio,
        int anioFin, int mesFin, int diaFin,
        int[] esperados)
    {
        // El tercer caso abarca tres años y es legítimo acá: este tipo no sabe nada del tope anual. Que
        // un período así se rechace sin llegar a consultar la base es decisión de SolicitudesService, y
        // se fija en los tests del bloque 3 — no acá.
        var anios = ImputacionPorAnio.AniosAbarcados(
            new DateOnly(anioInicio, mesInicio, diaInicio),
            new DateOnly(anioFin, mesFin, diaFin));

        Assert.Equal(esperados, anios);
    }
}
