using GestionVacaciones.Data.Services;

namespace GestionVacaciones.Web.Identidad;

/// <summary>
/// Las dos mitades de la mitigación de <b>R-01 (CRITICAL)</b>: la <b>triple</b> condición que habilita
/// el sustituto de identidad de desarrollo, y la comprobación de arranque que hace que la aplicación
/// <b>no levante</b> si ese sustituto quedara activo donde no corresponde.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué las dos viven acá y no sueltas en la composición.</b> R-01 es que una sola variable de
/// entorno mal puesta convierta el guardarraíl en nada. Con la decisión escrita en un único lugar
/// —igual que <c>CadenaDeConexion</c> concentra la precedencia de la cadena— hay un solo punto que leer
/// para saber qué habilita la suplantación, y un solo punto que testear.
/// </para>
/// </remarks>
public static class VerificacionDeIdentidad
{
    /// <summary>
    /// Segunda condición de R-01, independiente del entorno.
    /// </summary>
    /// <remarks>
    /// <b>Dónde se declara importa tanto como su valor.</b> Vive en
    /// <c>Properties/launchSettings.json</c>, que está versionado pero <b>no se publica</b>. Declararla
    /// en <c>appsettings.Development.json</c> —que sí viaja en el artefacto— la volvía una consecuencia
    /// del entorno en vez de una condición aparte, y con eso la mitigación se quedaba en una sola.
    /// </remarks>
    public const string ClaveDeConfiguracion = "Vacaciones:PermitirIdentidadDeDesarrollo";

    /// <summary>El único valor que habilita. Todo lo demás cae del lado seguro.</summary>
    private const string ValorQueHabilita = "true";

    /// <summary>
    /// ¿Corresponde registrar el sustituto de identidad de desarrollo? Solo si el artefacto se compiló
    /// para depuración, <b>y</b> el entorno es <c>Development</c>, <b>y además</b>
    /// <see cref="ClaveDeConfiguracion"/> vale <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Tres condiciones, y ahora sí independientes.</b> Este comentario decía antes que un despliegue
    /// con <c>ASPNETCORE_ENVIRONMENT=Development</c> mal puesto «no alcanza, porque además haría falta la
    /// clave», y <b>era falso</b>: la clave vivía en <c>appsettings.Development.json</c>, un archivo que
    /// solo se carga cuando ya se cumple la condición del entorno y que además se copiaba al artefacto
    /// publicado. La segunda condición llegaba sola detrás de la primera, así que las dos eran una.
    /// Hoy la clave vive en <c>launchSettings.json</c>, que no se publica, y a las dos se les suma
    /// <see cref="CompilacionDelArtefacto.EsDeDepuracion"/>, que no sale de la configuración y por lo
    /// tanto no la puede poner quien controla el entorno de ejecución.
    /// </para>
    /// <para>
    /// La clave se compara por igualdad con «true», sin distinguir mayúsculas, y <b>no</b> se interpreta
    /// con un conversor de booleanos. Dos motivos: un valor que no se puede interpretar —«sí», «on»—
    /// haría lanzar al conversor y el arranque fallaría por un motivo que no es el suyo; y las
    /// abreviaturas que uno escribe por costumbre —«1», «yes»— ampliarían en silencio la superficie de
    /// R-01. La dirección en la que conviene equivocarse es la de no habilitar.
    /// </para>
    /// </remarks>
    public static bool PermiteIdentidadDeDesarrollo(IConfiguration configuracion, bool esDesarrollo) =>
        PermiteIdentidadDeDesarrollo(configuracion, esDesarrollo, CompilacionDelArtefacto.EsDeDepuracion);

    /// <summary>
    /// Igual que <see cref="PermiteIdentidadDeDesarrollo(IConfiguration, bool)"/>, con la condición de
    /// compilación recibida en vez de leída del ensamblado.
    /// </summary>
    /// <param name="compiladoParaDepuracion">
    /// Si el artefacto se compiló para depuración. La sobrecarga existe para que un test pueda
    /// ejercitar el comportamiento del artefacto de <c>Release</c> sin compilar en Release —la suite
    /// corre en <c>Debug</c>, así que un <c>#if</c> crudo sería inerte y la condición quedaría sin
    /// verificar—. <b>La composición del host nunca la llama:</b> usa la sobrecarga de dos parámetros,
    /// que es la que lee el valor real.
    /// </param>
    public static bool PermiteIdentidadDeDesarrollo(
        IConfiguration configuracion,
        bool esDesarrollo,
        bool compiladoParaDepuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        return compiladoParaDepuracion &&
               esDesarrollo &&
               string.Equals(configuracion[ClaveDeConfiguracion], ValorQueHabilita, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Comprueba, sobre el contenedor <b>ya construido</b>, qué implementación de
    /// <see cref="IEmpleadoActualProvider"/> quedó resuelta, y lanza si fuera de <c>Development</c> no es
    /// <see cref="EmpleadoActualNoConfigurado"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Fallo al arrancar, no al primer uso</b> (mitigación 2 de R-01). Una aplicación que no levanta
    /// es un incidente visible; una que levanta y suplanta identidades, no: nadie la reporta porque
    /// funciona.
    /// </para>
    /// <para>
    /// Se resuelve del contenedor en vez de mirar los descriptores del registro, y esa diferencia es lo
    /// que la vuelve un guardarraíl: lo que se comprueba es lo que la aplicación va a recibir de verdad,
    /// no lo que la composición pretendía registrar. Cualquier registro posterior —un punto de extensión,
    /// un host futuro que copie el registro sin copiar la condición— queda cubierto igual.
    /// </para>
    /// <para>
    /// Se crea un ámbito porque el proveedor es <c>scoped</c>: pedirlo a la raíz fallaría por la
    /// validación de ámbitos, que en <c>Development</c> está activa.
    /// </para>
    /// <para>
    /// <b>No devuelve el tipo resuelto</b>, aunque lo tenga en la mano: no lo consumía nadie. El único
    /// llamador —la composición del host— lo descartaba, y los tests leen del contenedor la
    /// implementación que quedó, que es la forma correcta de afirmarlo porque no depende de que esta
    /// comprobación se haya ejecutado. Un valor de retorno sin llamador es superficie pública que hay que
    /// mantener; lo que este método hace es <i>lanzar o no lanzar</i>.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Si no hay ninguna implementación registrada, o si la resuelta no es
    /// <see cref="EmpleadoActualNoConfigurado"/> y o bien el entorno no es <c>Development</c>, o bien el
    /// artefacto se compiló en <c>Release</c>.
    /// </exception>
    public static void Verificar(IServiceProvider servicios, IHostEnvironment entorno) =>
        Verificar(servicios, entorno, CompilacionDelArtefacto.EsDeDepuracion);

    /// <summary>
    /// Igual que <see cref="Verificar(IServiceProvider, IHostEnvironment)"/>, con la condición de
    /// compilación recibida en vez de leída del ensamblado.
    /// </summary>
    /// <param name="compiladoParaDepuracion">
    /// Si el artefacto se compiló para depuración. Mismo motivo que en la sobrecarga equivalente de
    /// <see cref="PermiteIdentidadDeDesarrollo(IConfiguration, bool, bool)"/>: sin ella, la condición de
    /// compilación no sería observable desde una suite que corre en <c>Debug</c>.
    /// </param>
    public static void Verificar(IServiceProvider servicios, IHostEnvironment entorno, bool compiladoParaDepuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(entorno);

        using var ambito = servicios.CreateScope();

        var proveedor = ambito.ServiceProvider.GetService<IEmpleadoActualProvider>()
            ?? throw new InvalidOperationException(
                $"No hay ninguna implementación de {nameof(IEmpleadoActualProvider)} registrada, así que " +
                "la aplicación no puede resolver de quién es ninguna solicitud. La composición del host " +
                "tiene que registrar exactamente una.");

        var tipoResuelto = proveedor.GetType();

        if (tipoResuelto == typeof(EmpleadoActualNoConfigurado))
        {
            return;
        }

        // La condición de compilación se comprueba PRIMERO y por separado: es la que no depende de
        // ninguna variable de entorno, así que es la que sigue valiendo cuando el entorno miente. Su
        // mensaje es propio a propósito —el de abajo manda a revisar ASPNETCORE_ENVIRONMENT, y acá el
        // entorno puede estar perfectamente bien—.
        if (!compiladoParaDepuracion)
        {
            throw new InvalidOperationException(
                $"Este artefacto se compiló en Release y la identidad del empleado actual la resuelve " +
                $"«{tipoResuelto.Name}», que no es «{nameof(EmpleadoActualNoConfigurado)}». El sustituto " +
                "de identidad de desarrollo permite elegir cualquier empleado sin credencial y no puede " +
                "quedar activo en un artefacto desplegable, con ninguna variable de entorno. Si estabas " +
                "desarrollando, compilá en Debug.");
        }

        if (!entorno.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"El entorno es «{entorno.EnvironmentName}» y la identidad del empleado actual la " +
                $"resuelve «{tipoResuelto.Name}», que no es «{nameof(EmpleadoActualNoConfigurado)}». " +
                "El sustituto de identidad de desarrollo permite elegir cualquier empleado sin " +
                "credencial, así que la aplicación no arranca: revisá ASPNETCORE_ENVIRONMENT y el " +
                $"registro de {nameof(IEmpleadoActualProvider)}.");
        }
    }
}
