using GestionVacaciones.Data;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// El conjunto de catálogos que la semilla acepta escribir (mitigación R-03). No necesita motor: son
/// las reglas del guardarraíl, no su efecto sobre la base —eso lo verifica B3-T3—.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué el conjunto es inyectable y por qué eso no lo debilita.</b> Con el nombre incrustado en
/// la semilla, el guardarraíl no se podía ejercitar: los tests corren sobre
/// <c>GestionVacacionesV2_Test</c> y la semilla habría abortado siempre, de modo que B3-T1 y B3-T2
/// eran insatisfacibles y R-03 se quedaba sin evidencia. Las salidas fáciles —aceptar cualquier
/// catálogo, o saltear la comprobación al detectar que corre en un test— convierten la mitigación en
/// decoración. La salida sana es hacer el conjunto explícito y cerrado:
/// </para>
/// <list type="number">
///   <item><description>solo se construye por <see cref="CatalogosSembrables.Declarar"/>, que rechaza
///   el conjunto vacío, los nombres en blanco y el catálogo de la v1;</description></item>
///   <item><description>la composición productiva usa
///   <see cref="CatalogosSembrables.DeLaAplicacion"/>, que este archivo fija en exactamente
///   <c>GestionVacacionesV2</c>;</description></item>
///   <item><description>la comparación es exacta, así que ninguna base «parecida» entra por
///   prefijo.</description></item>
/// </list>
/// </remarks>
public sealed class CatalogosSembrablesTests
{
    [Fact]
    public void El_conjunto_de_la_aplicacion_es_exactamente_el_catalogo_de_la_v2()
    {
        // Es el conjunto que usa Program.cs. Si alguien le agrega un catálogo «para probar algo»,
        // este test lo dice antes de que la semilla escriba en la base equivocada.
        string[] esperados = ["GestionVacacionesV2"];

        Assert.Equal(esperados, CatalogosSembrables.DeLaAplicacion.Nombres);
        Assert.True(CatalogosSembrables.DeLaAplicacion.Admite("GestionVacacionesV2"));
    }

    [Fact]
    public void El_conjunto_de_la_aplicacion_no_admite_la_base_de_test_ni_ninguna_que_lo_contenga()
    {
        // Control positivo primero: sin él, este test lo cumpliría también un guardarraíl que no
        // admite nada, que es la forma que tienen las afirmaciones negativas de pasar sin decir nada.
        Assert.True(CatalogosSembrables.DeLaAplicacion.Admite("GestionVacacionesV2"));

        // La comparación tiene que ser por igualdad y no por prefijo: «GestionVacacionesV2_Test» y
        // «GestionVacacionesV2_Copia» empiezan igual y son otras bases. Un StartsWith acá dejaría
        // pasar cualquier base que alguien clonara con el nombre de la real por delante.
        Assert.False(CatalogosSembrables.DeLaAplicacion.Admite("GestionVacacionesV2_Test"));
        Assert.False(CatalogosSembrables.DeLaAplicacion.Admite("GestionVacacionesV2_Copia"));
        Assert.False(CatalogosSembrables.DeLaAplicacion.Admite("OtraBase"));
    }

    [Fact]
    public void No_admite_la_base_de_la_version_1_que_AGENTS_marca_como_intocable()
    {
        // Control positivo, por lo mismo que en el caso anterior.
        Assert.True(CatalogosSembrables.DeLaAplicacion.Admite("GestionVacacionesV2"));

        // La cicatriz documentada del proyecto: la v1 tiene su propio historial de migraciones y su
        // propia data. Sembrar cuatro empleados ficticios ahí son cuatro identidades fantasma.
        Assert.False(CatalogosSembrables.DeLaAplicacion.Admite("GestionVacaciones"));
    }

    [Fact]
    public void Declarar_rechaza_el_catalogo_de_la_version_1()
    {
        // El punto de inyección no puede ser la puerta trasera del guardarraíl: ni un test ni un host
        // futuro pueden declarar sembrable la base v1, por más que la escriban a mano.
        var excepcion = Assert.Throws<ArgumentException>(
            () => CatalogosSembrables.Declarar("GestionVacaciones"));

        Assert.Contains("GestionVacaciones", excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Declarar_rechaza_el_catalogo_de_la_version_1_sin_importar_como_se_escriba()
    {
        // SQL Server no distingue mayúsculas en el nombre de la base con la intercalación habitual:
        // comparar sensible a mayúsculas dejaría entrar «gestionvacaciones», que es la misma base.
        Assert.Throws<ArgumentException>(() => CatalogosSembrables.Declarar("gestionvacaciones"));
    }

    [Fact]
    public void Declarar_rechaza_un_conjunto_vacio()
    {
        // Un conjunto vacío no es peligroso —no admitiría nada—, pero sí es un error de composición
        // silencioso: la semilla nunca escribiría y nadie sabría por qué.
        Assert.Throws<ArgumentException>(() => CatalogosSembrables.Declarar());
    }

    [Fact]
    public void Declarar_rechaza_un_nombre_en_blanco()
    {
        // Un nombre vacío en la lista sí es peligroso: emparejaría con una conexión sin catálogo, que
        // es justo el caso en el que no se sabe adónde se escribe.
        Assert.Throws<ArgumentException>(() => CatalogosSembrables.Declarar("GestionVacacionesV2", "  "));
    }

    [Fact]
    public void No_admite_una_conexion_sin_catalogo()
    {
        var declarados = CatalogosSembrables.Declarar("GestionVacacionesV2_Test");

        // Control positivo: lo declarado sí entra, así que los tres «false» de abajo dicen algo.
        Assert.True(declarados.Admite("GestionVacacionesV2_Test"));

        Assert.False(declarados.Admite(null));
        Assert.False(declarados.Admite(string.Empty));
        Assert.False(declarados.Admite("   "));
    }

    [Fact]
    public void Admite_no_distingue_mayusculas_de_minusculas()
    {
        var declarados = CatalogosSembrables.Declarar("GestionVacacionesV2_Test");

        Assert.True(declarados.Admite("gestionvacacionesv2_test"));
    }
}
