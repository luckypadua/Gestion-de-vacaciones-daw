using GestionVacaciones.Data.Services;
using Xunit;

namespace GestionVacaciones.Tests.Identidad;

/// <summary>
/// Mitigación de <b>R-12</b> en el único punto por el que la PII se escapa <i>sin que nadie la escriba</i>:
/// el <c>ToString()</c> de los tipos de identidad, que es lo que un log o un mensaje de error interpolan
/// cuando alguien pasa el objeto entero.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué hace falta afirmarlo.</b> La spec del ticket dice «se registra <c>EmpleadoId</c>, nunca
/// nombre ni correo», y hoy eso se cumple porque nadie registra estos objetos. Es una propiedad del
/// código que hay <i>ahora</i>, no del tipo: los Bloques 5 y 6 son los que van a loguear y los que van a
/// redactar mensajes de error, y el camino corto —interpolar el objeto— es exactamente el que un
/// <c>ToString()</c> autogenerado convierte en una fuga silenciosa, sin una línea de código que la
/// delate en la revisión.
/// </para>
/// <para>
/// <b>Por qué en <c>Identidad/</c>.</b> Los dos tipos son del Bloque 4 —la identidad del empleado actual
/// y la proyección de la nómina que alimenta su selector— y la spec fija esa carpeta para el bloque. El
/// criterio es el mismo que ya siguen <c>ComposicionDeIdentidadTests</c> e
/// <c>IdentidadSinSeleccionarTests</c>: el test vive junto a la <i>preocupación</i> que verifica.
/// </para>
/// </remarks>
public sealed class DiagnosticoSinPiiTests
{
    /// <summary>
    /// Nombre imposible de encontrar por casualidad en una descripción de diagnóstico. Es deliberado: con
    /// «Ana» o «Bruno» —los de la nómina sembrada— un aserto de «no contiene» podría pasar en verde por
    /// coincidencia, o fallar por una subcadena inocente.
    /// </summary>
    private const string NombreInconfundible = "Zoraida-Que-Nunca-Debe-Aparecer-En-Un-Log";

    [Fact]
    public void El_ToString_de_EmpleadoDeLaNomina_no_lleva_el_nombre_del_empleado()
    {
        var deLaNomina = new EmpleadoDeLaNomina(4321, NombreInconfundible);

        var descripcion = deLaNomina.ToString();

        // El nombre es PII (F-TM-05) y un record imprime TODAS sus propiedades: sin un ToString() propio,
        // esta línea encuentra el nombre del empleado dentro de la cadena que va a terminar en un log.
        Assert.DoesNotContain(NombreInconfundible, descripcion, StringComparison.Ordinal);

        // Y la contracara imprescindible: el identificador SÍ tiene que estar. Sin este aserto, «no
        // filtra el nombre» lo cumpliría también una descripción vacía o un ToString() que devolviera
        // solo el nombre del tipo, y entonces el diagnóstico no serviría para nada —R-12 pide reemplazar
        // la PII por el EmpleadoId, no borrar la información—.
        Assert.Contains("4321", descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void El_ToString_de_IdentidadDelEmpleado_no_lleva_nada_mas_que_el_Id()
    {
        var identidad = IdentidadDelEmpleado.De(7);

        var descripcion = identidad.ToString();

        // La propiedad que su documentación ya declara: «lleva el identificador y nunca nombre ni
        // correo». Los únicos dígitos de la cadena son los del Id, así que no viaja ningún otro dato.
        Assert.Equal("7", new string([.. descripcion.Where(char.IsDigit)]));

        // Y nada que huela a los dos campos PII de la entidad. Un futuro
        // «$"... Nombre = {_nombre}"» —el atajo natural para hacer más legible un log— pone esto en rojo.
        Assert.DoesNotContain("Nombre", descripcion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Correo", descripcion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void El_ToString_de_una_identidad_sin_seleccionar_lo_dice_en_vez_de_lanzar()
    {
        // El estado sin seleccionar es el que tiene todo circuito recién abierto, y describirlo no puede
        // costar una excepción: un ToString() que interpolara la propiedad Id lanzaría
        // SinEmpleadoSeleccionadoException justo cuando alguien está intentando diagnosticar algo. Es la
        // rama que hoy no ejecutaba ningún test.
        var descripcion = IdentidadDelEmpleado.SinSeleccionar.ToString();

        Assert.Contains("sin empleado seleccionado", descripcion, StringComparison.OrdinalIgnoreCase);

        // Sin dígitos: no hay ningún Id que mostrar y un 0 se leería como un empleado válido.
        Assert.DoesNotContain(descripcion, character => char.IsDigit(character));
    }
}
