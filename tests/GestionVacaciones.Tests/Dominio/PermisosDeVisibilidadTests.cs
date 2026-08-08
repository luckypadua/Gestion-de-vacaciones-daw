using System.Globalization;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
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
/// Ninguno de estos casos toca la base: decidir si alguien puede ver un listado no es una consulta.
/// </para>
/// </remarks>
public sealed class PermisosDeVisibilidadTests
{
    private const int Propio = 7;
    private const int Ajeno = 8;

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
}
