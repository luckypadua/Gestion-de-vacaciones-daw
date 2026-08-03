using GestionVacaciones.Data.Services;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// B5-T1 y B5-T2: el conteo de días corridos, que es la unidad del dominio según el glosario de
/// <c>AGENTS.md</c> —días de calendario, no hábiles— y que es <b>inclusivo</b>.
/// </summary>
/// <remarks>
/// No tocan la base ni el reloj: son aritmética de fechas. El PRD registra como riesgo que la interfaz
/// y el servicio calculen distinto y persistan un número que no es el que se mostró; el cálculo vive en
/// un punto único y estos tests fijan lo que ese punto hace.
/// </remarks>
public sealed class CalculoDeDiasCorridosTests
{
    [Fact]
    public void B5_T1_Del_3_al_5_de_enero_son_tres_dias_corridos()
    {
        // El ejemplo textual de la spec y de la tercera check constraint del Bloque 2: si el conteo
        // fuera exclusivo daría 2, y CK_Solicitud_DiasCoincidenConPeriodo —que compara contra
        // DATEDIFF + 1— rechazaría la fila. El número que se muestra (AC-01) y el que se guarda
        // (AC-04) tienen que ser este.
        Assert.Equal(3, CalculadorDeDiasCorridos.Contar(new DateOnly(2026, 1, 3), new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void B5_T2_Un_periodo_de_un_solo_dia_es_un_dia_corrido()
    {
        // El borde inferior del período válido, y el caso que distingue el conteo inclusivo del
        // exclusivo de la forma más visible: exclusivo daría 0, que CK_Solicitud_DiasPositivos rechaza.
        // Pedirse un solo día de vacaciones es además el caso más común de todos.
        var unSoloDia = new DateOnly(2026, 1, 3);

        Assert.Equal(1, CalculadorDeDiasCorridos.Contar(unSoloDia, unSoloDia));
    }

    [Theory]
    [InlineData(2026, 1, 31, 2026, 2, 1, 2)]
    [InlineData(2026, 12, 31, 2027, 1, 1, 2)]
    [InlineData(2024, 2, 28, 2024, 3, 1, 3)]
    [InlineData(2026, 1, 1, 2026, 12, 31, 365)]
    public void El_conteo_cruza_meses_anios_y_el_29_de_febrero(
        int anioInicio, int mesInicio, int diaInicio,
        int anioFin, int mesFin, int diaFin,
        int diasEsperados)
    {
        // Complemento de B5-T1 y B5-T2, que se quedan dentro de un mismo mes: un cálculo hecho con
        // aritmética de días del mes los aprobaría a los dos y fallaría en todos estos. El caso de 2024
        // incluye un 29 de febrero, que es el que se pierde si el conteo pasa por algo que no sea la
        // diferencia entre fechas.
        //
        // El período de un año entero está acá a propósito: el tope anual de 14 días es de FEAT-001b, y
        // en FEAT-001a un período de duración arbitraria se acepta. El calculador no opina sobre el
        // largo.
        var dias = CalculadorDeDiasCorridos.Contar(
            new DateOnly(anioInicio, mesInicio, diaInicio),
            new DateOnly(anioFin, mesFin, diaFin));

        Assert.Equal(diasEsperados, dias);
    }

    [Fact]
    public void Un_periodo_invertido_no_tiene_dias_corridos_y_el_calculador_lo_rechaza()
    {
        // El calculador NO devuelve 0 ni un negativo para un período invertido: cualquiera de los dos
        // sería un número mostrable en pantalla y persistible en la columna, y las check constraints del
        // Bloque 2 lo rechazarían recién en la base. La precondición se afirma acá.
        //
        // Que esto lance no le devuelve la comparación de fechas a la interfaz (R-10): el orden de las
        // fechas lo decide SolicitudesService, que valida ANTES de contar y por eso nunca llega hasta
        // acá con un período invertido.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CalculadorDeDiasCorridos.Contar(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 3)));
    }
}
