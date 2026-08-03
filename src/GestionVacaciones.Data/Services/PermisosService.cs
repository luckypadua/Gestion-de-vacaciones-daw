namespace GestionVacaciones.Data.Services;

/// <summary>
/// Se pidieron las solicitudes de un empleado que quien consulta no puede ver.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué es una excepción y no una colección vacía.</b> Un listado vacío significa «no tenés
/// solicitudes», que es una respuesta legítima y frecuente. Devolverlo ante una negación haría que las
/// dos situaciones se vieran idénticas en pantalla, y la que oculta un problema de permisos es
/// justamente la que nadie iría a mirar. La tabla de errores del bloque lo nombra explícitamente.
/// </para>
/// <para>
/// <b>Por qué deriva de <see cref="UnauthorizedAccessException"/> y no de
/// <see cref="InvalidOperationException"/>.</b> Para que sea <b>distinguible</b> de
/// <see cref="SinEmpleadoSeleccionadoException"/>, que sí es una operación pedida en un estado que no la
/// admite. Los dos casos no entregan listado y significan cosas opuestas —«no te corresponde» frente a
/// «todavía no elegiste a nadie, mostrá el selector»—: con una jerarquía común, el único modo de
/// separarlos sería leer el texto del mensaje, que es exactamente cómo se terminan tratando igual.
/// </para>
/// <para>
/// <b>R-12:</b> el mensaje lleva identificadores de empleado y <b>nunca</b> nombre ni correo. Termina en
/// un log.
/// </para>
/// </remarks>
public sealed class AccesoASolicitudesDenegadoException : UnauthorizedAccessException
{
    /// <param name="mensaje">
    /// Motivo, con los dos identificadores en juego. Lo redacta <see cref="PermisosService"/>, que es el
    /// único que puede construir esta excepción con sentido: es el que tomó la decisión.
    /// </param>
    public AccesoASolicitudesDenegadoException(string mensaje)
        : base(mensaje)
    {
    }
}

/// <summary>
/// <b>Única</b> sede de la decisión de quién puede ver las solicitudes de quién.
/// </summary>
/// <remarks>
/// <para>
/// <c>AGENTS.md</c> lo exige así: «quién puede ver o resolver las solicitudes de quién se decide
/// <b>solo</b> en <c>PermisosService</c>, en ningún otro lugar». En FEAT-001a la sede contiene una sola
/// regla —un empleado ve sus propias solicitudes— y esa es exactamente la razón de crearla ahora:
/// FEAT-001b, FEAT-001c y el ticket de aprobación van a agregar acá «y el manager ve las de su equipo, y
/// el designado también». Si esa primera regla hubiera quedado como un <c>Where</c> dentro de
/// <see cref="SolicitudesService"/>, la segunda se escribiría al lado y la decisión ya estaría en dos
/// lugares antes de que nadie lo note.
/// </para>
/// <para>
/// <b>No toca la base.</b> Decidir si alguien puede ver un listado no es una consulta: es una regla sobre
/// la identidad de quien consulta y el sujeto del listado. El día que la regla necesite el organigrama
/// —el manager y el designado ya están en el modelo— este tipo recibirá una fábrica de contextos como
/// todos los demás (NFR-05); hoy pedirla sería declarar una dependencia que no usa.
/// </para>
/// <para>
/// <b>Fuera de alcance en FEAT-001a:</b> la vista del manager y del designado, y la denegación con 403
/// del PRD-001 (RF-09, AC-11), que depende de la identidad real de RF-01. Este tipo decide; traducir la
/// negación a una respuesta HTTP no es asunto suyo.
/// </para>
/// </remarks>
public sealed class PermisosService
{
    /// <summary>
    /// ¿Puede <paramref name="quienConsulta"/> ver las solicitudes de
    /// <paramref name="empleadoDeLasSolicitudes"/>?
    /// </summary>
    /// <remarks>
    /// La única regla del ticket: cada uno ve las propias. La comparación es por identificador y no por
    /// ninguna propiedad de la persona.
    /// </remarks>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si <paramref name="quienConsulta"/> no tiene empleado seleccionado. <b>No devuelve <c>false</c></b>:
    /// un <c>false</c> significa «te lo negué», que es una decisión de visibilidad, y acá no hubo ninguna
    /// decisión posible porque no hay sujeto sobre el que decidir. Colapsarlos mandaría a la interfaz a
    /// mostrar un cartel de acceso denegado cuando lo que corresponde es el selector de empleado.
    /// </exception>
    public bool PuedeVerLasSolicitudesDe(IdentidadDelEmpleado quienConsulta, int empleadoDeLasSolicitudes)
    {
        ArgumentNullException.ThrowIfNull(quienConsulta);

        return quienConsulta.Id == empleadoDeLasSolicitudes;
    }

    /// <summary>
    /// Igual que <see cref="PuedeVerLasSolicitudesDe"/>, pero <b>niega</b> en lugar de contestar: es la
    /// forma que usan los servicios, para que la negación no pueda ignorarse por descuido.
    /// </summary>
    /// <exception cref="AccesoASolicitudesDenegadoException">Si no le corresponde verlas.</exception>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si <paramref name="quienConsulta"/> no tiene empleado seleccionado.
    /// </exception>
    public void ExigirPoderVerLasSolicitudesDe(IdentidadDelEmpleado quienConsulta, int empleadoDeLasSolicitudes)
    {
        if (PuedeVerLasSolicitudesDe(quienConsulta, empleadoDeLasSolicitudes))
        {
            return;
        }

        // Los dos identificadores, que son lo único accionable para quien diagnostica, y nada más: ni
        // nombre ni correo (R-12).
        throw new AccesoASolicitudesDenegadoException(
            $"El empleado {quienConsulta.Id} pidió las solicitudes del empleado " +
            $"{empleadoDeLasSolicitudes} y no le corresponde verlas. En FEAT-001a cada empleado ve " +
            "únicamente las propias; la vista del manager y del designado es de un ticket futuro.");
    }
}
