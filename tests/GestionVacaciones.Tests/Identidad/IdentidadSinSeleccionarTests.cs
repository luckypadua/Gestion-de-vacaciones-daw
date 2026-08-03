using GestionVacaciones.Data;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Web.Identidad;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionVacaciones.Tests.Identidad;

/// <summary>
/// B4-T8 y la validación de entrada del bloque: qué dice el proveedor <b>antes</b> de que alguien
/// seleccione a alguien, y qué hace con un identificador que no puede existir.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué no llevan la categoría de integración.</b> Ninguno necesita la instancia SQL Server del
/// entorno: necesitan exactamente lo contrario. La fábrica de contextos que reciben <b>revienta si
/// alguien la usa</b>, y eso es parte de lo que se afirma —responder «todavía no hay empleado
/// seleccionado», o rechazar un identificador no positivo, no puede costar un viaje a la base—.
/// </para>
/// </remarks>
public sealed class IdentidadSinSeleccionarTests
{
    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public void B4_T8_Sin_empleado_seleccionado_el_proveedor_lo_comunica_de_forma_explicita()
    {
        var proveedor = ProveedorDeDesarrolloSinBase();

        var identidad = proveedor.Identidad;

        // Lo primero: NO es null. Un null silencioso es indistinguible de «hubo un error» y de «no
        // tenés solicitudes», que son las tres cosas que la tabla de errores del bloque separa.
        Assert.NotNull(identidad);
        Assert.False(identidad.HayEmpleadoSeleccionado);

        // Y pedir el Id de todos modos no devuelve 0 —que se leería como un empleado válido y sería
        // «cualquiera»—: lanza, con su propio tipo de excepción.
        var excepcion = Assert.Throws<SinEmpleadoSeleccionadoException>(() => _ = identidad.Id);
        Assert.Contains("seleccionado", excepcion.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void B4_T8_El_estado_sin_seleccionar_es_distinguible_de_la_identidad_no_configurada()
    {
        // Los dos estados se parecen —ninguno entrega un empleado— y significan cosas opuestas: uno es
        // «mostrale el selector», el otro es «esta aplicación no debería estar corriendo así» (AC-06).
        // Si compartieran tipo de excepción, el Bloque 6 tendría que distinguirlos por el texto del
        // mensaje, que es exactamente cómo se terminan tratando igual.
        var sinSeleccionar = Assert.Throws<SinEmpleadoSeleccionadoException>(
            () => _ = ProveedorDeDesarrolloSinBase().Identidad.Id);

        var noConfigurado = Assert.Throws<InvalidOperationException>(
            () => _ = new EmpleadoActualNoConfigurado().Identidad);

        // Assert.Throws exige el tipo EXACTO, así que la línea de arriba ya falla si el proveedor no
        // configurado lanzara la excepción de «sin seleccionar». Se deja explícito igual: es la
        // propiedad que el bloque promete, no un detalle de la biblioteca de aserciones.
        Assert.Equal(typeof(InvalidOperationException), noConfigurado.GetType());
        Assert.IsAssignableFrom<InvalidOperationException>(sinSeleccionar);
        Assert.NotEqual(noConfigurado.GetType(), sinSeleccionar.GetType());

        // Y el que sí es un problema de configuración nombra la vía correcta; el otro no tiene por qué
        // hablar de RF-01, porque no hay nada mal configurado.
        Assert.Contains("RF-01", noConfigurado.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("RF-01", sinSeleccionar.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void La_capacidad_de_elegir_empleado_se_contesta_sin_lanzar_y_no_es_lo_mismo_que_haber_elegido()
    {
        // Dos propiedades en un test porque son la misma frase leída en las dos direcciones, y separarlas
        // dejaría a cada una pareciendo un detalle.
        //
        // Primera: es una CAPACIDAD, no un estado. El proveedor de desarrollo permite elegir desde el
        // primer instante, cuando todavía no hay nadie elegido: si la capacidad dependiera de la
        // selección, el selector no aparecería nunca y no habría forma de hacer la primera.
        var deDesarrollo = ProveedorDeDesarrolloSinBase();

        Assert.False(deDesarrollo.Identidad.HayEmpleadoSeleccionado);
        Assert.True(deDesarrollo.PermiteElegirEmpleado);

        // Segunda: la identidad NO configurada lo contesta —con un «no»— en vez de lanzar, y es el único
        // miembro que hace eso. Si lanzara, la interfaz no tendría forma de preguntar y volveríamos a
        // averiguarlo por las tres malas: nombrar el tipo concreto, capturar la excepción como control de
        // flujo, o releer el entorno y la clave desde el componente.
        var noConfigurado = new EmpleadoActualNoConfigurado();

        Assert.False(noConfigurado.PermiteElegirEmpleado);

        // El contraste, en el mismo test: los otros dos miembros del mismo objeto sí lanzan (AC-06).
        Assert.Throws<InvalidOperationException>(() => _ = noConfigurado.Identidad);
    }

    [Fact]
    public void Una_identidad_seleccionada_entrega_su_Id()
    {
        // Contracara de B4-T8: sin esto, «no hay empleado seleccionado» lo cumpliría también un tipo
        // que nunca entrega a nadie.
        var identidad = IdentidadDelEmpleado.De(7);

        Assert.True(identidad.HayEmpleadoSeleccionado);
        Assert.Equal(7, identidad.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void No_existe_una_identidad_con_un_Id_no_positivo(int idInvalido)
    {
        // El tipo no tiene ningún valor que signifique «cualquiera»: el único estado sin empleado es
        // el explícito. Un Id 0 construido a mano sería justamente eso, un empleado inexistente que se
        // ve como uno válido.
        Assert.Throws<ArgumentOutOfRangeException>(() => IdentidadDelEmpleado.De(idInvalido));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Un_Id_no_positivo_se_rechaza_sin_consultar_la_base(int idInvalido)
    {
        // El Id llega del navegador (TB-1) y es entrada no confiable. Los no positivos no pueden existir
        // —la clave es identity y arranca en 1—, así que se rechazan sin viajar a la base: la fábrica de
        // esta prueba revienta si alguien la usa, y es eso lo que lo demuestra.
        var nomina = new EmpleadosService(new FabricaQueNadieDebeUsar());

        Assert.False(await nomina.ExisteEnLaNominaAsync(idInvalido, Cancelacion));
    }

    [Fact]
    public async Task Un_Id_no_positivo_no_cambia_la_identidad_del_circuito()
    {
        var proveedor = ProveedorDeDesarrolloSinBase();

        var resultado = await proveedor.SeleccionarAsync(0, Cancelacion);

        Assert.Equal(ResultadoDeSeleccion.RechazadaPorEmpleadoInexistente, resultado);
        Assert.False(proveedor.Identidad.HayEmpleadoSeleccionado);
    }

    private static EmpleadoActualDesarrollo ProveedorDeDesarrolloSinBase() =>
        new(new EmpleadosService(new FabricaQueNadieDebeUsar()));

    /// <summary>
    /// Fábrica de contextos que lanza en cuanto alguien la usa. No es un doble para simular la base:
    /// es la afirmación de que estos casos <b>no</b> la necesitan.
    /// </summary>
    private sealed class FabricaQueNadieDebeUsar : IDbContextFactory<VacacionesDbContext>
    {
        public const string Marca = "ningún caso de esta clase debe abrir un contexto";

        public VacacionesDbContext CreateDbContext() => throw new InvalidOperationException(Marca);
    }
}
