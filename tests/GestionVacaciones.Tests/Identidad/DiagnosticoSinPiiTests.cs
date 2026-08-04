using System.Reflection;
using GestionVacaciones.Data.Entidades;
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

    /// <summary>
    /// Año de las tres fechas de la solicitud de prueba —período y creación—. Ninguno de los otros
    /// valores lo contiene como subcadena, así que buscarlo en la descripción encuentra <b>cualquiera</b>
    /// de las tres si alguna se cuela.
    /// </summary>
    private const string AnioDeLasFechas = "2027";

    [Fact]
    public void El_ToString_de_SolicitudDelListado_no_lleva_el_periodo_de_las_vacaciones()
    {
        // El dato que protege R-12 acá no es el nombre sino el PERÍODO: cuándo no va a estar una
        // persona identificada es un dato personal suyo, y esta proyección lo lleva junto al EmpleadoId
        // que la identifica. El ToString() está escrito a mano para dejarlo afuera, y hasta ahora nada
        // lo fijaba: borrar el override —o dejarlo intacto y agregarle una fecha— devolvía el dato al
        // log sin que nada se pusiera en rojo.
        var solicitud = new SolicitudDelListado(
            Id: 931,
            EmpleadoId: 4321,
            FechaInicio: new DateOnly(2027, 3, 17),
            FechaFin: new DateOnly(2027, 3, 29),
            DiasCorridos: 13,
            Estado: EstadoSolicitud.Pendiente,
            FechaCreacion: new DateTimeOffset(2027, 1, 8, 10, 30, 0, TimeSpan.FromHours(-3)));

        var descripcion = solicitud.ToString();

        // El año es común a las tres fechas y no aparece en ningún otro valor: si el ToString() que
        // genera el record volviera —imprime TODAS las propiedades—, esta línea lo encuentra.
        Assert.DoesNotContain(AnioDeLasFechas, descripcion, StringComparison.Ordinal);

        // Y los nombres de las propiedades del período, que es como el record las rotula. Cubre el caso
        // en que las fechas se formateen de una manera que no incluya el año.
        Assert.DoesNotContain(nameof(SolicitudDelListado.FechaInicio), descripcion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(SolicitudDelListado.FechaFin), descripcion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(SolicitudDelListado.FechaCreacion), descripcion, StringComparison.OrdinalIgnoreCase);

        // La contracara imprescindible: sin ella, «no filtra el período» lo cumpliría también una
        // descripción vacía, y R-12 pide reemplazar el dato personal por los identificadores, no borrar
        // el diagnóstico.
        Assert.Contains("931", descripcion, StringComparison.Ordinal);
        Assert.Contains("4321", descripcion, StringComparison.Ordinal);
        Assert.Contains(nameof(EstadoSolicitud.Pendiente), descripcion, StringComparison.Ordinal);
    }

    [Fact]
    public void El_ToString_de_un_alta_creada_no_lleva_mas_que_el_identificador()
    {
        // Un alta creada se describe con su identificador y nada más: el período que se acaba de
        // registrar no tiene por qué aparecer en un log.
        var creada = ResultadoDelAlta.Creada(931);

        Assert.Equal("931", new string([.. creada.ToString().Where(char.IsDigit)]));
    }

    [Fact]
    public void El_ToString_de_un_rechazo_no_lleva_nada_mas_que_el_literal_que_lo_motivo()
    {
        // La otra mitad del camino del alta: un rechazo se describe con el literal del PRD que lo
        // motivó —un texto fijo, igual para todas las personas— y con nada más. Lo que R-12 protege acá
        // es el período que se intentó registrar.
        //
        // OJO AL FORMULARLO. La versión anterior de este test afirmaba que la descripción no tenía
        // NINGÚN dígito, razonando que el único origen posible de un dígito eran las fechas. Eso ya es
        // falso: el PRD tiene escrito el literal de FEAT-001b —«No dispones de días suficientes. Tu
        // saldo actual es de X días»—, que lleva un número propio y legítimo. El día que ese literal
        // exista, aquella versión se ponía roja sin que se hubiera filtrado nada, y un test que grita
        // por un motivo que no es el suyo es un test que se termina borrando. No lo "simplifiques" de
        // vuelta a contar dígitos.
        //
        // La formulación correcta es esta: los dígitos que puede haber son EXACTAMENTE los del literal.
        // Cualquier otro solo podría venir de un dato que este resultado no tiene por qué describir.
        var literales = LiteralesDeRechazo();

        // Sin esto, un ErroresDeSolicitud vacío o renombrado dejaría el bucle sin iteraciones y el test
        // pasaría en verde sin haber comprobado nada.
        Assert.NotEmpty(literales);

        foreach (var literal in literales)
        {
            var descripcion = ResultadoDelAlta.Rechazada(literal).ToString();

            // El motivo viaja entero: es lo que hace útil al diagnóstico.
            Assert.Contains(literal, descripcion, StringComparison.Ordinal);

            // Y alrededor del motivo no queda ningún dígito. Una fecha —«17/03/2027»— cae acá sí o sí,
            // en cualquier formato que se le dé.
            var alrededorDelLiteral = descripcion.Replace(literal, string.Empty, StringComparison.Ordinal);

            Assert.DoesNotContain(alrededorDelLiteral, character => char.IsDigit(character));
        }
    }

    /// <summary>
    /// Los mensajes de rechazo declarados, leídos del propio <see cref="ErroresDeSolicitud"/>.
    /// </summary>
    /// <remarks>
    /// Por reflexión y no a mano: los sub-tickets siguientes agregan los suyos —el tope anual en
    /// FEAT-001b, la superposición en FEAT-001c— y una lista escrita acá los dejaría fuera sin que
    /// nada avisara, que es como un guardarraíl deja de cubrir justo lo que se agrega después.
    /// </remarks>
    private static IReadOnlyList<string> LiteralesDeRechazo() =>
        [.. typeof(ErroresDeSolicitud)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(campo => campo is { IsLiteral: true, IsInitOnly: false })
            .Select(campo => campo.GetRawConstantValue())
            .OfType<string>()];

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
