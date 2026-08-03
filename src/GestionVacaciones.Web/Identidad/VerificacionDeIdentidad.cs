using GestionVacaciones.Data.Services;

namespace GestionVacaciones.Web.Identidad;

/// <summary>
/// Las dos mitades de la mitigación de <b>R-01 (CRITICAL)</b>: la doble condición que habilita el
/// sustituto de identidad de desarrollo, y la comprobación de arranque que hace que la aplicación
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
    /// <summary>Segunda condición de R-01, independiente del entorno.</summary>
    public const string ClaveDeConfiguracion = "Vacaciones:PermitirIdentidadDeDesarrollo";

    /// <summary>El único valor que habilita. Todo lo demás cae del lado seguro.</summary>
    private const string ValorQueHabilita = "true";

    /// <summary>
    /// ¿Corresponde registrar el sustituto de identidad de desarrollo? Solo si el entorno es
    /// <c>Development</c> <b>y además</b> <see cref="ClaveDeConfiguracion"/> vale <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Las dos condiciones son independientes a propósito (mitigación 1 de R-01): un despliegue con
    /// <c>ASPNETCORE_ENVIRONMENT=Development</c> mal puesto no alcanza para que aparezca el desplegable
    /// de identidades, porque además haría falta la clave.
    /// </para>
    /// <para>
    /// La clave se compara por igualdad con «true», sin distinguir mayúsculas, y <b>no</b> se interpreta
    /// con un conversor de booleanos. Dos motivos: un valor que no se puede interpretar —«sí», «on»—
    /// haría lanzar al conversor y el arranque fallaría por un motivo que no es el suyo; y las
    /// abreviaturas que uno escribe por costumbre —«1», «yes»— ampliarían en silencio la superficie de
    /// R-01. La dirección en la que conviene equivocarse es la de no habilitar.
    /// </para>
    /// </remarks>
    public static bool PermiteIdentidadDeDesarrollo(IConfiguration configuracion, bool esDesarrollo)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        return esDesarrollo &&
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
    /// Si no hay ninguna implementación registrada, o si el entorno no es <c>Development</c> y la
    /// resuelta no es <see cref="EmpleadoActualNoConfigurado"/>.
    /// </exception>
    public static void Verificar(IServiceProvider servicios, IHostEnvironment entorno)
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

        if (!entorno.IsDevelopment() && tipoResuelto != typeof(EmpleadoActualNoConfigurado))
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
