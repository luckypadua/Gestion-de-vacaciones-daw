using System.Text.Json;
using GestionVacaciones.Web.Configuracion;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// B1-T2 (precedencia entre las dos fuentes de la cadena de conexión) y la exigencia de cifrado en
/// tránsito hacia SQL Server fuera de <c>Development</c> — mitigación 7 del modelo de amenazas
/// (R-02, F-TM-07).
/// </summary>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class CadenaDeConexionTests
{
    private const string CadenaDeLaVariable =
        "Server=host-de-la-variable,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=False";

    private const string CadenaDeLaConfiguracion =
        "Server=host-de-la-configuracion,1433;Initial Catalog=DeLaConfiguracion;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=False";

    // Ninguna de las cadenas de este archivo apunta a un servidor real ni lleva una credencial real:
    // el repositorio es público. «valor-ficticio-de-test» existe para comprobar que nunca aparece en
    // un mensaje de error ni en el texto de la cadena resuelta.
    private const string CadenaQueOmiteEncrypt =
        "Server=host-de-prueba,1433;Initial Catalog=GestionVacacionesV2;User ID=usuario-ficticio;" +
        "Password=valor-ficticio-de-test";

    // Encrypt declarado explícitamente en False. La cadena de arriba OMITE la clave: sin este fixture
    // nada distingue «ausente» de «presente afirmando que no», y agregar "false" al conjunto de
    // valores que activan el cifrado dejaría la suite en verde.
    private const string CadenaConCifradoDesactivadoExplicitamente =
        "Server=host-de-prueba,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=False;TrustServerCertificate=False";

    // La forma que usan los contenedores de SQL Server de los tests del Block 2: certificado
    // autofirmado, así que exigen TrustServerCertificate=True.
    private const string CadenaDeContenedorDeTest =
        "Server=127.0.0.1,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=True";

    // «Trust Server Certificate» con espacios: la grafía canónica de SqlClient. Sin este fixture,
    // borrar la línea que la consulta deja la suite entera en verde.
    private const string CadenaConCertificadoDeConfianzaConEspacios =
        "Server=host-de-prueba,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=True;Trust Server Certificate=True";

    // SqlClient lee «yes» como true: sin este fixture, borrarlo del conjunto de valores afirmativos
    // abre un bypass del certificado verificado y la suite no se entera.
    private const string CadenaConCertificadoDeConfianzaEnYes =
        "Server=host-de-prueba,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=yes";

    // «Database» es el alias habitual de «Initial Catalog». Sin este fixture, borrar esa rama deja la
    // suite verde y rompe a quien despliegue con Database=GestionVacacionesV2.
    private const string CadenaConDatabaseComoCatalogo =
        "Server=host-de-prueba,1433;Database=GestionVacacionesV2;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=False";

    // Catálogo presente pero en blanco. Con «Initial Catalog=» a secas el parser descarta la clave y
    // el caso recae en el de la cadena sin catálogo; entrecomillado la conserva, que es lo que
    // ejercita el guard de valor en blanco.
    private const string CadenaConCatalogoEnBlanco =
        "Server=host-de-prueba,1433;Initial Catalog=\"   \";Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=False";

    /// <summary>Forma que toma <c>ConnectionStrings:Vacaciones</c> como variable de entorno.</summary>
    private const string VariableDeConfiguracionEquivalente = "ConnectionStrings__Vacaciones";

    private const string ValorFicticio = "valor-ficticio-de-test";

    [Fact]
    public void B1_T2_Con_ambas_fuentes_presentes_gana_la_variable_de_entorno()
    {
        // La variable equivalente a la clave de configuración se neutraliza: AddEnvironmentVariables()
        // corre después de AddInMemoryCollection y, si existiera en el proceso, pisaría el valor en
        // memoria y este test fallaría sin que nadie hubiera tocado código.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaDeLaVariable);

        // La configuración se construye dentro del alcance: el proveedor de variables de entorno toma
        // su copia al cargar, no en cada lectura.
        var resuelta = CadenaDeConexion.Resolver(ConstruirConfiguracionCon(CadenaDeLaConfiguracion), esDesarrollo: false);

        Assert.Equal(CadenaDeLaVariable, resuelta);
    }

    [Fact]
    public void B1_T2_Sin_la_variable_de_entorno_se_usa_la_clave_de_configuracion()
    {
        // Prueba que la fuente perdedora del test anterior era alcanzable: sin esto, la precedencia
        // podría ser un artefacto de estar ignorando siempre la configuración.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var resuelta = CadenaDeConexion.Resolver(ConstruirConfiguracionCon(CadenaDeLaConfiguracion), esDesarrollo: false);

        Assert.Equal(CadenaDeLaConfiguracion, resuelta);
    }

    [Fact]
    public void Fuera_de_desarrollo_una_cadena_que_omite_la_clave_encrypt_se_rechaza()
    {
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => CadenaDeConexion.Resolver(ConstruirConfiguracionCon(CadenaQueOmiteEncrypt), esDesarrollo: false));

        Assert.Contains("Encrypt", excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fuera_de_desarrollo_una_cadena_con_encrypt_false_explicito_se_rechaza()
    {
        // Declarar «no cifres» no es lo mismo que no decir nada, y ambos deben terminar igual. Este
        // caso es el único que distingue las dos situaciones.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => CadenaDeConexion.Resolver(
                ConstruirConfiguracionCon(CadenaConCifradoDesactivadoExplicitamente), esDesarrollo: false));

        Assert.Contains("Encrypt", excepcion.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("True")]
    [InlineData("yes")]
    [InlineData("mandatory")]
    [InlineData("strict")]
    public void Fuera_de_desarrollo_se_acepta_cada_grafia_de_encrypt_que_activa_el_cifrado(string valorDeEncrypt)
    {
        // Las cuatro grafías las acepta SqlClient. Sin un caso por valor, borrar «strict» de la lista
        // rechazaría en el arranque un despliegue legítimo con Encrypt=Strict —el modo más estricto
        // que existe— y ningún test avisaría.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var cadena =
            "Server=host-de-prueba,1433;Initial Catalog=GestionVacacionesV2;Integrated Security=True;" +
            $"Encrypt={valorDeEncrypt};TrustServerCertificate=False";

        var resuelta = CadenaDeConexion.Resolver(ConstruirConfiguracionCon(cadena), esDesarrollo: false);

        Assert.Equal(cadena, resuelta);
    }

    [Fact]
    public void Fuera_de_desarrollo_trust_server_certificate_true_sin_espacios_se_rechaza()
    {
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => CadenaDeConexion.Resolver(ConstruirConfiguracionCon(CadenaDeContenedorDeTest), esDesarrollo: false));

        Assert.Contains("TrustServerCertificate", excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fuera_de_desarrollo_trust_server_certificate_true_con_espacios_se_rechaza()
    {
        // «Trust Server Certificate» es la grafía canónica de SqlClient, no una rareza: el validador
        // busca las dos y este es el caso que sostiene la segunda.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => CadenaDeConexion.Resolver(
                ConstruirConfiguracionCon(CadenaConCertificadoDeConfianzaConEspacios), esDesarrollo: false));

        Assert.Contains("TrustServerCertificate", excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fuera_de_desarrollo_trust_server_certificate_en_yes_se_rechaza()
    {
        // SqlClient lee «yes» como true: si el validador solo mirara «true», la cadena pasaría y el
        // canal quedaría cifrado contra un certificado que nadie verifica.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => CadenaDeConexion.Resolver(
                ConstruirConfiguracionCon(CadenaConCertificadoDeConfianzaEnYes), esDesarrollo: false));

        Assert.Contains("TrustServerCertificate", excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Database_vale_como_catalogo_igual_que_initial_catalog()
    {
        // Alias habitual en cadenas escritas a mano. Sin este caso, quitar la rama que lo reconoce
        // dejaría la suite verde y rompería el arranque de quien despliegue con Database=…
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var resuelta = CadenaDeConexion.Resolver(
            ConstruirConfiguracionCon(CadenaConDatabaseComoCatalogo), esDesarrollo: false);

        Assert.Equal(CadenaConDatabaseComoCatalogo, resuelta);
    }

    [Fact]
    public void Un_catalogo_declarado_pero_en_blanco_se_rechaza()
    {
        // Declarar la clave con espacios no es declarar un catálogo: sin este caso, comprobar solo la
        // presencia de la clave bastaría para pasar.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => CadenaDeConexion.Resolver(ConstruirConfiguracionCon(CadenaConCatalogoEnBlanco), esDesarrollo: false));

        Assert.Contains("Initial Catalog", excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void En_desarrollo_se_acepta_la_cadena_de_los_contenedores_de_test()
    {
        // La exigencia de cifrado está acotada al entorno a propósito: los contenedores de SQL Server
        // del Block 2 usan certificado autofirmado y no podrían arrancar con TrustServerCertificate=False.
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var resuelta = CadenaDeConexion.Resolver(ConstruirConfiguracionCon(CadenaDeContenedorDeTest), esDesarrollo: true);

        Assert.Equal(CadenaDeContenedorDeTest, resuelta);
    }

    [Fact]
    public void El_rechazo_por_falta_de_cifrado_no_revela_el_valor_de_la_cadena()
    {
        using var variableEquivalente = new VariableDeEntornoTemporal(VariableDeConfiguracionEquivalente, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, null);

        var excepcion = Assert.Throws<InvalidOperationException>(
            () => CadenaDeConexion.Resolver(ConstruirConfiguracionCon(CadenaQueOmiteEncrypt), esDesarrollo: false));

        Assert.DoesNotContain(CadenaQueOmiteEncrypt, excepcion.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ValorFicticio, excepcion.Message, StringComparison.OrdinalIgnoreCase);
        // Tampoco encadenada en una excepción interna: ToString() acabaría en un log.
        Assert.DoesNotContain(ValorFicticio, excepcion.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void La_cadena_resuelta_no_revela_su_valor_al_convertirse_en_texto()
    {
        // El ToString() que el compilador genera para un record imprime todas sus propiedades: un
        // logger.LogInformation("{Config}", cadena) volcaría la cadena entera al log (R-02, F-SAST-10).
        var resuelta = new CadenaDeConexionResuelta(CadenaQueOmiteEncrypt);

        Assert.DoesNotContain(CadenaQueOmiteEncrypt, resuelta.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(ValorFicticio, resuelta.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ValorFicticio, $"{resuelta}", StringComparison.OrdinalIgnoreCase);

        // El valor sigue disponible para quien lo necesita de verdad: lo que se oculta es el volcado
        // accidental, no el acceso explícito.
        Assert.Equal(CadenaQueOmiteEncrypt, resuelta.Valor);
    }

    [Fact]
    public void La_cadena_resuelta_no_revela_su_valor_al_serializarse()
    {
        // El ToString() cerró los caminos de log, no los de serialización: siendo Valor una propiedad
        // pública, cualquier volcado a JSON —un endpoint de diagnóstico, una caché, un mensaje— emite
        // «{"Valor":"…con la contraseña…"}» (R-02, F-SAST-10).
        var resuelta = new CadenaDeConexionResuelta(CadenaQueOmiteEncrypt);

        var json = JsonSerializer.Serialize(resuelta);

        Assert.DoesNotContain(CadenaQueOmiteEncrypt, json, StringComparison.Ordinal);
        Assert.DoesNotContain(ValorFicticio, json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Valor", json, StringComparison.Ordinal);

        // Igual que con el ToString(): se cierra el volcado accidental, no el acceso explícito.
        Assert.Equal(CadenaQueOmiteEncrypt, resuelta.Valor);
    }

    private static IConfiguration ConstruirConfiguracionCon(string cadena) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CadenaDeConexion.ClaveDeConfiguracion] = cadena,
            })
            .AddEnvironmentVariables()
            .Build();
}
