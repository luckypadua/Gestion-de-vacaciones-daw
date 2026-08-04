using AngleSharp.Dom;
using Bunit;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Web.Components.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// B6-T1, B6-T2 y B6-T3: el formulario de alta muestra los días corridos antes de habilitar el envío
/// (AC-01) y muestra los mensajes literales que devuelve el servidor (AC-02, AC-03).
/// </summary>
/// <remarks>
/// <para>
/// <b>La fábrica de contextos revienta si alguien la usa</b> (<see cref="FabricaQueNadieDebeUsar"/>),
/// igual que en los tests de validación del Bloque 5. Acá afirma dos cosas de una vez: que un período
/// rechazado no llega a la base, y —lo que importa para R-10— que el mensaje que se ve <b>viene del
/// servicio</b>. Si el componente hubiera reimplementado la comparación de fechas y decidido por su
/// cuenta, jamás llamaría a <c>CrearAsync</c>; y si se saltease la validación, la fábrica lanzaría y
/// aparecería el mensaje genérico en lugar del literal del PRD. Los dos modos de fallo son visibles.
/// </para>
/// <para>
/// El reloj del servicio está detenido (<see cref="TiempoFijo"/>): «ayer» y «hoy» tienen que significar
/// lo mismo en cualquier día en que se corra la suite.
/// </para>
/// </remarks>
public sealed class FormularioDeAltaTests : ContextoDeComponentes
{
    /// <summary>
    /// Instante en el que se detiene el reloj del <b>servicio</b>. El componente no tiene reloj: no
    /// inyecta <see cref="TimeProvider"/> y ese hecho está fijado por
    /// <see cref="ComponentesSinAccesoADatosTests"/>.
    /// </summary>
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 0, TimeSpan.FromHours(-3));

    private const int EmpleadoActual = 7;

    private readonly TiempoFijo _tiempo = new(_instanteFijo);

    public FormularioDeAltaTests()
    {
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(EmpleadoActual));
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SolicitudesService>();
    }

    [Fact]
    public async Task B6_T1_Muestra_los_dias_corridos_del_periodo_antes_de_habilitar_el_envio()
    {
        // AC-01. El orden de los asertos ES la afirmación: el número aparece ANTES de que el envío
        // quede habilitado, no junto con él ni después.
        var formulario = RenderizarFormulario();

        // Recién abierto: no hay período, así que no hay días corridos que mostrar y no hay nada que
        // enviar.
        Assert.Empty(formulario.FindAll(Selector(FormularioDeAlta.IdDeLosDiasCorridos)));
        Assert.True(BotonDeEnvio(formulario).HasAttribute("disabled"));

        // Con una sola de las dos fechas tampoco hay período: un envío habilitado acá mandaría al
        // servidor una solicitud sin fecha de fin.
        await ElegirFechaAsync(formulario, FormularioDeAlta.EtiquetaFechaDeInicio, new DateOnly(2026, 8, 10));

        Assert.Empty(formulario.FindAll(Selector(FormularioDeAlta.IdDeLosDiasCorridos)));
        Assert.True(BotonDeEnvio(formulario).HasAttribute("disabled"));

        await ElegirFechaAsync(formulario, FormularioDeAlta.EtiquetaFechaDeFin, new DateOnly(2026, 8, 12));

        // Del 10 al 12 de agosto son 3: días CORRIDOS e inclusivos, los que cuenta
        // CalculadorDeDiasCorridos. Un 2 acá sería la interfaz contando por su cuenta.
        Assert.Equal(
            "3 días corridos",
            formulario.Find(Selector(FormularioDeAlta.IdDeLosDiasCorridos)).TextContent.Trim());

        Assert.False(BotonDeEnvio(formulario).HasAttribute("disabled"));
    }

    [Fact]
    public async Task B6_T2_Una_fecha_de_inicio_anterior_a_hoy_muestra_el_mensaje_literal_y_no_crea_nada()
    {
        // AC-02. El literal se compara contra la constante del dominio, y la constante contra el PRD
        // versionado en MensajesLiteralesDelPrdTests: sin ese segundo eslabón, este test aprobaría
        // cualquier texto con tal de que el componente y el test coincidan.
        var formulario = RenderizarFormulario();

        var ayer = _tiempo.Hoy.AddDays(-1);

        await ElegirFechaAsync(formulario, FormularioDeAlta.EtiquetaFechaDeInicio, ayer);
        await ElegirFechaAsync(formulario, FormularioDeAlta.EtiquetaFechaDeFin, ayer.AddDays(2));
        await EnviarAsync(formulario);

        Assert.Equal(
            ErroresDeSolicitud.FechaDeInicioAnteriorAHoy,
            formulario.Find(Selector(FormularioDeAlta.IdDelError)).TextContent.Trim());

        NoSeIntentoPersistir(formulario);
    }

    [Fact]
    public async Task B6_T3_Una_fecha_de_fin_anterior_a_la_de_inicio_muestra_el_mensaje_literal_y_no_crea_nada()
    {
        // AC-03. La fecha de inicio es válida —dentro de diez días—, así que lo único que puede estar
        // rechazando el período es el orden de las dos fechas.
        var formulario = RenderizarFormulario();

        var inicio = _tiempo.Hoy.AddDays(10);

        await ElegirFechaAsync(formulario, FormularioDeAlta.EtiquetaFechaDeInicio, inicio);
        await ElegirFechaAsync(formulario, FormularioDeAlta.EtiquetaFechaDeFin, inicio.AddDays(-1));

        // El período invertido no tiene días corridos que contar —CalculadorDeDiasCorridos lanza si se
        // los piden— así que el número no se muestra. Pero el envío SIGUE habilitado: deshabilitarlo
        // acá movería la validación de AC-03 al cliente, que es exactamente lo que R-10 prohíbe.
        Assert.Empty(formulario.FindAll(Selector(FormularioDeAlta.IdDeLosDiasCorridos)));
        Assert.False(BotonDeEnvio(formulario).HasAttribute("disabled"));

        await EnviarAsync(formulario);

        Assert.Equal(
            ErroresDeSolicitud.FechaDeFinAnteriorALaFechaDeInicio,
            formulario.Find(Selector(FormularioDeAlta.IdDelError)).TextContent.Trim());

        NoSeIntentoPersistir(formulario);
    }

    private static string Selector(string identificador) => $"[data-testid='{identificador}']";

    private IRenderedComponent<FormularioDeAlta> RenderizarFormulario() =>
        Render<FormularioDeAlta>(parametros => parametros.Add(
            formulario => formulario.EmpleadoId, EmpleadoActual));

    private static IElement BotonDeEnvio(IRenderedComponent<FormularioDeAlta> formulario) =>
        formulario.Find(Selector(FormularioDeAlta.IdDelBotonDeEnvio));

    /// <summary>
    /// Elige una fecha por el mismo camino que el usuario: el <c>MudDatePicker</c> notifica el cambio
    /// al componente que lo aloja. No se escribe en el estado del formulario desde el test.
    /// </summary>
    private static Task ElegirFechaAsync(
        IRenderedComponent<FormularioDeAlta> formulario,
        string etiqueta,
        DateOnly fecha)
    {
        var selectorDeFecha = formulario.FindComponents<MudDatePicker>()
            .Single(selector => selector.Instance.Label == etiqueta);

        return formulario.InvokeAsync(
            () => selectorDeFecha.Instance.DateChanged.InvokeAsync(fecha.ToDateTime(TimeOnly.MinValue)));
    }

    private static Task EnviarAsync(IRenderedComponent<FormularioDeAlta> formulario) =>
        BotonDeEnvio(formulario).ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

    /// <summary>
    /// El rechazo ocurrió <b>antes</b> de tocar la base: si el componente hubiera llegado a persistir,
    /// la fábrica habría lanzado y en pantalla estaría el mensaje genérico —y su marca— en vez del
    /// literal del PRD.
    /// </summary>
    private static void NoSeIntentoPersistir(IRenderedComponent<FormularioDeAlta> formulario)
    {
        Assert.DoesNotContain(FormularioDeAlta.MensajeDeFalloInesperado, formulario.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(FabricaQueNadieDebeUsar.Marca, formulario.Markup, StringComparison.Ordinal);
    }
}
