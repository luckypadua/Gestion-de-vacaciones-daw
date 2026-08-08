using Bunit;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Tests.Persistencia;
using GestionVacaciones.Web.Components.Pages;
using GestionVacaciones.Web.Components.Solicitudes;
using GestionVacaciones.Web.Identidad;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// B6-T5: al cambiar el empleado en el selector, el listado pasa a mostrar las solicitudes del nuevo
/// (<b>AC-07</b>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué necesita la instancia SQL Server 2022 del entorno.</b> AC-07 es una afirmación sobre la
/// cadena entera —selector → <c>IEmpleadoActualProvider</c> → <c>SolicitudesService</c> → listado— y
/// sus dos extremos son datos: la nómina que alimenta el desplegable y las solicitudes que se
/// renderizan. Con dobles en el medio, lo único que quedaría afirmado es que un componente llama al
/// otro, que es justo lo que AC-07 <i>no</i> pregunta. El proveedor que se registra acá es el
/// <b>productivo</b> de desarrollo (<see cref="EmpleadoActualDesarrollo"/>), no un doble: es el que
/// valida contra la nómina el identificador que llega del navegador.
/// </para>
/// <para>
/// Si no hay cadena de test configurada, se saltea con motivo explícito. Cada caso crea sus propios
/// empleados y los borra al terminar, con sus solicitudes (regla #0).
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class SeleccionDeEmpleadoEnLaInterfazTests : ContextoDeComponentes
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 0, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;
    private readonly TiempoFijo _tiempo = new(_instanteFijo);

    public SeleccionDeEmpleadoEnLaInterfazTests(BaseDeDatosDeTest baseDeDatos)
    {
        _baseDeDatos = baseDeDatos;

        ComposicionDeLaPantalla.RegistrarContraLaBaseDeTest(Services, baseDeDatos, _tiempo);
    }

    /// <summary>
    /// Calificado: <c>Bunit</c> y <c>Xunit</c> traen los dos un <c>TestContext</c>, y con los dos
    /// espacios de nombres abiertos el nombre corto es ambiguo.
    /// </summary>
    private static CancellationToken Cancelacion => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task B6_T5_Al_cambiar_el_empleado_el_listado_pasa_a_mostrar_las_del_nuevo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var primero = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var segundo = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // Períodos de duración distinta: es lo que hace que las dos pantallas no puedan confundirse.
        await SembrarAsync(
            (primero.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)),
            (segundo.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9)),
            (segundo.Id, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2)));

        var pagina = Render<MisSolicitudes>();

        // Antes de elegir a nadie: el selector, no un listado.
        Assert.Equal(MisSolicitudes.EstadoSinEmpleado, EstadoDeLaPantalla(pagina));

        await ElegirEmpleadoAsync(pagina, primero.Id);

        Assert.Equal(MisSolicitudes.EstadoListado, EstadoDeLaPantalla(pagina));
        Assert.Equal(["3"], DiasCorridosRenderizados(pagina));

        await ElegirEmpleadoAsync(pagina, segundo.Id);

        // Las del segundo, y NINGUNA del primero: si el sujeto del listado no hubiera cambiado, acá
        // seguiría el período de 3 días.
        Assert.Equal(MisSolicitudes.EstadoListado, EstadoDeLaPantalla(pagina));
        Assert.Equal(["5", "1"], DiasCorridosRenderizados(pagina));
    }

    [Fact]
    public async Task Elegir_un_empleado_inexistente_no_le_cambia_el_listado_a_nadie()
    {
        // La entrada del selector llega del navegador (TB-1) y no se le cree. El Bloque 4 ya fija que
        // el proveedor rechaza el identificador y conserva el anterior; lo que se afirma acá es que la
        // interfaz no muestra las solicitudes de nadie más por ese camino.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await SembrarAsync((empleado.Id, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)));

        var pagina = Render<MisSolicitudes>();

        await ElegirEmpleadoAsync(pagina, empleado.Id);
        Assert.Equal(["3"], DiasCorridosRenderizados(pagina));

        // Ningún empleado tiene identificador negativo: la clave es identity y arranca en 1.
        await ElegirEmpleadoAsync(pagina, -1);

        Assert.Equal(MisSolicitudes.EstadoListado, EstadoDeLaPantalla(pagina));
        Assert.Equal(["3"], DiasCorridosRenderizados(pagina));
    }

    [Fact]
    public async Task Al_cambiar_de_empleado_el_formulario_no_conserva_nada_del_anterior()
    {
        // El formulario tiene estado propio —el período a medio elegir y el mensaje con que el servidor
        // rechazó el último intento— y ese estado es DEL EMPLEADO que lo escribió. Blazor, por su
        // cuenta, reutiliza la instancia del componente y se limita a actualizarle el parámetro: el
        // rechazo del anterior quedaría en pantalla bajo la identidad nueva, atribuyéndole a una persona
        // un intento que hizo otra. Lo que lo impide es el @key de la página.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var primero = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var segundo = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var pagina = Render<MisSolicitudes>();

        await ElegirEmpleadoAsync(pagina, primero.Id);
        Assert.Equal(MisSolicitudes.EstadoListado, EstadoDeLaPantalla(pagina));

        // Un período con la fecha de inicio en el pasado: se ven los días corridos y, al enviarlo, el
        // servidor lo rechaza con el literal de AC-02. Dos rastros observables, uno de cada clase.
        var ayer = _tiempo.Hoy.AddDays(-1);
        await ElegirFechaAsync(pagina, FormularioDeAlta.EtiquetaFechaDeInicio, ayer);
        await ElegirFechaAsync(pagina, FormularioDeAlta.EtiquetaFechaDeFin, ayer.AddDays(2));

        Assert.Single(pagina.FindAll($"[data-testid='{FormularioDeAlta.IdDeLosDiasCorridos}']"));

        await pagina.Find($"[data-testid='{FormularioDeAlta.IdDelBotonDeEnvio}']").ClickAsync(new MouseEventArgs());

        Assert.Equal(
            ErroresDeSolicitud.FechaDeInicioAnteriorAHoy,
            pagina.Find($"[data-testid='{FormularioDeAlta.IdDelError}']").TextContent.Trim());

        await ElegirEmpleadoAsync(pagina, segundo.Id);

        // El formulario sigue en pantalla —hay empleado, se puede pedir— pero es uno nuevo: ni el
        // período del anterior ni el rechazo que recibió.
        Assert.Equal(MisSolicitudes.EstadoListado, EstadoDeLaPantalla(pagina));
        Assert.Single(pagina.FindAll($"[data-testid='{FormularioDeAlta.IdDelFormulario}']"));

        Assert.Empty(pagina.FindAll($"[data-testid='{FormularioDeAlta.IdDelError}']"));
        Assert.Empty(pagina.FindAll($"[data-testid='{FormularioDeAlta.IdDeLosDiasCorridos}']"));
    }

    /// <summary>
    /// Elige una fecha por el mismo camino que el usuario: el <c>MudDatePicker</c> notifica el cambio
    /// al formulario que lo aloja.
    /// </summary>
    private static Task ElegirFechaAsync(
        IRenderedComponent<MisSolicitudes> pagina,
        string etiqueta,
        DateOnly fecha)
    {
        var selectorDeFecha = pagina.FindComponents<MudDatePicker>()
            .Single(selector => selector.Instance.Label == etiqueta);

        return pagina.InvokeAsync(
            () => selectorDeFecha.Instance.DateChanged.InvokeAsync(fecha.ToDateTime(TimeOnly.MinValue)));
    }

    private static string? EstadoDeLaPantalla(IRenderedComponent<MisSolicitudes> pagina) =>
        pagina.Find($"[{MisSolicitudes.AtributoDelEstado}]").GetAttribute(MisSolicitudes.AtributoDelEstado);

    private static IEnumerable<string> DiasCorridosRenderizados(IRenderedComponent<MisSolicitudes> pagina) =>
        pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDeLosDiasCorridos}']")
            .Select(celda => celda.TextContent.Trim());

    /// <summary>
    /// Elige por el mismo camino que el usuario: el desplegable del selector notifica el cambio. No se
    /// llama a <c>SeleccionarAsync</c> desde el test, que saltearía justo el componente bajo prueba.
    /// </summary>
    private static Task ElegirEmpleadoAsync(IRenderedComponent<MisSolicitudes> pagina, int empleadoId)
    {
        var desplegable = pagina.FindComponent<MudSelect<int?>>();

        return pagina.InvokeAsync(() => desplegable.Instance.ValueChanged.InvokeAsync(empleadoId));
    }

    /// <summary>
    /// Siembra por el contexto y no por el servicio de alta: los períodos son del futuro respecto del
    /// reloj detenido, pero lo que este test necesita es que existan, no ejercitar el alta —eso es
    /// B5-T5—.
    /// </summary>
    private async Task SembrarAsync(params (int EmpleadoId, DateOnly Inicio, DateOnly Fin)[] solicitudes)
    {
        await using VacacionesDbContext contexto = _baseDeDatos.CrearContexto();

        var creacion = _instanteFijo;
        foreach (var (empleadoId, inicio, fin) in solicitudes)
        {
            contexto.Solicitudes.Add(new Solicitud
            {
                EmpleadoId = empleadoId,
                FechaInicio = inicio,
                FechaFin = fin,
                DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
                Estado = EstadoSolicitud.Pendiente,

                // Descendente por fecha de creación: la primera sembrada es la más nueva, así que el
                // listado la muestra primera.
                FechaCreacion = creacion,
            });

            creacion = creacion.AddMinutes(-1);
        }

        await contexto.SaveChangesAsync(Cancelacion);
    }
}
