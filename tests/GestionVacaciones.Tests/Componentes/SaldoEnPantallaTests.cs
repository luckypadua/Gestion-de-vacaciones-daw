using Bunit;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Tests.Persistencia;
using GestionVacaciones.Web.Components.Pages;
using GestionVacaciones.Web.Components.Solicitudes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using Xunit;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// Bloque 4 de FEAT-001b: el saldo del empleado en pantalla, antes de <c>FormularioDeAlta</c>
/// (AC-05, AC-06, AC-07).
/// </summary>
/// <remarks>
/// <para>
/// Los casos que no llegan a la base —el fallo del cálculo, la distinción de estados, el escaneo del
/// componente nuevo y la ausencia de saldo sin empleado— viven en <see cref="SaldoEnPantallaTests"/>,
/// sin la instancia SQL Server. Los que necesitan un saldo real —el año en curso, la separación entre
/// usados y disponibles, el cruce de año, y el botón que sigue habilitado sin saldo— viven en
/// <see cref="SaldoEnPantallaConBaseDeDatosTests"/>: no hay proveedor en memoria en la spec, así que un
/// número real solo puede salir de la base real (mismo criterio que el resto de <c>Componentes/</c>).
/// </para>
/// </remarks>
public sealed class SaldoEnPantallaTests : ContextoDeComponentes
{
    private const int EmpleadoActual = 7;

    private readonly TiempoFijo _tiempo =
        new(new DateTimeOffset(2026, 8, 3, 9, 15, 0, TimeSpan.FromHours(-3)));

    [Fact]
    public void Si_el_calculo_falla_no_se_muestra_ninguna_cantidad()
    {
        // AC-07. La fábrica que lanza en cuanto alguien la usa reproduce «el cálculo del saldo falló»
        // sin depender de que haya, o no, una instancia SQL Server a mano.
        RegistrarSaldoQueFalla();

        var componente = Render<SaldoDelEmpleado>();

        Assert.Equal(
            SaldoDelEmpleado.EstadoFallo,
            componente.Find($"[data-testid='{SaldoDelEmpleado.IdDelSaldo}']")
                .GetAttribute(SaldoDelEmpleado.AtributoDelEstado));

        Assert.Equal(
            SaldoDelEmpleado.MensajeDeFallo,
            componente.Find($"[data-testid='{SaldoDelEmpleado.IdDelFallo}']").TextContent.Trim());

        Assert.Empty(componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']"));
        Assert.Empty(componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelOtroAnio}']"));

        // Nunca «0 días disponibles»: ni un dígito en todo el bloque, ni siquiera un cero que se leería
        // como saldo agotado en vez de como cálculo fallido.
        var texto = componente.Find($"[data-testid='{SaldoDelEmpleado.IdDelSaldo}']").TextContent;
        Assert.DoesNotContain(texto, char.IsDigit);
    }

    [Fact]
    public void El_estado_del_saldo_se_distingue_de_los_otros_tres()
    {
        // AC-07. Los cuatro que ya publica MisSolicitudes.razor —EstadoCargando queda incluido de
        // más, no de menos— y los dos que puede tomar SaldoDelEmpleado no pueden compartir ningún
        // valor: si lo hicieran, un test que lea el atributo por afuera no podría distinguirlos.
        string[] estadosDeLaPagina =
        [
            MisSolicitudes.EstadoCargando,
            MisSolicitudes.EstadoSinEmpleado,
            MisSolicitudes.EstadoListado,
            MisSolicitudes.EstadoError,
        ];

        Assert.DoesNotContain(SaldoDelEmpleado.EstadoCalculado, estadosDeLaPagina);
        Assert.DoesNotContain(SaldoDelEmpleado.EstadoFallo, estadosDeLaPagina);
        Assert.NotEqual(SaldoDelEmpleado.EstadoCalculado, SaldoDelEmpleado.EstadoFallo);

        // Y del estado vacío del listado, que vive en otro componente de la misma pantalla.
        Assert.NotEqual(SaldoDelEmpleado.EstadoCalculado, ListadoDeSolicitudes.IdDelEstadoVacio);
        Assert.NotEqual(SaldoDelEmpleado.EstadoFallo, ListadoDeSolicitudes.IdDelEstadoVacio);
    }

    [Fact]
    public void Ningun_razor_nombra_TimeProvider()
    {
        // Extiende ComponentesSinAccesoADatosTests al componente nuevo: ese test ya escanea todo
        // src/GestionVacaciones.Web/**/*.razor, así que esto es la comprobación puntual de que el
        // archivo de este bloque, en concreto, no reintroduce ninguno de los tokens prohibidos.
        var raiz = RaizDelRepositorio.Localizar();
        var archivo = Path.Combine(
            raiz, "src", "GestionVacaciones.Web", "Components", "Solicitudes", "SaldoDelEmpleado.razor");

        Assert.True(File.Exists(archivo), $"No se encontró el componente nuevo en «{archivo}».");

        var contenido = File.ReadAllText(archivo);

        string[] tokensProhibidos =
            ["TimeProvider", "DateTime.Today", "DateTime.Now", "DateTime.UtcNow", "MarkupString"];

        foreach (var token in tokensProhibidos)
        {
            Assert.DoesNotContain(token, contenido, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sin_empleado_seleccionado_el_saldo_no_se_renderiza()
    {
        // La rama que decide si hay empleado ya la resuelve la página: acá se afirma la mitad que le
        // toca al bloque 4, que SaldoDelEmpleado no aparece cuando esa rama no se toma.
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
        Services.AddSingleton<SolicitudesService>();
        Services.AddSingleton<EmpleadosService>();
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.SinSeleccionar());

        var pagina = Render<MisSolicitudes>();

        Assert.Equal(
            MisSolicitudes.EstadoSinEmpleado,
            pagina.Find($"[{MisSolicitudes.AtributoDelEstado}]").GetAttribute(MisSolicitudes.AtributoDelEstado));

        Assert.Empty(pagina.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldo}']"));
    }

    private void RegistrarSaldoQueFalla()
    {
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaQueNadieDebeUsar());
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(EmpleadoActual));
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
    }
}

/// <summary>
/// La mitad de <see cref="SaldoEnPantallaTests"/> que necesita un saldo de verdad: sin proveedor en
/// memoria en la spec, un número que no sea 0 o el tope completo solo puede salir de la instancia SQL
/// Server del entorno.
/// </summary>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class SaldoEnPantallaConBaseDeDatosTests : ContextoDeComponentes
{
    private static readonly DateTimeOffset _instanteFijo =
        new(2026, 8, 3, 9, 15, 0, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;
    private readonly TiempoFijo _tiempo = new(_instanteFijo);

    public SaldoEnPantallaConBaseDeDatosTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_saldo_del_anio_en_curso_se_muestra_al_entrar()
    {
        // AC-05. Sin ninguna solicitud sembrada, el saldo del año en curso es el tope completo: 14
        // disponibles, 0 usados. Alcanza para afirmar que el bloque aparece apenas se entra.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        RegistrarSaldoReal(empleado.Id);

        var componente = Render<SaldoDelEmpleado>();

        componente.WaitForState(
            () => componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']").Count > 0,
            TimeSpan.FromSeconds(10));

        var bloque = componente.Find($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']");
        Assert.Equal("2026", bloque.GetAttribute(SaldoDelEmpleado.AtributoDelAnio));
    }

    [Fact]
    public async Task Se_muestran_utilizados_y_disponibles_por_separado()
    {
        // AC-05. Tres días reservados (Pendiente, que cuenta igual que Aprobada) dejan 11 disponibles:
        // dos números distintos, cada uno con su propio marcador.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 3));
        RegistrarSaldoReal(empleado.Id);

        var componente = Render<SaldoDelEmpleado>();

        componente.WaitForState(
            () => componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']").Count > 0,
            TimeSpan.FromSeconds(10));

        var utilizados = componente.Find(
            $"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}'] [data-testid='{SaldoDelEmpleado.IdDeLosDiasUtilizados}']");
        var disponibles = componente.Find(
            $"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}'] [data-testid='{SaldoDelEmpleado.IdDeLosDiasDisponibles}']");

        Assert.Equal("3 días utilizados o reservados", utilizados.TextContent.Trim());
        Assert.Equal("11 días disponibles", disponibles.TextContent.Trim());
    }

    [Fact]
    public async Task Un_periodo_que_cruza_el_anio_muestra_los_dos_saldos()
    {
        // AC-06, con el ejemplo numérico de AC-01: 28-dic-2026 a 5-ene-2027 cruza a 2027.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        RegistrarSaldoReal(empleado.Id);

        var componente = Render<SaldoDelEmpleado>();

        componente.WaitForState(
            () => componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']").Count > 0,
            TimeSpan.FromSeconds(10));

        componente.Render(parametros => parametros
            .Add(saldo => saldo.FechaInicio, new DateOnly(2026, 12, 28))
            .Add(saldo => saldo.FechaFin, new DateOnly(2027, 1, 5)));

        componente.WaitForState(
            () => componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelOtroAnio}']").Count > 0,
            TimeSpan.FromSeconds(10));

        var anioEnCurso = componente.Find($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']");
        var otroAnio = componente.Find($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelOtroAnio}']");

        Assert.Equal("2026", anioEnCurso.GetAttribute(SaldoDelEmpleado.AtributoDelAnio));
        Assert.Equal("2027", otroAnio.GetAttribute(SaldoDelEmpleado.AtributoDelAnio));
    }

    [Fact]
    public async Task Un_periodo_dentro_del_anio_muestra_un_solo_saldo()
    {
        // La contracara de AC-06: un período que no cruza el 31 de diciembre no agrega ningún segundo
        // bloque.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        RegistrarSaldoReal(empleado.Id);

        var componente = Render<SaldoDelEmpleado>();

        componente.WaitForState(
            () => componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']").Count > 0,
            TimeSpan.FromSeconds(10));

        componente.Render(parametros => parametros
            .Add(saldo => saldo.FechaInicio, new DateOnly(2026, 8, 10))
            .Add(saldo => saldo.FechaFin, new DateOnly(2026, 8, 12)));

        Assert.Empty(componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelOtroAnio}']"));
        Assert.Single(componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']"));
    }

    [Fact]
    public async Task Un_periodo_invertido_no_pide_el_saldo_del_segundo_anio()
    {
        // Camino triste: un período con la fecha de fin antes que la de inicio no tiene años que
        // AniosAbarcados pueda calcular sin lanzar. El componente lo comprueba antes de llamarla, así
        // que ni pide el segundo saldo ni cae en el estado de fallo; el año en curso se sigue viendo.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        RegistrarSaldoReal(empleado.Id);

        var componente = Render<SaldoDelEmpleado>();

        componente.WaitForState(
            () => componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']").Count > 0,
            TimeSpan.FromSeconds(10));

        componente.Render(parametros => parametros
            .Add(saldo => saldo.FechaInicio, new DateOnly(2026, 8, 20))
            .Add(saldo => saldo.FechaFin, new DateOnly(2026, 8, 10)));

        Assert.Empty(componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelOtroAnio}']"));
        Assert.Single(componente.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}']"));

        Assert.Equal(
            SaldoDelEmpleado.EstadoCalculado,
            componente.Find($"[data-testid='{SaldoDelEmpleado.IdDelSaldo}']")
                .GetAttribute(SaldoDelEmpleado.AtributoDelEstado));
    }

    [Fact]
    public async Task Si_el_calculo_falla_el_listado_y_el_alta_siguen_usables()
    {
        // AC-07. SaldoService recibe una fábrica que lanza; SolicitudesService recibe la real: el
        // listado y el formulario no dependen de que el saldo se haya podido calcular.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var identidad = IdentidadDePrueba.De(empleado.Id);
        var permisos = new PermisosService();
        var saldoQueFalla = new SaldoService(new FabricaQueNadieDebeUsar(), identidad, permisos, _tiempo);

        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaDeLaBaseDeTest(_baseDeDatos));
        Services.AddSingleton<IEmpleadoActualProvider>(identidad);
        Services.AddSingleton<EmpleadosService>();
        Services.AddSingleton(saldoQueFalla);
        Services.AddSingleton(new SolicitudesService(
            new FabricaDeLaBaseDeTest(_baseDeDatos),
            identidad,
            permisos,
            _tiempo,
            saldoQueFalla,
            NullLogger<SolicitudesService>.Instance));

        var pagina = Render<MisSolicitudes>();

        pagina.WaitForState(
            () => pagina.FindAll($"[data-testid='{SaldoDelEmpleado.IdDelFallo}']").Count > 0,
            TimeSpan.FromSeconds(10));

        Assert.Equal(
            MisSolicitudes.EstadoListado,
            pagina.Find($"[{MisSolicitudes.AtributoDelEstado}]").GetAttribute(MisSolicitudes.AtributoDelEstado));

        Assert.Single(pagina.FindAll($"[data-testid='{FormularioDeAlta.IdDelFormulario}']"));
        Assert.Single(pagina.FindAll($"[data-testid='{ListadoDeSolicitudes.IdDelEstadoVacio}']"));

        // Ni rastro del error genérico de la página: el fallo es del saldo, no del listado.
        Assert.Empty(pagina.FindAll($"[data-testid='{MisSolicitudes.IdDelError}']"));
    }

    [Fact]
    public async Task El_boton_de_enviar_sigue_habilitado_sin_saldo_suficiente()
    {
        // Camino triste, mitigación R-10: aunque el saldo mostrado sea 0, la interfaz no decide la
        // regla del tope. El botón depende únicamente de que estén las dos fechas.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // 14 días Pendiente agotan el tope del año en curso (AC-04: Pendiente reserva igual que
        // Aprobada).
        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 14));

        var identidad = IdentidadDePrueba.De(empleado.Id);
        var permisos = new PermisosService();
        var fabrica = new FabricaDeLaBaseDeTest(_baseDeDatos);
        var saldo = new SaldoService(fabrica, identidad, permisos, _tiempo);

        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(fabrica);
        Services.AddSingleton<IEmpleadoActualProvider>(identidad);
        Services.AddSingleton<EmpleadosService>();
        Services.AddSingleton(saldo);
        Services.AddSingleton(new SolicitudesService(
            fabrica, identidad, permisos, _tiempo, saldo, NullLogger<SolicitudesService>.Instance));

        var pagina = Render<MisSolicitudes>();

        pagina.WaitForState(
            () => pagina.FindAll($"[data-testid='{SaldoDelEmpleado.IdDeLosDiasDisponibles}']").Count > 0,
            TimeSpan.FromSeconds(10));

        var disponibles = pagina.Find(
            $"[data-testid='{SaldoDelEmpleado.IdDelSaldoDelAnioEnCurso}'] [data-testid='{SaldoDelEmpleado.IdDeLosDiasDisponibles}']");
        Assert.Equal("0 días disponibles", disponibles.TextContent.Trim());

        var inicio = _tiempo.Hoy.AddDays(10);
        await ElegirFechaAsync(pagina, FormularioDeAlta.EtiquetaFechaDeInicio, inicio);
        await ElegirFechaAsync(pagina, FormularioDeAlta.EtiquetaFechaDeFin, inicio.AddDays(2));

        var boton = pagina.Find($"[data-testid='{FormularioDeAlta.IdDelBotonDeEnvio}']");
        Assert.False(boton.HasAttribute("disabled"));
    }

    private void RegistrarSaldoReal(int empleadoId)
    {
        Services.AddSingleton<TimeProvider>(_tiempo);
        Services.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaDeLaBaseDeTest(_baseDeDatos));
        Services.AddSingleton<IEmpleadoActualProvider>(IdentidadDePrueba.De(empleadoId));
        Services.AddSingleton<PermisosService>();
        Services.AddSingleton<SaldoService>();
    }

    private async Task SembrarAsync(int empleadoId, DateOnly inicio, DateOnly fin)
    {
        await using VacacionesDbContext contexto = _baseDeDatos.CrearContexto();

        contexto.Solicitudes.Add(new Solicitud
        {
            EmpleadoId = empleadoId,
            FechaInicio = inicio,
            FechaFin = fin,
            DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
            Estado = EstadoSolicitud.Pendiente,
            FechaCreacion = _instanteFijo,
        });

        await contexto.SaveChangesAsync(Cancelacion);
    }

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
}
