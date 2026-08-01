using System.Data.Common;
using System.Text.Json.Serialization;

namespace GestionVacaciones.Web.Configuracion;

/// <summary>
/// Cadena de conexión ya resuelta y validada. Se registra en el contenedor para que el resto de la
/// aplicación la consuma sin volver a leer configuración ni variables de entorno.
/// </summary>
/// <param name="Valor">La cadena en sí. Nunca se registra en logs: puede contener credenciales.</param>
public sealed record CadenaDeConexionResuelta([property: JsonIgnore] string Valor)
{
    private const string TextoOculto = "CadenaDeConexionResuelta { Valor = [oculto] }";

    /// <summary>
    /// Oculta el valor. El <c>ToString()</c> que genera el compilador para un record imprime todas
    /// sus propiedades, así que un <c>logger.LogInformation("{Config}", cadena)</c> volcaría la
    /// cadena entera —contraseña incluida— al log (R-02, F-SAST-10). Que el comentario diga «nunca se
    /// registra» no lo impide; esto sí. Quien necesite el valor lo pide por <see cref="Valor"/>, que
    /// es explícito y auditable.
    /// </summary>
    /// <remarks>
    /// El <c>[JsonIgnore]</c> sobre <see cref="Valor"/> cierra la otra salida por la que el valor se
    /// escapa sin que nadie lo pida: siendo una propiedad pública, cualquier
    /// <c>JsonSerializer.Serialize(cadena)</c> —un endpoint de diagnóstico, una caché, un mensaje—
    /// emitiría <c>{"Valor":"…con la contraseña…"}</c>. Ocultarlo en el <c>ToString()</c> y dejarlo en
    /// la serialización tapa el camino visible y deja abierto el que nadie mira.
    /// </remarks>
    public override string ToString() => TextoOculto;
}

/// <summary>
/// Resuelve la cadena de conexión a SQL Server con una precedencia explícita:
/// <list type="number">
///   <item><description>la variable de entorno <c>VACACIONES_CONNECTION</c>, que gana siempre;</description></item>
///   <item><description>la clave de configuración <c>ConnectionStrings:Vacaciones</c> (user-secrets en desarrollo).</description></item>
/// </list>
/// Si ninguna resuelve, lanza. No existe valor por defecto: el repositorio es público y una cadena
/// escrita en <c>appsettings.json</c> sería una credencial publicada de forma permanente (R-02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Cifrado en tránsito (mitigación 7 del modelo de amenazas, R-02 / F-TM-07).</b> Fuera de
/// <c>Development</c> la cadena <b>debe</b> declarar <c>Encrypt=True</c> y <b>no</b> puede declarar
/// <c>TrustServerCertificate=True</c>: sin eso la PII —nombres, correos y períodos de ausencia—
/// viaja en claro hasta SQL Server, o cifrada contra un certificado que nadie verifica. Si no se
/// cumple, la aplicación no arranca. La exigencia está acotada al entorno, como la doble condición
/// de R-01: en <c>Development</c> no se aplica, porque los contenedores de SQL Server de los tests
/// usan certificado autofirmado y solo se alcanzan con <c>TrustServerCertificate=True</c>.
/// </para>
/// <para>
/// La cadena debe además apuntar al catálogo <c>GestionVacacionesV2</c>. Eso sigue siendo requisito
/// de despliegue documentado, no validación de arranque: acá solo se comprueba que indique
/// <i>algún</i> catálogo.
/// </para>
/// <para>
/// <b>Deuda conocida del validador.</b> Se usa <see cref="DbConnectionStringBuilder"/> de la BCL, y
/// no <c>SqlConnectionStringBuilder</c>, para no arrastrar <c>Microsoft.Data.SqlClient</c> al
/// proyecto Web —el acceso a datos vive en el proyecto Data—. El precio es una pérdida de
/// estrictez: <see cref="DbConnectionStringBuilder"/> <b>acepta palabras clave desconocidas</b>, así
/// que una errata como <c>Servr=localhost;Initial Catalog=X</c> pasa esta validación y revienta en
/// la primera consulta, que es justo el modo de fallo que el diseño quiere evitar. Tampoco reconoce
/// sinónimos de SqlClient, por eso <c>TrustServerCertificate</c> se busca en sus dos grafías
/// habituales. Cuando el Block 2 traiga el proveedor de SQL Server al proyecto Data, conviene
/// revisar si la validación debe mudarse allí y usar el constructor específico.
/// </para>
/// <para>
/// <b>A revisar en el Bloque 2.</b> El motivo de la deuda de arriba desaparece en cuanto
/// <c>Microsoft.Data.SqlClient</c> llegue a Web por vía transitiva (Web → Data → EFCore.SqlServer):
/// ya no habría que arrastrar nada que no esté puesto. Entonces:
/// <list type="number">
///   <item><description>
///     <b>Migrar la validación a <c>SqlConnectionStringBuilder</c></b>, que <b>rechaza palabras clave
///     desconocidas</b>. Hoy <c>Servr=localhost</c> (errata) y <c>TrustServerCertificate=1</c> pasan
///     esta validación y explotan en la primera consulta: exactamente la promesa que el diseño hace
///     —«falla al arrancar, no en la primera consulta»— incumplida en los dos casos más probables.
///   </description></item>
///   <item><description>
///     <b>Reemplazar el parámetro <c>bool esDesarrollo</c></b> por un enum propio o por
///     <c>IHostEnvironment</c>. Es un booleano posicional que decide si se exige cifrado en tránsito,
///     y la dirección insegura —<c>true</c>, «no lo exijas»— es la que compila sin ruido: un
///     <c>Resolver(configuracion, true)</c> escrito por error en el arranque de producción apaga la
///     mitigación 7 sin que nada chille.
///   </description></item>
///   <item><description>
///     <b>Testear <c>ConfigurarCanalizacion</c></b> cuando el Bloque 2 traiga hosting de integración.
///     Hoy la canalización —manejador de excepciones sin traza, HSTS, antiforgery (F-SAST-09)— no
///     tiene ninguna afirmación encima porque no hay forma de ejercitarla sin levantar el servidor.
///   </description></item>
/// </list>
/// </para>
/// </remarks>
public static class CadenaDeConexion
{
    /// <summary>Variable de entorno con precedencia sobre cualquier otra fuente.</summary>
    public const string VariableDeEntorno = "VACACIONES_CONNECTION";

    /// <summary>Clave de configuración usada en desarrollo, a través de user-secrets.</summary>
    public const string ClaveDeConfiguracion = "ConnectionStrings:Vacaciones";

    private const string ClaveDeCatalogo = "Initial Catalog";
    private const string ClaveDeCatalogoAlternativa = "Database";

    private const string ClaveDeCifrado = "Encrypt";
    private const string ClaveDeCertificadoDeConfianza = "TrustServerCertificate";
    private const string ClaveDeCertificadoDeConfianzaConEspacios = "Trust Server Certificate";

    /// <summary>Valores de <c>Encrypt</c> que SqlClient interpreta como «cifrar».</summary>
    private static readonly string[] _valoresQueActivanElCifrado = ["true", "yes", "mandatory", "strict"];

    /// <summary>Valores que SqlClient interpreta como «no verificar el certificado del servidor».</summary>
    private static readonly string[] _valoresAfirmativos = ["true", "yes"];

    /// <summary>Devuelve la cadena de conexión resuelta y validada.</summary>
    /// <param name="configuracion">Configuración del host, de donde salen las dos fuentes posibles.</param>
    /// <param name="esDesarrollo">
    /// <c>true</c> solo si el entorno del host es <c>Development</c>. Fuera de ahí se exige cifrado en
    /// tránsito hacia SQL Server (R-02, F-TM-07).
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Si ninguna fuente la aporta, si no es interpretable, si no indica catálogo o si —fuera de
    /// <c>Development</c>— no exige cifrado verificado.
    /// </exception>
    public static string Resolver(IConfiguration configuracion, bool esDesarrollo)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        var (cadena, fuente) = Localizar(configuracion);
        Validar(cadena, fuente, esDesarrollo);
        return cadena;
    }

    private static (string Cadena, string Fuente) Localizar(IConfiguration configuracion)
    {
        var deLaVariableDeEntorno = configuracion[VariableDeEntorno];
        if (!string.IsNullOrWhiteSpace(deLaVariableDeEntorno))
        {
            return (deLaVariableDeEntorno, $"la variable de entorno {VariableDeEntorno}");
        }

        var deLaConfiguracion = configuracion[ClaveDeConfiguracion];
        if (!string.IsNullOrWhiteSpace(deLaConfiguracion))
        {
            return (deLaConfiguracion, $"la clave de configuración {ClaveDeConfiguracion}");
        }

        throw new InvalidOperationException(
            "No hay cadena de conexión configurada y no existe valor por defecto. " +
            $"Definí la variable de entorno {VariableDeEntorno}, " +
            $"o la clave de configuración {ClaveDeConfiguracion} con user-secrets en desarrollo.");
    }

    private static void Validar(string cadena, string fuente, bool esDesarrollo)
    {
        DbConnectionStringBuilder interpretada;
        try
        {
            interpretada = new DbConnectionStringBuilder { ConnectionString = cadena };
        }
        catch (ArgumentException)
        {
            // Ni el valor ni la excepción original se propagan: la cadena puede contener credenciales
            // y este mensaje termina en un log (R-02, F-SAST-10).
            throw new InvalidOperationException(
                $"La cadena de conexión aportada por {fuente} no se puede interpretar. " +
                "Revisá su formato; el valor no se incluye acá porque puede contener credenciales.");
        }

        if (!IndicaCatalogo(interpretada))
        {
            throw new InvalidOperationException(
                $"La cadena de conexión aportada por {fuente} no indica «{ClaveDeCatalogo}» " +
                $"(ni su equivalente «{ClaveDeCatalogoAlternativa}»).");
        }

        if (!esDesarrollo)
        {
            ValidarCifradoEnTransito(interpretada, fuente);
        }
    }

    /// <summary>
    /// Exige cifrado verificado hacia SQL Server fuera de <c>Development</c> (mitigación 7, R-02 /
    /// F-TM-07). Ningún mensaje incluye el valor de la cadena: puede llevar credenciales y termina en
    /// un log.
    /// </summary>
    private static void ValidarCifradoEnTransito(DbConnectionStringBuilder interpretada, string fuente)
    {
        if (!Declara(interpretada, ClaveDeCifrado, _valoresQueActivanElCifrado))
        {
            throw new InvalidOperationException(
                $"La cadena de conexión aportada por {fuente} no exige cifrado en tránsito: agregá " +
                $"«{ClaveDeCifrado}=True». Fuera del entorno Development es obligatorio, porque sin él " +
                "los datos personales viajan en claro hasta SQL Server. El valor no se incluye acá " +
                "porque puede contener credenciales.");
        }

        if (Declara(interpretada, ClaveDeCertificadoDeConfianza, _valoresAfirmativos) ||
            Declara(interpretada, ClaveDeCertificadoDeConfianzaConEspacios, _valoresAfirmativos))
        {
            throw new InvalidOperationException(
                $"La cadena de conexión aportada por {fuente} declara " +
                $"«{ClaveDeCertificadoDeConfianza}=True»: quitalo o ponelo en False. Fuera del entorno " +
                "Development aceptar cualquier certificado deja el cifrado sin verificar y el canal " +
                "queda expuesto a un intermediario. El valor no se incluye acá porque puede contener " +
                "credenciales.");
        }
    }

    private static bool IndicaCatalogo(DbConnectionStringBuilder interpretada) =>
        TieneValor(interpretada, ClaveDeCatalogo) || TieneValor(interpretada, ClaveDeCatalogoAlternativa);

    private static bool TieneValor(DbConnectionStringBuilder interpretada, string clave) =>
        interpretada.TryGetValue(clave, out var valor) && !string.IsNullOrWhiteSpace(valor as string);

    private static bool Declara(DbConnectionStringBuilder interpretada, string clave, string[] valoresAceptados) =>
        interpretada.TryGetValue(clave, out var valor) &&
        valor is string texto &&
        valoresAceptados.Contains(texto.Trim(), StringComparer.OrdinalIgnoreCase);
}
