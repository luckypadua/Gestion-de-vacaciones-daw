using GestionVacaciones.Data.Services;

namespace GestionVacaciones.Web.Identidad;

/// <summary>
/// Sustituto de identidad <b>de desarrollo</b>: el empleado actual es el que se eligió en el selector,
/// sin credencial de ninguna clase. Se registra <b>solo</b> con la doble condición de R-01 —entorno
/// <c>Development</c> <i>y</i> la clave <c>Vacaciones:PermitirIdentidadDeDesarrollo</c> en <c>true</c>—
/// y es <b>scoped</b>, o sea uno por circuito Blazor Server.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué scoped y no singleton.</b> En Blazor Server el ámbito es el circuito. Como singleton, la
/// persona que elige un empleado se lo cambiaría a todas las demás: AC-07 se rompería de una forma que
/// ningún test unitario ve, porque cada instancia por separado funcionaría perfectamente. Transitorio
/// tampoco sirve: cada componente recibiría su propia copia y la elección del selector no llegaría ni
/// al formulario ni al listado.
/// </para>
/// <para>
/// <b>Suplantación por diseño (R-01, CRITICAL).</b> Elegir del desplegable <i>es</i> elevación de
/// privilegio: aceptable en desarrollo, catastrófico fuera. Lo que impide que salga de desarrollo no
/// es esta clase, son las dos condiciones del registro y la verificación de arranque
/// (<see cref="VerificacionDeIdentidad"/>).
/// </para>
/// <para>
/// <b>Estado en memoria, sin persistir nada.</b> La elección vive lo que vive el circuito. No hay nada
/// que escribir: al recargar, el circuito es otro y no hay empleado seleccionado, que es el estado
/// explícito y correcto.
/// </para>
/// </remarks>
public sealed class EmpleadoActualDesarrollo : IEmpleadoActualProvider
{
    private readonly EmpleadosService _nomina;

    private IdentidadDelEmpleado _identidad = IdentidadDelEmpleado.SinSeleccionar;

    /// <param name="nomina">
    /// Lectura de la nómina. La identidad no toca el <c>DbContext</c> (<c>AGENTS.md</c>): pasa por el
    /// servicio, que además es quien valida que el identificador recibido exista.
    /// </param>
    public EmpleadoActualDesarrollo(EmpleadosService nomina)
    {
        ArgumentNullException.ThrowIfNull(nomina);

        _nomina = nomina;
    }

    /// <summary>
    /// Este proveedor <b>es</b> el selector de identidad, así que la respuesta es siempre sí. No depende
    /// de si ya se eligió a alguien: al abrirse el circuito no hay nadie elegido y es justo entonces
    /// cuando la interfaz más necesita mostrar el selector.
    /// </summary>
    /// <remarks>
    /// Que la respuesta sea un <c>true</c> fijo, y que la decisión viva acá y no en la composición del
    /// host, es a propósito: quien responde es el proveedor que quedó registrado, y la doble condición de
    /// R-01 ya decidió cuál es. Volver a mirar el entorno y la clave desde acá sería duplicar esa
    /// decisión.
    /// </remarks>
    public bool PermiteElegirEmpleado => true;

    /// <summary>
    /// El empleado del circuito, o el estado explícito «todavía no hay ninguno». Nunca <c>null</c> y
    /// nunca un identificador inventado.
    /// </summary>
    public IdentidadDelEmpleado Identidad => _identidad;

    /// <summary>
    /// Cambia el empleado del circuito si el identificador existe en la nómina. Si no existe, <b>se
    /// conserva el anterior</b>: vaciar la identidad ante una entrada inválida le cambiaría el sujeto
    /// del listado a quien no pidió nada, y quedar en un estado intermedio sería peor que las dos.
    /// </summary>
    public async Task<ResultadoDeSeleccion> SeleccionarAsync(int empleadoId, CancellationToken cancelacion = default)
    {
        // El identificador viene del navegador por el circuito (TB-1) y no se le cree: tiene que existir
        // en la nómina. Confiar en que el desplegable solo ofrece valores válidos deja la validación del
        // lado del cliente, que es el modo de fallo que R-10 describe para las fechas y que acá tendría
        // peor consecuencia todavía —crear solicitudes a nombre de un empleado arbitrario—.
        if (!await _nomina.ExisteEnLaNominaAsync(empleadoId, cancelacion).ConfigureAwait(false))
        {
            return ResultadoDeSeleccion.RechazadaPorEmpleadoInexistente;
        }

        _identidad = IdentidadDelEmpleado.De(empleadoId);

        return ResultadoDeSeleccion.Seleccionado;
    }
}
