using Bunit;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Tests.Persistencia;
using GestionVacaciones.Web.Components.Identidad;
using GestionVacaciones.Web.Components.Pages;
using GestionVacaciones.Web.Components.Solicitudes;
using MudBlazor;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// El tercero de los tres estados confundibles, visto <b>desde la página</b>: hay empleado resuelto,
/// se consultaron sus solicitudes, y no tiene ninguna.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué hace falta si ya existe B6-T7.</b> Aquel afirma sobre <c>ListadoDeSolicitudes</c>
/// aislado: le entrega una lista vacía y comprueba que la renderiza como estado vacío. Lo que no mira
/// nadie es el <b>ruteo</b>: que la página, ante una consulta que devolvió cero filas, entre en la
/// rama del listado y no en la del error. Es el único de los tres estados que se puede colapsar en
/// silencio —B6-T6 seguiría verde porque su caso es el servicio lanzando, B6-T7 porque es otro
/// componente, y B6-T9 porque ahí no hay empleado—, y colapsarlo le diría a quien recién entra que
/// algo falló cuando lo único que pasa es que todavía no pidió vacaciones.
/// </para>
/// <para>
/// <b>Por qué necesita la instancia SQL Server del entorno.</b> «Cero filas» tiene que venir de una
/// consulta que de verdad ocurrió: es la diferencia entre este caso y el de B6-T9, donde no se
/// consulta nada. <c>SolicitudesService</c> es sellado y la única forma de que devuelva una lista
/// vacía es que la base conteste eso, y no hay proveedor en memoria disponible sin sumar un paquete,
/// que la spec no autoriza. Sin cadena configurada se saltea con motivo explícito.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class ListadoVacioEnLaPaginaTests : ContextoDeComponentes
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 0, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public ListadoVacioEnLaPaginaTests(BaseDeDatosDeTest baseDeDatos)
    {
        _baseDeDatos = baseDeDatos;

        ComposicionDeLaPantalla.RegistrarContraLaBaseDeTest(Services, baseDeDatos, new TiempoFijo(_instanteFijo));
    }

    [Fact]
    public async Task Un_empleado_sin_solicitudes_ve_el_estado_vacio_y_no_un_error()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        // Recién creado: existe en la nómina y no tiene ni una solicitud. No se siembra nada, y eso ES
        // el caso bajo prueba.
        await using var reciencito = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var pagina = Render<MisSolicitudes>();

        var desplegable = pagina.FindComponent<MudSelect<int?>>();
        await pagina.InvokeAsync(() => desplegable.Instance.ValueChanged.InvokeAsync(reciencito.Id));

        // La consulta ocurrió y salió bien: la página está en la rama del listado, no en la del error.
        Assert.Equal(MisSolicitudes.EstadoListado, EstadoDeLaPantalla(pagina));

        // Y lo que se ve es el estado vacío explícito, con su marcador propio.
        Assert.Single(pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDelEstadoVacio}']"));
        Assert.Empty(pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDeLaFila}']"));

        // Ni rastro del error de la página: «no tenés solicitudes» es una respuesta, no un problema.
        Assert.Empty(pagina.FindAll($"[data-testid='{MisSolicitudes.IdDelError}']"));
        Assert.DoesNotContain(MisSolicitudes.MensajeDeFalloInesperado, pagina.Markup, StringComparison.Ordinal);

        // Y tampoco el estado «sin empleado»: hay empleado, y por eso el formulario se ofrece. Sin este
        // aserto, rutear el vacío hacia el selector pasaría desapercibido.
        Assert.Single(pagina.FindAll($"[data-testid='{FormularioDeAlta.IdDelFormulario}']"));
        Assert.Single(pagina.FindAll($"[data-testid='{SelectorDeEmpleado.IdDelSelector}']"));
    }

    private static string? EstadoDeLaPantalla(IRenderedComponent<MisSolicitudes> pagina) =>
        pagina.Find($"[{MisSolicitudes.AtributoDelEstado}]").GetAttribute(MisSolicitudes.AtributoDelEstado);
}
