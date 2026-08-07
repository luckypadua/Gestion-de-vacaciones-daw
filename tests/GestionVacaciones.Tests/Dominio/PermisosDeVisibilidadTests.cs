using System.Globalization;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// B5-T8: <see cref="PermisosService"/> es la <b>única</b> sede de la decisión de quién ve las
/// solicitudes de quién, como exige <c>AGENTS.md</c>, y niega el listado de otro empleado.
/// </summary>
/// <remarks>
/// <para>
/// En FEAT-001a la sede tiene una sola regla —un empleado ve sus propias solicitudes—, y esa es la razón
/// de crearla ahora: FEAT-001b, FEAT-001c y el ticket de aprobación van a copiar este patrón. Que la
/// regla sea corta no la vuelve prescindible; la vuelve el único lugar donde va a crecer.
/// </para>
/// <para>
/// Los casos de FEAT-001a no tocan la base: decidir si alguien puede ver un listado no es una consulta.
/// Los de FEAT-002 (Bloque 2), agregados acá, sí la necesitan —afirman sobre el resultado de consultar
/// el organigrama real—, así que la clase pasa a integrar <see cref="ColeccionDeBaseDeDatos"/>. El trait
/// de integración se aplica a <b>nivel de clase</b> y no por método: <c>CategoriaDeIntegracionTests</c>
/// decide quién usa la base por el tipo del fixture que recibe la clase, no caso por caso, así que los
/// cinco casos de FEAT-001a que no tocan la base quedan igual bajo el filtro que los seis de
/// <c>AltaDeSolicitudTests</c> que usan <c>FabricaQueNadieDebeUsar</c> sin abrir conexión.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class PermisosDeVisibilidadTests
{
    private const int Propio = 7;
    private const int Ajeno = 8;

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public PermisosDeVisibilidadTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public void B5_T8_Se_niega_el_listado_de_otro_empleado()
    {
        var permisos = new PermisosService();
        var quienConsulta = IdentidadDelEmpleado.De(Propio);

        Assert.False(permisos.PuedeVerLasSolicitudesDe(quienConsulta, Ajeno));

        // Y negar NO es devolver una colección vacía, que se confundiría con «no tiene solicitudes»:
        // exigir el permiso lanza. Es la propiedad que la tabla de errores del bloque nombra
        // explícitamente.
        var excepcion = Assert.Throws<AccesoASolicitudesDenegadoException>(
            () => permisos.ExigirPoderVerLasSolicitudesDe(quienConsulta, Ajeno));

        // R-12: el mensaje lleva identificadores, nunca nombre ni correo. Los dos Id son lo único
        // accionable para quien diagnostica.
        Assert.Contains(Propio.ToString(CultureInfo.InvariantCulture), excepcion.Message, StringComparison.Ordinal);
        Assert.Contains(Ajeno.ToString(CultureInfo.InvariantCulture), excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_empleado_si_puede_ver_sus_propias_solicitudes()
    {
        // La contracara imprescindible de B5-T8: sin ella, «niega el listado de otro» lo cumpliría
        // también una sede que niega SIEMPRE, y entonces nadie vería nunca nada y AC-05 estaría roto sin
        // que ningún test se pusiera rojo.
        var permisos = new PermisosService();
        var quienConsulta = IdentidadDelEmpleado.De(Propio);

        Assert.True(permisos.PuedeVerLasSolicitudesDe(quienConsulta, Propio));
        permisos.ExigirPoderVerLasSolicitudesDe(quienConsulta, Propio);
    }

    [Fact]
    public void La_negacion_es_distinguible_de_la_falta_de_identidad()
    {
        // Los dos casos no entregan listado y significan cosas opuestas: uno es «no te corresponde»,
        // el otro es «todavía no elegiste a nadie, mostrá el selector». Con el mismo tipo de excepción,
        // el Bloque 6 solo podría separarlos leyendo el texto del mensaje, que es cómo se terminan
        // tratando igual.
        var permisos = new PermisosService();

        var negado = Assert.Throws<AccesoASolicitudesDenegadoException>(
            () => permisos.ExigirPoderVerLasSolicitudesDe(IdentidadDelEmpleado.De(Propio), Ajeno));

        var sinIdentidad = Assert.Throws<SinEmpleadoSeleccionadoException>(
            () => permisos.ExigirPoderVerLasSolicitudesDe(IdentidadDelEmpleado.SinSeleccionar, Propio));

        Assert.NotEqual(negado.GetType(), sinIdentidad.GetType());
    }

    [Fact]
    public void Sin_identidad_no_se_concede_ni_se_niega_en_silencio()
    {
        // «No hay empleado seleccionado» no puede colapsar en un false: false significa «te lo negué»,
        // que es una decisión de visibilidad, y acá no hubo ninguna decisión posible porque no hay sujeto
        // que decidir. Devolver false mandaría al Bloque 6 a mostrar un cartel de acceso denegado cuando
        // lo que corresponde es el selector.
        var permisos = new PermisosService();

        Assert.Throws<SinEmpleadoSeleccionadoException>(
            () => permisos.PuedeVerLasSolicitudesDe(IdentidadDelEmpleado.SinSeleccionar, Propio));
    }

    [Fact]
    public void Ningun_otro_archivo_de_src_decide_negar_la_visibilidad()
    {
        // AGENTS.md: «Quién puede ver o resolver las solicitudes de quién se decide SOLO en
        // PermisosService, en ningún otro lugar». El código de hoy es una foto; esto es una propiedad del
        // código fuente, y es la que tiene que sobrevivir a FEAT-001b, a FEAT-001c y al ticket de
        // aprobación, que son los que van a querer agregar «si es el manager, también».
        //
        // Se busca la CONSTRUCCIÓN de la denegación, no su mención: consumirla —capturarla, documentarla
        // con <exception cref>— es legítimo y de hecho necesario. Lo que no puede haber en otro archivo
        // es el momento en que alguien decide decir no.
        var raiz = RaizDelRepositorio.Localizar();
        var codigo = Path.Combine(raiz, "src");

        const string laSede = "src/GestionVacaciones.Data/Services/PermisosService.cs";
        var construirLaDenegacion = $"new {nameof(AccesoASolicitudesDenegadoException)}";

        var archivos = Directory
            .EnumerateFiles(codigo, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(archivo => archivo.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                              archivo.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Select(archivo => Path.GetRelativePath(raiz, archivo).Replace('\\', '/'))
            .Where(relativa => !relativa.Split('/').Any(tramo => tramo is "bin" or "obj"))
            .ToList();

        // Sin esto el test pasaría en verde por no haber encontrado nada que revisar.
        Assert.NotEmpty(archivos);

        // Y sin esto pasaría en verde si nadie construyera nunca la denegación, o si el tipo se
        // renombrara: la sede tiene que seguir siendo la sede.
        Assert.Contains(laSede, archivos, StringComparer.Ordinal);
        Assert.Contains(
            construirLaDenegacion,
            File.ReadAllText(Path.Combine(raiz, laSede)),
            StringComparison.Ordinal);

        var infracciones = archivos
            .Where(relativa => !string.Equals(relativa, laSede, StringComparison.Ordinal))
            .Where(relativa => File.ReadAllText(Path.Combine(raiz, relativa))
                .Contains(construirLaDenegacion, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            infracciones.Count == 0,
            "Estos archivos deciden negar la visibilidad de las solicitudes fuera de PermisosService: " +
            $"{string.Join(", ", infracciones)}. Esa decisión vive en un punto único (AGENTS.md); el " +
            "resto del código la consume.");
    }

    /// <summary>
    /// FEAT-002 (FR-06, Bloque 2): además de la propia persona, el manager también puede ver las
    /// solicitudes de su equipo.
    /// </summary>
    [Fact]
    public async Task El_manager_puede_ver_las_solicitudes_de_su_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));
        var quienConsulta = IdentidadDelEmpleado.De(manager.Id);

        Assert.True(await permisos.PuedeVerLasSolicitudesDeAsync(quienConsulta, subordinado.Id, Cancelacion));

        // Y exigirlo no lanza.
        await permisos.ExigirPoderVerLasSolicitudesDeAsync(quienConsulta, subordinado.Id, Cancelacion);
    }

    /// <summary>
    /// FEAT-002 (FR-06, Bloque 2): el designado del manager tiene, sobre ese equipo, la misma capacidad
    /// de ver que el propio manager.
    /// </summary>
    [Fact]
    public async Task El_designado_puede_ver_las_solicitudes_del_equipo_de_su_manager()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var designado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerYDesignadoAsync(subordinado.Id, manager.Id, designado.Id);

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));

        Assert.True(await permisos.PuedeVerLasSolicitudesDeAsync(
            IdentidadDelEmpleado.De(designado.Id), subordinado.Id, Cancelacion));
    }

    /// <summary>
    /// Camino triste que valida FR-06/AC-08: sin ninguna relación de manager ni de designado, el acceso
    /// se sigue negando aunque ahora la sede consulte el organigrama real.
    /// </summary>
    [Fact]
    public async Task Un_empleado_sin_relacion_no_puede_ver_las_solicitudes_de_otro()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var quienConsulta = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var ajeno = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));
        var identidad = IdentidadDelEmpleado.De(quienConsulta.Id);

        Assert.False(await permisos.PuedeVerLasSolicitudesDeAsync(identidad, ajeno.Id, Cancelacion));

        var excepcion = await Assert.ThrowsAsync<AccesoASolicitudesDenegadoException>(
            () => permisos.ExigirPoderVerLasSolicitudesDeAsync(identidad, ajeno.Id, Cancelacion));

        // R-12: los dos identificadores, nunca nombre ni correo.
        Assert.Contains(
            quienConsulta.Id.ToString(CultureInfo.InvariantCulture), excepcion.Message, StringComparison.Ordinal);
        Assert.Contains(
            ajeno.Id.ToString(CultureInfo.InvariantCulture), excepcion.Message, StringComparison.Ordinal);
    }

    private async Task AsignarManagerAsync(int empleadoId, int managerId)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        var empleado = await contexto.Empleados
            .SingleAsync(candidato => candidato.Id == empleadoId, Cancelacion);
        empleado.ManagerId = managerId;
        await contexto.SaveChangesAsync(Cancelacion);
    }

    private async Task AsignarManagerYDesignadoAsync(int empleadoId, int managerId, int designadoId)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        var empleado = await contexto.Empleados
            .SingleAsync(candidato => candidato.Id == empleadoId, Cancelacion);
        empleado.ManagerId = managerId;
        empleado.DesignadoId = designadoId;
        await contexto.SaveChangesAsync(Cancelacion);
    }
}
