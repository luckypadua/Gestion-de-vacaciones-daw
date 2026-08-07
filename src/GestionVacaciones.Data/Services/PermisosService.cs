using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;

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
/// <b>La regla de FEAT-001a no toca la base:</b> decidir si alguien ve sus propias solicitudes es una
/// comparación de identificadores, no una consulta. Desde FEAT-002 (Bloque 2) la sede también sabe
/// responder «¿es el manager?» o «¿es el designado del manager?», y esas dos preguntas sí necesitan el
/// organigrama (<see cref="Empleado.ManagerId"/>/<see cref="Empleado.DesignadoId"/>): de ahí el
/// constructor sobrecargado que recibe <see cref="IDbContextFactory{TContext}"/> (NFR-05).
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
    /// Fábrica de contextos para los métodos async de organigrama. <c>null</c> cuando la instancia se
    /// construyó con el constructor de 0 parámetros (<see cref="PermisosService()"/>), que solo respalda
    /// los métodos síncronos de FEAT-001a.
    /// </summary>
    private readonly IDbContextFactory<VacacionesDbContext>? _fabrica;

    /// <summary>
    /// Constructor de FEAT-001a, **sin cambios**: 21 sitios de test lo siguen usando y solo respalda los
    /// métodos síncronos. No agrega parámetros opcionales — eso sería, en los hechos, reemplazarlo.
    /// </summary>
    public PermisosService()
    {
    }

    /// <summary>
    /// Constructor de FEAT-002 (Bloque 2), <b>sobrecarga</b> del anterior y no su reemplazo: habilita los
    /// tres métodos async que necesitan el organigrama.
    /// </summary>
    /// <param name="fabrica">
    /// Fábrica de contextos. Siempre <see cref="IDbContextFactory{TContext}"/> y nunca un
    /// <c>DbContext</c> inyectado (<c>AGENTS.md</c>): cada método abre y cierra el suyo.
    /// </param>
    public PermisosService(IDbContextFactory<VacacionesDbContext> fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        _fabrica = fabrica;
    }

    /// <summary>
    /// La fábrica, o el error de uso que corresponde si esta instancia se construyó sin ella.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Si la instancia se construyó con <see cref="PermisosService()"/>: ese constructor no recibe
    /// fábrica, así que llamar a un método que consulta el organigrama sobre una instancia así es un
    /// error de uso del llamador, no un estado de negocio — se señaliza con la misma familia de
    /// excepción que <c>SaldoService</c> usa para «operación pedida en un estado que no la admite», no
    /// con un <c>null</c> silencioso ni con una degradación a «no autorizado».
    /// </exception>
    private IDbContextFactory<VacacionesDbContext> Fabrica =>
        _fabrica ?? throw new InvalidOperationException(
            $"{nameof(PermisosService)} se construyó con el constructor sin parámetros, que solo " +
            "respalda los métodos síncronos de FEAT-001a. Los métodos que consultan el organigrama " +
            $"({nameof(PuedeVerLasSolicitudesDeAsync)}, {nameof(PuedeResolverLasSolicitudesDeAsync)}, " +
            $"{nameof(EmpleadosBajoAutoridadDeAsync)}) necesitan la fábrica de contextos: construí la " +
            $"instancia con el constructor que recibe {nameof(IDbContextFactory<VacacionesDbContext>)}.");

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

    /// <summary>
    /// ¿Puede <paramref name="quienConsulta"/> ver las solicitudes de
    /// <paramref name="empleadoDeLasSolicitudes"/>? FEAT-002 (FR-06): además de la propia persona
    /// (<see cref="PuedeVerLasSolicitudesDe"/>), también puede su manager y el designado de su manager.
    /// </summary>
    /// <remarks>
    /// Delega la comparación de «es la propia persona» en el método síncrono existente en vez de
    /// repetirla: la regla de FEAT-001a sigue viviendo en un único lugar.
    /// </remarks>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si <paramref name="quienConsulta"/> no tiene empleado seleccionado. Se resuelve antes de abrir
    /// ningún contexto (mismo criterio que el método síncrono): no se degrada a <c>false</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Si esta instancia se construyó con <see cref="PermisosService()"/>, ver <see cref="Fabrica"/>.
    /// </exception>
    public async Task<bool> PuedeVerLasSolicitudesDeAsync(
        IdentidadDelEmpleado quienConsulta,
        int empleadoDeLasSolicitudes,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(quienConsulta);

        if (PuedeVerLasSolicitudesDe(quienConsulta, empleadoDeLasSolicitudes))
        {
            return true;
        }

        return await EsManagerODesignadoDeAsync(quienConsulta.Id, empleadoDeLasSolicitudes, cancelacion)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Igual que <see cref="PuedeVerLasSolicitudesDeAsync"/>, pero <b>niega</b> en lugar de contestar.
    /// </summary>
    /// <exception cref="AccesoASolicitudesDenegadoException">Si no le corresponde verlas.</exception>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si <paramref name="quienConsulta"/> no tiene empleado seleccionado.
    /// </exception>
    public async Task ExigirPoderVerLasSolicitudesDeAsync(
        IdentidadDelEmpleado quienConsulta,
        int empleadoDeLasSolicitudes,
        CancellationToken cancelacion = default)
    {
        if (await PuedeVerLasSolicitudesDeAsync(quienConsulta, empleadoDeLasSolicitudes, cancelacion)
            .ConfigureAwait(false))
        {
            return;
        }

        // Los dos identificadores, nunca nombre ni correo (R-12).
        throw new AccesoASolicitudesDenegadoException(
            $"El empleado {quienConsulta.Id} pidió las solicitudes del empleado " +
            $"{empleadoDeLasSolicitudes} y no le corresponde verlas: no es la propia persona, ni su " +
            "manager, ni el designado de su manager.");
    }

    /// <summary>
    /// ¿Puede <paramref name="quienConsulta"/> <b>resolver</b> (aprobar o rechazar) las solicitudes de
    /// <paramref name="empleadoDeLasSolicitudes"/>? FEAT-002 (FR-01, FR-02): únicamente el manager o el
    /// designado de su manager — <b>nunca</b> la propia persona, aunque
    /// <see cref="PuedeVerLasSolicitudesDeAsync"/> para el mismo par devuelva <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <b>Mitigación de R-22 — el <c>self</c> se excluye como condición propia del método</b>, antes de
    /// consultar el organigrama y no como consecuencia accidental de la consulta. Así, ni un dato
    /// externo anómalo (<c>Empleado.ManagerId == Empleado.Id</c> del propio empleado, algo que este
    /// ticket no escribe pero que PRD-001 declara administrado externamente) alcanzaría a autorizar la
    /// autoaprobación: la condición de exclusión no depende de qué contesten <c>ManagerId</c>/
    /// <c>DesignadoId</c>.
    /// </remarks>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si <paramref name="quienConsulta"/> no tiene empleado seleccionado. Se resuelve antes de abrir
    /// ningún contexto: no se degrada a <c>false</c>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Si esta instancia se construyó con <see cref="PermisosService()"/>, ver <see cref="Fabrica"/>.
    /// </exception>
    public async Task<bool> PuedeResolverLasSolicitudesDeAsync(
        IdentidadDelEmpleado quienConsulta,
        int empleadoDeLasSolicitudes,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(quienConsulta);
        var sujeto = quienConsulta.Id;

        if (sujeto == empleadoDeLasSolicitudes)
        {
            return false;
        }

        return await EsManagerODesignadoDeAsync(sujeto, empleadoDeLasSolicitudes, cancelacion)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Igual que <see cref="PuedeResolverLasSolicitudesDeAsync"/>, pero <b>niega</b> en lugar de
    /// contestar: es la forma que usa <c>SolicitudesService.ResolverAsync</c>, para que la negación no
    /// pueda ignorarse por descuido.
    /// </summary>
    /// <exception cref="AccesoASolicitudesDenegadoException">Si no le corresponde resolverlas.</exception>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si <paramref name="quienConsulta"/> no tiene empleado seleccionado.
    /// </exception>
    public async Task ExigirPoderResolverLasSolicitudesDeAsync(
        IdentidadDelEmpleado quienConsulta,
        int empleadoDeLasSolicitudes,
        CancellationToken cancelacion = default)
    {
        if (await PuedeResolverLasSolicitudesDeAsync(quienConsulta, empleadoDeLasSolicitudes, cancelacion)
            .ConfigureAwait(false))
        {
            return;
        }

        // Los dos identificadores, nunca nombre ni correo (R-12).
        throw new AccesoASolicitudesDenegadoException(
            $"El empleado {quienConsulta.Id} intentó resolver la solicitud del empleado " +
            $"{empleadoDeLasSolicitudes} y no le corresponde: solo pueden resolverla su manager o el " +
            "designado de su manager, nunca la propia persona.");
    }

    /// <summary>
    /// Los <see cref="Empleado.Id"/> sobre los que <paramref name="quienConsulta"/> tiene autoridad: es
    /// su manager, o es el designado de su manager. FEAT-002 (FR-05), base de
    /// <c>SolicitudesService.ListarPendientesDelEquipoAsync</c> (Bloque 4).
    /// </summary>
    /// <remarks>
    /// <b>Lista vacía si no tiene ningún equipo a cargo — no es un error</b>: es la respuesta legítima de
    /// alguien sin autoridad sobre nadie, mismo criterio que el resto del dominio distingue «sin datos»
    /// de «sin identidad» y de «error».
    /// </remarks>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si <paramref name="quienConsulta"/> no tiene empleado seleccionado. Se resuelve antes de abrir
    /// ningún contexto: no se degrada a lista vacía, que se leería como «no tenés equipo».
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Si esta instancia se construyó con <see cref="PermisosService()"/>, ver <see cref="Fabrica"/>.
    /// </exception>
    public async Task<IReadOnlyList<int>> EmpleadosBajoAutoridadDeAsync(
        IdentidadDelEmpleado quienConsulta,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(quienConsulta);
        var sujeto = quienConsulta.Id;

        await using var contexto = await Fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        // Proyección mínima (R-22, C15-I): solo el Id, nunca Nombre ni Correo.
        return await contexto.Empleados
            .AsNoTracking()
            .Where(empleado => empleado.ManagerId == sujeto || empleado.DesignadoId == sujeto)
            .Select(empleado => empleado.Id)
            .ToListAsync(cancelacion)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// ¿<paramref name="posibleAutoridadId"/> es el manager de <paramref name="empleadoId"/>, o el
    /// designado de su manager? Único punto que consulta el organigrama para las dos preguntas
    /// booleanas de arriba, así que la proyección mínima (R-22) está escrita una sola vez.
    /// </summary>
    private async Task<bool> EsManagerODesignadoDeAsync(
        int posibleAutoridadId, int empleadoId, CancellationToken cancelacion)
    {
        await using var contexto = await Fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        // Proyección mínima (R-22, C15-I): solo ManagerId y DesignadoId, nunca Nombre ni Correo.
        var organigrama = await contexto.Empleados
            .AsNoTracking()
            .Where(empleado => empleado.Id == empleadoId)
            .Select(empleado => new { empleado.ManagerId, empleado.DesignadoId })
            .SingleOrDefaultAsync(cancelacion)
            .ConfigureAwait(false);

        return organigrama is not null
            && (organigrama.ManagerId == posibleAutoridadId || organigrama.DesignadoId == posibleAutoridadId);
    }
}
