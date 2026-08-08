using GestionVacaciones.Data.Services;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Proveedor de identidad para los tests del dominio: entrega el empleado que el test declaró, o el
/// estado explícito «todavía no hay ninguno».
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué existe, si el proyecto Web ya trae dos implementaciones.</b> Las dos de <c>Web</c> son
/// comportamiento productivo —una suplanta en desarrollo, la otra niega fuera de él— y ninguna permite
/// declarar la identidad sin pasar por la nómina de la base. Los tests de este bloque afirman sobre el
/// alta y el listado, no sobre la resolución de la identidad: esa la fija el Bloque 4.
/// </para>
/// <para>
/// Vive en el proyecto de tests a propósito. El guardarraíl de NFR-06
/// —<c>B4_T7_Las_unicas_implementaciones_de_la_interfaz_son_las_dos_del_proyecto_Web</c>— cuenta las
/// implementaciones de <c>Data</c> y de <c>Web</c>: un doble en <c>src/</c> sería una tercera sede de la
/// identidad y lo pondría en rojo, con razón.
/// </para>
/// </remarks>
internal sealed class IdentidadDePrueba : IEmpleadoActualProvider
{
    private IdentidadDePrueba(IdentidadDelEmpleado identidad) => Identidad = identidad;

    public bool PermiteElegirEmpleado => true;

    public IdentidadDelEmpleado Identidad { get; }

    /// <summary>El circuito ya tiene identidad: es <paramref name="empleadoId"/>.</summary>
    public static IdentidadDePrueba De(int empleadoId) => new(IdentidadDelEmpleado.De(empleadoId));

    /// <summary>
    /// Nadie eligió todavía. Pedir el <c>Id</c> lanza <see cref="SinEmpleadoSeleccionadoException"/>, que
    /// es justamente lo que B5-T6 ejercita.
    /// </summary>
    public static IdentidadDePrueba SinSeleccionar() => new(IdentidadDelEmpleado.SinSeleccionar);

    public Task<ResultadoDeSeleccion> SeleccionarAsync(int empleadoId, CancellationToken cancelacion = default) =>
        throw new NotSupportedException(
            "Los tests del dominio no cambian la identidad del circuito: la declaran al construir el " +
            "proveedor. Cambiarla es competencia del Bloque 4 y sus tests la cubren allá.");
}
