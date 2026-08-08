using GestionVacaciones.Tests.Andamiaje;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// B2-T10, B2-T11, B2-T12 y B2-T13: el guardarraíl del fixture de integración (mitigación R-11).
/// </summary>
/// <remarks>
/// <para>
/// Al pasar de Testcontainers a la instancia SQL Server 2022 del entorno, el aislamiento dejó de ser
/// una propiedad de la infraestructura —el contenedor nacía vacío y moría al terminar— y pasó a
/// depender del código. Estos tests son ese código puesto a prueba: sin ellos, la regla #0 de
/// testing volvería a depender de la disciplina de quien escriba el próximo test.
/// </para>
/// <para>
/// Ninguna cadena de este archivo apunta a un servidor real ni lleva una credencial real: el
/// repositorio es público (R-02). «valor-ficticio-de-test» existe para comprobar que nunca aparece
/// en un mensaje de error.
/// </para>
/// <para>
/// La clase entra en <see cref="ColeccionDeEntornoDeProceso"/> porque uno de sus casos fija
/// <c>VACACIONES_CONNECTION_TEST</c>, que es estado global del proceso y el fixture de
/// <see cref="ColeccionDeBaseDeDatos"/> lee al arrancar.
/// </para>
/// </remarks>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class GuardarrailDeBaseDeTestTests
{
    private const string ValorFicticio = "valor-ficticio-de-test";

    /// <summary>Endpoint local sin nadie escuchando: el intento de conexión se rechaza de inmediato.</summary>
    private const string HostQueNoResponde = "127.0.0.1,14330";

    private static string CadenaHacia(string catalogo, string host = "127.0.0.1,1433") =>
        $"Server={host};Initial Catalog={catalogo};User ID=usuario-ficticio;Password={ValorFicticio};" +
        "Encrypt=True;TrustServerCertificate=True;Connect Timeout=2";

    [Fact]
    public async Task B2_T10_Un_catalogo_que_no_termina_en_Test_aborta_antes_de_abrir_la_conexion()
    {
        // El delegado es la costura: si el fixture llegara a abrirlo, este booleano quedaría en true y
        // la afirmación de abajo lo delataría. Sin la costura, «aborta antes de abrir» solo se podría
        // inferir de que la excepción llegó rápido, que es una medición, no una prueba.
        var seIntentoAbrirLaConexion = false;

        var excepcion = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BaseDeDatosDeTest.PrepararAsync(
                CadenaHacia("UnaBaseCualquiera"),
                _ =>
                {
                    seIntentoAbrirLaConexion = true;
                    return Task.CompletedTask;
                }));

        Assert.False(
            seIntentoAbrirLaConexion,
            "El fixture abrió la conexión antes de validar el catálogo: el guardarraíl llega tarde.");
        Assert.Contains("UnaBaseCualquiera", excepcion.Message, StringComparison.Ordinal);
        Assert.Contains(BaseDeDatosDeTest.SufijoExigido, excepcion.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GestionVacacionesV2")]
    [InlineData("GestionVacaciones")]
    [InlineData("gestionvacacionesv2")]
    public async Task B2_T11_Los_catalogos_intocables_abortan_nombrados_uno_por_uno(string catalogo)
    {
        // La regla del sufijo ya los rechazaría. Se los nombra igual porque son las dos bases que
        // AGENTS.md marca como cicatriz: si mañana alguien afloja el sufijo, esta lista sigue de pie.
        var seIntentoAbrirLaConexion = false;

        var excepcion = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BaseDeDatosDeTest.PrepararAsync(
                CadenaHacia(catalogo),
                _ =>
                {
                    seIntentoAbrirLaConexion = true;
                    return Task.CompletedTask;
                }));

        Assert.False(seIntentoAbrirLaConexion, "El fixture abrió la conexión contra un catálogo intocable.");
        Assert.Contains(catalogo, excepcion.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void B2_T11_La_denylist_no_alcanza_a_la_base_descartable_homonima()
    {
        // Sin este caso, escribir la denylist con «contiene» en vez de «es igual a» dejaría la suite
        // verde y bloquearía la única base contra la que los tests pueden correr.
        var catalogo = BaseDeDatosDeTest.ExigirCatalogoDescartable(CadenaHacia("GestionVacacionesV2_Test"));

        Assert.Equal("GestionVacacionesV2_Test", catalogo);
    }

    [Fact]
    public void B2_T12_Sin_cadena_configurada_los_tests_de_integracion_se_saltean_con_motivo()
    {
        var configuracionVacia = new ConfigurationBuilder().Build();

        var resolucion = BaseDeDatosDeTest.Resolver(configuracionVacia);

        // No lanza: una suite roja por falta de entorno enseña a ignorar el rojo.
        Assert.False(resolucion.Disponible);
        Assert.Null(resolucion.Valor);

        // El motivo tiene que ser accionable: nombra las dos fuentes posibles, como hace B1-T3 con la
        // cadena de la aplicación. «Se salteó» sin decir por qué es ruido.
        Assert.Contains(BaseDeDatosDeTest.VariableDeEntorno, resolucion.Motivo, StringComparison.Ordinal);
        Assert.Contains(BaseDeDatosDeTest.ClaveDeConfiguracion, resolucion.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public void B2_T12_El_motivo_llega_al_runner_como_salteo_y_no_como_fallo()
    {
        // Que el motivo exista no basta: lo que la spec exige es que la suite quede VERDE. Este caso
        // ejercita el mismo helper que usan los tests de integración y comprueba que lo que sale de
        // ahí es la señal de salteo de xUnit, no una aserción fallida.
        var resolucion = BaseDeDatosDeTest.Resolver(new ConfigurationBuilder().Build());

        // Se atrapa a mano y no con Record.Exception: xUnit trata la señal de salteo como control de
        // flujo y Record.Exception la deja pasar, con lo que este test se saltearía a sí mismo en vez
        // de afirmar nada.
        Exception? senal = null;
        try
        {
            BaseDeDatosDeTest.Saltear(resolucion.Motivo);
        }
        catch (Exception excepcion)
        {
            senal = excepcion;
        }

        // SkipException y no una aserción fallida: las dos derivan de XunitException, así que mirar
        // el tipo base no distinguiría el salteo del rojo, que es justo lo que este caso decide.
        Assert.IsType<Xunit.Sdk.SkipException>(senal);
        Assert.Contains(resolucion.Motivo, senal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task B2_T13_Con_la_instancia_inalcanzable_el_error_nombra_el_catalogo_pero_no_la_cadena()
    {
        // Espeja lo que B1-T4 fija para la cadena de la aplicación: el diagnóstico tiene que ser
        // accionable sin publicar credenciales. El catálogo se elige distinto del real para que la
        // afirmación sea nítida y no la satisfaga cualquier texto.
        var cadena = CadenaHacia("CatalogoInexistente_Test", HostQueNoResponde);

        var excepcion = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BaseDeDatosDeTest.PrepararAsync(cadena));

        Assert.Contains("CatalogoInexistente_Test", excepcion.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cadena, excepcion.ToString(), StringComparison.Ordinal);
        // Ni el mensaje propio ni el de la excepción encadenada pueden repetir la credencial: el
        // ToString() completo es lo que termina en un log (R-02, R-11, F-SAST-10).
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(excepcion.InnerException);
    }

    [Fact]
    public void La_variable_de_entorno_gana_sobre_los_user_secrets()
    {
        // La cadena de test se lee de dos fuentes y la precedencia importa: desde WSL la IP del host
        // de Windows cambia entre reinicios, así que la variable de entorno es el modo de corregirla
        // sin reescribir los secretos.
        const string CadenaDeLaVariable = "Server=host-de-la-variable,1433;Initial Catalog=DeLaVariable_Test";

        using var variable = new VariableDeEntornoTemporal(BaseDeDatosDeTest.VariableDeEntorno, CadenaDeLaVariable);

        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BaseDeDatosDeTest.ClaveDeConfiguracion] = "Server=host-de-los-secretos,1433;Initial Catalog=DeLosSecretos_Test",
            })
            .AddEnvironmentVariables()
            .Build();

        var resolucion = BaseDeDatosDeTest.Resolver(configuracion);

        Assert.True(resolucion.Disponible);
        Assert.Equal(CadenaDeLaVariable, resolucion.Valor);
    }

    [Fact]
    public void Sin_la_variable_de_entorno_se_usa_la_clave_de_los_user_secrets()
    {
        // Prueba que la fuente perdedora del caso anterior era alcanzable: sin esto, la precedencia
        // podría ser un artefacto de estar ignorando siempre los user-secrets.
        const string CadenaDeLosSecretos = "Server=host-de-los-secretos,1433;Initial Catalog=DeLosSecretos_Test";

        using var variable = new VariableDeEntornoTemporal(BaseDeDatosDeTest.VariableDeEntorno, null);

        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BaseDeDatosDeTest.ClaveDeConfiguracion] = CadenaDeLosSecretos,
            })
            .AddEnvironmentVariables()
            .Build();

        var resolucion = BaseDeDatosDeTest.Resolver(configuracion);

        Assert.True(resolucion.Disponible);
        Assert.Equal(CadenaDeLosSecretos, resolucion.Valor);
    }

    [Fact]
    public void La_cadena_resuelta_no_revela_su_valor_al_convertirse_en_texto()
    {
        // Mismo motivo que en CadenaDeConexionResuelta (B1): el ToString() que el compilador genera
        // para un record imprime todas sus propiedades, y esta lleva credenciales de una instancia con
        // datos reales (R-11).
        var resolucion = BaseDeDatosDeTest.Resolver(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [BaseDeDatosDeTest.ClaveDeConfiguracion] = CadenaHacia("Descartable_Test"),
                })
                .Build());

        Assert.DoesNotContain(ValorFicticio, resolucion.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValorFicticio, $"{resolucion}", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Una_cadena_sin_catalogo_se_rechaza_sin_revelar_su_valor()
    {
        var cadena = $"Server=127.0.0.1,1433;User ID=usuario-ficticio;Password={ValorFicticio}";

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => BaseDeDatosDeTest.ExigirCatalogoDescartable(cadena));

        Assert.Contains("Initial Catalog", excepcion.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Una_cadena_con_AttachDbFilename_se_rechaza_aunque_el_catalogo_sea_descartable()
    {
        // Con AttachDbFilename el «Initial Catalog» deja de nombrar una base del servidor: nombra la
        // que se adjunta desde un archivo del disco. Una cadena así pasa la regla del sufijo —el
        // catálogo termina en _Test— y EnsureDeleted dropearía la base adjuntada, borrando el .mdf y
        // el .ldf reales. El guardarraíl comprueba un nombre; esta clave hace que ese nombre no diga
        // nada sobre lo que se destruye.
        var cadena = "Server=127.0.0.1,1433;AttachDbFilename=C:\\datos\\produccion.mdf;" +
                     $"Initial Catalog=Cualquiera_Test;User ID=usuario-ficticio;Password={ValorFicticio}";

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => BaseDeDatosDeTest.ExigirCatalogoDescartable(cadena));

        Assert.Contains("AttachDbFilename", excepcion.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Una_cadena_con_User_Instance_se_rechaza_aunque_el_catalogo_sea_descartable()
    {
        // «User Instance=True» desvía la conexión a una instancia SQL Express aparte, levantada para
        // el usuario y con sus propios archivos. El catálogo que se valide acá no describe lo que se
        // borraría allá: es el mismo agujero que AttachDbFilename, por otra puerta.
        var cadena = "Server=.\\SQLEXPRESS;Initial Catalog=Cualquiera_Test;User Instance=True;" +
                     "Integrated Security=True";

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => BaseDeDatosDeTest.ExigirCatalogoDescartable(cadena));

        Assert.Contains("User Instance", excepcion.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Construir_un_contexto_suelto_contra_un_catalogo_real_tambien_aborta()
    {
        // El guardarraíl no puede vivir solo en PrepararAsync: CrearContexto(cadena) es la primitiva
        // desde la que EnsureDeletedAsync queda a una llamada de distancia, y un test futuro que se
        // arme el contexto por su cuenta no pasaría por PrepararAsync en ningún momento. Validar acá
        // hace que ningún contexto de esta suite pueda apuntar a una base que no se declaró
        // descartable, sin depender de que nadie se olvide.
        var excepcion = Assert.Throws<InvalidOperationException>(
            () => BaseDeDatosDeTest.CrearContexto(CadenaHacia("GestionVacacionesV2")));

        Assert.Contains("GestionVacacionesV2", excepcion.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Una_cadena_no_interpretable_se_rechaza_sin_revelar_su_valor()
    {
        // «Servr» es una errata verosímil. SqlConnectionStringBuilder rechaza palabras clave
        // desconocidas, y ese rechazo tiene que llegar como un aborto del guardarraíl y no como un
        // fallo de conexión a mitad de la corrida.
        var cadena = $"Servr=127.0.0.1;Initial Catalog=Descartable_Test;Password={ValorFicticio}";

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => BaseDeDatosDeTest.ExigirCatalogoDescartable(cadena));

        Assert.DoesNotContain(cadena, excepcion.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
