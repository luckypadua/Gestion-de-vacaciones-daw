using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// Fixture de los tests de integración: resuelve la cadena de test, <b>impide</b> que apunte a una
/// base que no sea descartable, y deja el esquema recién aplicado desde las migraciones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué existe el guardarraíl (mitigación R-11).</b> Con Testcontainers el aislamiento era una
/// propiedad de la infraestructura: el contenedor nacía vacío, moría al terminar y no compartía nada
/// con ninguna base real. Al pasar a la instancia SQL Server 2022 del entorno —que aloja también
/// <c>GestionVacacionesV2</c> y la v1 <c>GestionVacaciones</c>— ese aislamiento pasa a depender del
/// código. Este fixture <b>recrea la base en cada corrida</b>, que es una operación destructiva: sin
/// el guardarraíl, una variable de entorno mal puesta borra datos reales.
/// </para>
/// <para>
/// El guardarraíl corre <b>antes de abrir la conexión</b>, no después de fallar: para cuando el
/// motor contesta, el daño ya podría estar hecho.
/// </para>
/// <para>
/// <b>Ningún mensaje de error incluye la cadena.</b> Lleva credenciales de una instancia con datos
/// reales, y los mensajes terminan en la salida del runner y en los logs (R-02, R-11, F-SAST-10). Lo
/// que sí nombran es el catálogo destino, que es lo único accionable para quien diagnostica.
/// </para>
/// </remarks>
public sealed class BaseDeDatosDeTest : IAsyncLifetime
{
    /// <summary>
    /// Variable de entorno con precedencia sobre los user-secrets. Es la que permite corregir la IP
    /// del host de Windows sin reescribir los secretos: desde WSL esa IP cambia entre reinicios, y
    /// <c>AGENTS.md</c> lo documenta como cicatriz.
    /// </summary>
    public const string VariableDeEntorno = "VACACIONES_CONNECTION_TEST";

    /// <summary>Clave en los user-secrets del proyecto de tests. Nunca versionada: el repo es público.</summary>
    public const string ClaveDeConfiguracion = "ConnectionStrings:VacacionesTest";

    /// <summary>Sufijo que declara una base como descartable. Sin él, el fixture no la toca.</summary>
    public const string SufijoExigido = "_Test";

    /// <summary>
    /// Los dos catálogos que <c>AGENTS.md</c> marca como intocables. La regla del sufijo ya los
    /// rechazaría; se los nombra igual para que la protección sobreviva a que alguien afloje aquella.
    /// </summary>
    public static readonly string[] CatalogosIntocables = ["GestionVacacionesV2", "GestionVacaciones"];

    private string? _cadena;

    /// <summary>Hay instancia y esquema listos: los tests de integración pueden correr.</summary>
    public bool Disponible { get; private set; }

    /// <summary>Por qué no hay base, cuando no la hay. Es lo que se le informa al runner al saltear.</summary>
    public string MotivoDeIndisponibilidad { get; private set; } = string.Empty;

    /// <summary>Catálogo descartable sobre el que se está trabajando.</summary>
    public string Catalogo { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var resolucion = Resolver(ConstruirConfiguracion());
        if (!resolucion.Disponible)
        {
            MotivoDeIndisponibilidad = resolucion.Motivo;
            return;
        }

        // Si hay cadena configurada pero la instancia no responde, la excepción SE PROPAGA y tumba la
        // colección. No se saltea: «configurada pero rota» es un fallo real, y saltearlo escondería
        // que NFR-04 dejó de verificarse en una máquina donde sí se puede verificar.
        Catalogo = await PrepararAsync(resolucion.Valor!).ConfigureAwait(false);
        _cadena = resolucion.Valor;
        Disponible = true;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Saltea el test en curso si no hay instancia. Es un único punto para que ningún test invente su
    /// propio criterio ni se olvide del motivo.
    /// </summary>
    public void SaltearSiNoEstaDisponible() => Assert.SkipUnless(Disponible, MotivoDeIndisponibilidad);

    /// <summary>
    /// Señal de salteo con motivo. Una suite roja por falta de entorno enseña a ignorar el rojo, que
    /// es peor que no tener el test.
    /// </summary>
    [DoesNotReturn]
    public static void Saltear(string motivo) => Assert.Skip(motivo);

    /// <summary>
    /// Localiza la cadena de test: primero la variable de entorno, después los user-secrets. Si no
    /// hay ninguna, <b>no lanza</b>: devuelve el motivo para que los tests se salteen.
    /// </summary>
    public static CadenaDeTest Resolver(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        var deLaVariable = configuracion[VariableDeEntorno];
        if (!string.IsNullOrWhiteSpace(deLaVariable))
        {
            return CadenaDeTest.Encontrada(deLaVariable);
        }

        var deLosSecretos = configuracion[ClaveDeConfiguracion];
        if (!string.IsNullOrWhiteSpace(deLosSecretos))
        {
            return CadenaDeTest.Encontrada(deLosSecretos);
        }

        return CadenaDeTest.NoConfigurada(
            "No hay cadena de conexión de test configurada, así que los tests de integración de " +
            $"NFR-04 se saltean. Definí la variable de entorno {VariableDeEntorno}, o la clave " +
            $"{ClaveDeConfiguracion} en los user-secrets del proyecto de tests, apuntando a una base " +
            $"cuyo catálogo termine en «{SufijoExigido}».");
    }

    /// <summary>
    /// <b>El guardarraíl.</b> Devuelve el catálogo destino, o lanza si no es descartable. Nunca abre
    /// una conexión y nunca repite la cadena en el mensaje.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si la cadena no es interpretable, declara <c>AttachDbFilename</c> o <c>User Instance</c> —que
    /// vuelven mentiroso al catálogo—, no indica catálogo, apunta a uno de los catálogos intocables, o
    /// a uno que no termina en <see cref="SufijoExigido"/>.
    /// </exception>
    public static string ExigirCatalogoDescartable(string cadena)
    {
        var catalogo = LeerCatalogo(cadena);

        if (CatalogosIntocables.Contains(catalogo, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"La cadena de test apunta al catálogo «{catalogo}», que es una base real del " +
                "proyecto y los tests la recrearían desde cero. AGENTS.md la marca como intocable. " +
                $"Apuntá a una base descartable cuyo catálogo termine en «{SufijoExigido}».");
        }

        if (!catalogo.EndsWith(SufijoExigido, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"El catálogo «{catalogo}» no termina en «{SufijoExigido}», así que no está declarado " +
                "como descartable y este fixture no lo toca: la preparación borra y recrea la base " +
                "entera. Apuntá la cadena de test a una base cuyo nombre lo declare.");
        }

        return catalogo;
    }

    /// <summary>
    /// Aplica el guardarraíl y solo entonces ejecuta <paramref name="crearElEsquema"/>. El delegado
    /// es la costura que permite afirmar que el aborto ocurre <b>antes</b> de abrir la conexión, y no
    /// solo que ocurre.
    /// </summary>
    public static async Task<string> PrepararAsync(string cadena, Func<string, Task> crearElEsquema)
    {
        ArgumentNullException.ThrowIfNull(crearElEsquema);

        var catalogo = ExigirCatalogoDescartable(cadena);

        try
        {
            await crearElEsquema(cadena).ConfigureAwait(false);
        }
        catch (DbException excepcion)
        {
            // El diagnóstico tiene que ser accionable sin publicar credenciales: se nombra el
            // catálogo destino, nunca la cadena (R-11, mitigación 4). La excepción original se
            // encadena porque su mensaje describe el fallo concreto y no contiene la contraseña.
            //
            // El mensaje NO afirma una causa: este catch no la averiguó. Afirmar «la instancia no
            // respondió» manda a revisar la red cuando el motor puede haber contestado
            // perfectamente —por ejemplo matando la sesión que dejó abierta la corrida anterior—, y
            // un error que apunta al lugar equivocado cuesta más que uno que no apunta a ninguno.
            throw new InvalidOperationException(
                $"No se pudo preparar la base de datos de test «{catalogo}». El motivo exacto está en " +
                "la excepción interna; las causas habituales son que la instancia no sea alcanzable " +
                "—desde WSL, la IP del host de Windows cambia entre reinicios—, que las credenciales " +
                "o los permisos no alcancen, o que queden sesiones abiertas de una corrida anterior " +
                "bloqueando el borrado. La cadena no se incluye acá porque puede llevar credenciales.",
                excepcion);
        }

        return catalogo;
    }

    /// <summary>Guardarraíl + recreación del esquema desde las migraciones.</summary>
    public static Task<string> PrepararAsync(string cadena) => PrepararAsync(cadena, RecrearEsquemaAsync);

    /// <summary>
    /// Contexto suelto contra una cadena arbitraria, con el guardarraíl aplicado. No abre conexión al
    /// construirse.
    /// </summary>
    /// <remarks>
    /// <b>Por qué el guardarraíl también acá, y no solo en <see cref="PrepararAsync(string)"/>.</b>
    /// Esta es la primitiva: de un contexto ya construido, <c>Database.EnsureDeletedAsync()</c> queda
    /// a una sola llamada de distancia, y un test futuro que se arme el suyo no pasaría por
    /// <see cref="PrepararAsync(string)"/> en ningún momento. Con la comprobación acá, ningún contexto
    /// de esta suite puede apuntar a una base que no se declaró descartable. Es <c>internal</c> por lo
    /// mismo: no es una utilidad de uso general.
    /// </remarks>
    internal static VacacionesDbContext CrearContexto(string cadena)
    {
        ExigirCatalogoDescartable(cadena);

        return new VacacionesDbContext(
            new DbContextOptionsBuilder<VacacionesDbContext>().UseSqlServer(cadena).Options);
    }

    /// <summary>Contexto contra la base descartable de esta corrida.</summary>
    public VacacionesDbContext CrearContexto() =>
        CrearContexto(_cadena ?? throw new InvalidOperationException(
            "No hay base de datos de test disponible. Los tests que la usan tienen que llamar antes a " +
            $"{nameof(SaltearSiNoEstaDisponible)}()."));

    /// <summary>
    /// Crea un empleado propio del test y devuelve el asa que lo borra —con sus solicitudes— al
    /// terminar. Es la regla #0 de testing hecha código: cada test crea sus propios datos y los
    /// limpia, en vez de confiar en que la base esté como el anterior la dejó.
    /// </summary>
    public async Task<EmpleadoDescartable> CrearEmpleadoDescartableAsync(string? correo = null)
    {
        await using var contexto = CrearContexto();
        var empleado = new Empleado
        {
            // Nombre genérico y correo aleatorio: son PII en el modelo (F-TM-05) y no hace falta
            // inventar personas para probar una constraint.
            Nombre = "Empleado de prueba",
            Correo = correo ?? $"{Guid.NewGuid():N}@prueba.local",
        };

        contexto.Empleados.Add(empleado);
        await contexto.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        return new EmpleadoDescartable(this, empleado.Id, empleado.Correo);
    }

    /// <summary>
    /// Borra y vuelve a crear la base aplicando las migraciones. Recrear en cada corrida es lo que
    /// garantiza que B2-T5 mire el índice que produce la migración y no uno que quedó de antes.
    /// </summary>
    private static async Task RecrearEsquemaAsync(string cadena)
    {
        await using var contexto = CrearContexto(cadena);
        await contexto.Database.EnsureDeletedAsync().ConfigureAwait(false);
        await contexto.Database.MigrateAsync().ConfigureAwait(false);
    }

    private static string LeerCatalogo(string cadena)
    {
        if (string.IsNullOrWhiteSpace(cadena))
        {
            throw new InvalidOperationException("La cadena de conexión de test está vacía.");
        }

        SqlConnectionStringBuilder interpretada;
        try
        {
            // SqlConnectionStringBuilder y no DbConnectionStringBuilder: rechaza palabras clave
            // desconocidas, así que una errata como «Servr=…» aborta acá y no a mitad de la corrida.
            interpretada = new SqlConnectionStringBuilder(cadena);
        }
        catch (ArgumentException)
        {
            // Ni el valor ni la excepción original se propagan: la cadena puede llevar credenciales.
            throw new InvalidOperationException(
                "La cadena de conexión de test no se puede interpretar. Revisá su formato; el valor " +
                "no se incluye acá porque puede contener credenciales.");
        }

        // Las dos claves que vuelven mentiroso al «Initial Catalog»: con cualquiera de ellas el
        // nombre que se valida abajo deja de describir lo que se borraría. Se rechazan antes de
        // mirarlo, porque una cadena así pasa la regla del sufijo sin problema.
        if (!string.IsNullOrWhiteSpace(interpretada.AttachDBFilename))
        {
            // El catálogo pasa a nombrar una base adjuntada desde un archivo del disco, y
            // EnsureDeleted la dropea llevándose el .mdf y el .ldf. La ruta no se repite en el
            // mensaje: la cadena entera queda fuera de los mensajes por regla (R-11).
            throw new InvalidOperationException(
                "La cadena de conexión de test declara «AttachDbFilename», que adjunta una base desde " +
                "un archivo del disco: el «Initial Catalog» deja de nombrar una base del servidor y " +
                "la preparación borraría los archivos físicos. Quitá esa clave y apuntá a una base " +
                $"descartable del servidor cuyo catálogo termine en «{SufijoExigido}».");
        }

        if (interpretada.UserInstance)
        {
            // Desvía la conexión a una instancia SQL Express levantada aparte, con sus propios
            // archivos: el catálogo validado acá no describe lo que se destruye allá.
            throw new InvalidOperationException(
                "La cadena de conexión de test declara «User Instance», que desvía la conexión a una " +
                "instancia SQL Express aparte con sus propios archivos: lo que se comprueba acá no es " +
                "lo que se borraría. Quitá esa clave y apuntá a la instancia del entorno.");
        }

        var catalogo = interpretada.InitialCatalog;
        if (string.IsNullOrWhiteSpace(catalogo))
        {
            throw new InvalidOperationException(
                "La cadena de conexión de test no indica «Initial Catalog», así que no hay forma de " +
                "comprobar que apunte a una base descartable.");
        }

        return catalogo;
    }

    private static IConfiguration ConstruirConfiguracion() =>
        new ConfigurationBuilder()
            .AddUserSecrets(typeof(BaseDeDatosDeTest).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
}

/// <summary>
/// Resultado de localizar la cadena de test: o la hay, o hay un motivo para saltear. Nunca las dos
/// cosas, y nunca ninguna.
/// </summary>
public sealed class CadenaDeTest
{
    private const string TextoOculto = "CadenaDeTest { Valor = [oculto] }";

    private CadenaDeTest(string? valor, string motivo)
    {
        Valor = valor;
        Motivo = motivo;
    }

    /// <summary>La cadena, o <c>null</c> si no hay ninguna configurada.</summary>
    [JsonIgnore]
    public string? Valor { get; }

    /// <summary>Motivo del salteo. Vacío cuando sí hay cadena.</summary>
    public string Motivo { get; }

    public bool Disponible => Valor is not null;

    public static CadenaDeTest Encontrada(string valor) => new(valor, string.Empty);

    public static CadenaDeTest NoConfigurada(string motivo) => new(null, motivo);

    /// <summary>
    /// Oculta el valor. Mismo motivo que en <c>CadenaDeConexionResuelta</c> del Bloque 1: un
    /// <c>ToString()</c> que imprima la cadena la vuelca entera —contraseña incluida— en cuanto
    /// alguien la interpole en un mensaje de fallo (R-02, R-11, F-SAST-10).
    /// </summary>
    public override string ToString() => TextoOculto;
}

/// <summary>
/// Empleado creado por un test, que se borra —junto con sus solicitudes— al salir del alcance.
/// El orden importa: la FK de <c>Solicitud</c> es <c>NO ACTION</c>, así que primero las solicitudes.
/// </summary>
public sealed class EmpleadoDescartable : IAsyncDisposable
{
    private readonly BaseDeDatosDeTest _baseDeDatos;

    internal EmpleadoDescartable(BaseDeDatosDeTest baseDeDatos, int id, string correo)
    {
        _baseDeDatos = baseDeDatos;
        Id = id;
        Correo = correo;
    }

    public int Id { get; }

    public string Correo { get; }

    public async ValueTask DisposeAsync()
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        await contexto.Solicitudes
            .Where(solicitud => solicitud.EmpleadoId == Id)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(false);

        await contexto.Empleados
            .Where(empleado => empleado.Id == Id)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(false);
    }
}
