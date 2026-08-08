using GestionVacaciones.Data.Services;

namespace GestionVacaciones.Web.Identidad;

/// <summary>
/// Implementación de identidad para todo entorno donde el sustituto de desarrollo <b>no</b> está
/// habilitado: <b>lanza</b> en lugar de devolver un empleado por defecto (AC-06).
/// </summary>
/// <remarks>
/// <para>
/// Es el valor por defecto de la doble condición de R-01: ausente o falsa la clave
/// <c>Vacaciones:PermitirIdentidadDeDesarrollo</c>, o fuera de <c>Development</c>, es esto lo que queda
/// registrado. <i>Secure by default</i>: para habilitar la identidad de desarrollo hay que declarar dos
/// cosas, para no habilitarla no hay que hacer nada.
/// </para>
/// <para>
/// <b>Por qué lanza en vez de devolver «nadie».</b> Un proveedor que devolviera un empleado vacío
/// dejaría la aplicación funcionando a medias, atribuyendo solicitudes a nadie y mostrando listados
/// ajenos como propios. Lanzar convierte una suplantación silenciosa en un error visible.
/// </para>
/// <para>
/// <b>Nota de diseño, heredada de la spec.</b> Esto es comportamiento productivo alojado en el proyecto
/// de UI. Es válido mientras <c>Web</c> sea el único host: si aparece un worker o un job por lotes,
/// heredará la interfaz pero no este guardarraíl, y habrá que mover las dos implementaciones.
/// </para>
/// </remarks>
public sealed class EmpleadoActualNoConfigurado : IEmpleadoActualProvider
{
    /// <summary>
    /// El mensaje nombra <b>RF-01</b> —la autenticación OAuth del PRD-001— como la vía correcta, y las
    /// dos condiciones del sustituto de desarrollo. Quien se encuentre con esta excepción tiene que
    /// saber qué falta, no solo que algo falló. No lleva ningún dato personal (R-12).
    /// </summary>
    public const string MensajeDeNegacion =
        "No hay identidad de empleado configurada en este entorno y no se devuelve ningún empleado por " +
        "defecto. La vía correcta es RF-01, la autenticación OAuth del PRD-001, que este ticket no " +
        "implementa. El sustituto de desarrollo solo se registra en el entorno Development y con la " +
        "clave «Vacaciones:PermitirIdentidadDeDesarrollo» en true.";

    /// <summary>
    /// <b>Contesta «no». No lanza, y es el único miembro de esta clase que no lo hace.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Esto contradice a propósito la regla general de la clase, así que no es un olvido: no lo
    /// «arregles» haciendo que lance.</b> Si lanzara, la interfaz no tendría forma de <i>preguntar</i> si
    /// corresponde ofrecer el selector, y para averiguarlo tendría que nombrar una implementación
    /// concreta, capturar la excepción como control de flujo, o releer el entorno y la clave de
    /// configuración por su cuenta. Los tres abren una segunda sede de la identidad, que es lo que NFR-06
    /// cuenta en cero, y el tercero además duplica la decisión de R-01 lejos de donde vive.
    /// </para>
    /// <para>
    /// Y no debilita nada: lo que este miembro entrega es un <c>false</c>, no un empleado. Los dos
    /// miembros por los que se obtiene o se cambia una identidad siguen lanzando (AC-06).
    /// </para>
    /// </remarks>
    public bool PermiteElegirEmpleado => false;

    /// <inheritdoc/>
    public IdentidadDelEmpleado Identidad => throw Negar();

    /// <inheritdoc/>
    public Task<ResultadoDeSeleccion> SeleccionarAsync(int empleadoId, CancellationToken cancelacion = default) =>
        throw Negar();

    /// <summary>
    /// Declarado por contrato de la interfaz. Nunca se dispara: <see cref="SeleccionarAsync"/> en
    /// esta clase siempre lanza, así que jamás se llega al punto donde cambiaría la identidad.
    /// Accesores explícitos y vacíos, no un evento de campo: uno de campo que nunca se invoca es
    /// <c>CS0067</c>, y con <c>TreatWarningsAsErrors</c> eso rompe el build.
    /// </summary>
    public event EventHandler? IdentidadCambiada
    {
        add { }
        remove { }
    }

    /// <summary>
    /// <see cref="InvalidOperationException"/> <b>exactamente</b>, nunca una derivada: el tipo es lo que
    /// distingue «no hay identidad configurada» de «todavía no elegiste a nadie»
    /// (<see cref="SinEmpleadoSeleccionadoException"/>), que son dos estados opuestos que tampoco
    /// entregan empleado.
    /// </summary>
    private static InvalidOperationException Negar() => new(MensajeDeNegacion);
}
