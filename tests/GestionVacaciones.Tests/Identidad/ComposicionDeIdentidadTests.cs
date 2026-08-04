using System.Text.Json;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Web.Identidad;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GestionVacaciones.Tests.Identidad;

/// <summary>
/// B4-T1, B4-T2, B4-T4, B4-T5 y B4-T7: la composición de la identidad del empleado actual, que es la
/// única barrera entre el selector sin credencial y un entorno que no sea de desarrollo (R-01,
/// CRITICAL).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué no necesitan la instancia SQL Server del entorno.</b> Ninguno abre conexión: se afirma
/// sobre qué registra el contenedor y sobre qué hace el proveedor resuelto antes de que nadie
/// seleccione a nadie. La cadena de conexión que usan apunta a un catálogo ficticio precisamente para
/// que no pueda haber una consulta escondida pasando en verde.
/// </para>
/// <para>
/// Van en la colección del entorno de proceso porque fijan <c>ASPNETCORE_ENVIRONMENT</c> y la clave
/// <c>Vacaciones__PermitirIdentidadDeDesarrollo</c>, que son estado global del proceso.
/// </para>
/// <para>
/// <b>Por qué acá y no en <c>Andamiaje/</c>.</b> Hay precedente en las dos carpetas, y el que manda es
/// el del bloque anterior: el test de composición de NFR-05 vive en <c>Persistencia/</c> y el de la
/// invocación de la semilla también, es decir junto a la <i>preocupación</i> que verifican y no junto
/// al host que las compone. NFR-06 es una propiedad de la identidad, así que su test vive en
/// <c>Identidad/</c>.
/// </para>
/// </remarks>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class ComposicionDeIdentidadTests
{
    /// <summary>
    /// Fuerza en el contenedor el sustituto de identidad de desarrollo, con la nómina que necesita para
    /// construirse. Reproduce lo que en producción provocaría un error de composición, o un host futuro
    /// que copiara el registro sin copiar su condición.
    /// </summary>
    /// <remarks>
    /// El mismo forzado se usa en los dos entornos —B4-T4 y su contracara— para que los dos tests
    /// difieran <b>únicamente</b> en el entorno, que es la variable bajo prueba. La nómina va incluida
    /// porque en <c>Development</c> el contenedor se valida al construirse y, sin ella, el fallo sería
    /// «no se puede construir el proveedor» en vez de lo que se quiere observar.
    /// </remarks>
    private static readonly Action<IServiceCollection> _forzarElProveedorDeDesarrollo = servicios =>
    {
        servicios.AddScoped<EmpleadosService>();
        servicios.AddScoped<IEmpleadoActualProvider, EmpleadoActualDesarrollo>();
    };

    /// <summary>
    /// Los tramos de <see cref="VerificacionDeIdentidad.ClaveDeConfiguracion"/>, que es como el JSON los
    /// anida. Se derivan de la constante en vez de repetirse a mano: dos grafías de la misma clave se
    /// separan la primera vez que alguien renombra una de las dos.
    /// </summary>
    private static string[] TramosDeLaClave => VerificacionDeIdentidad.ClaveDeConfiguracion.Split(':');

    [Fact]
    public async Task El_host_de_prueba_deja_ausente_la_clave_de_identidad_de_desarrollo()
    {
        // Guardarraíl del propio andamiaje de estos tests. El directorio de salida contiene una copia
        // del appsettings.Development.json del proyecto Web, que declara la clave en true: sin mover la
        // raíz de contenido, el caso «la clave está ausente» de B4-T2 pasaría por el motivo equivocado
        // —la clave estaría presente y en true, y aun así resolvería el proveedor que lanza, con lo que
        // la doble condición quedaría sin verificar—.
        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeDesarrollo, clave: null);

        var configuracion = aplicacion.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(HostConIdentidad.RaizSinConfiguracion(), aplicacion.Environment.ContentRootPath);
        Assert.Null(configuracion[VerificacionDeIdentidad.ClaveDeConfiguracion]);
        Assert.True(aplicacion.Environment.IsDevelopment());
    }

    [Fact]
    public async Task B4_T1_Fuera_de_desarrollo_el_contenedor_resuelve_el_proveedor_que_lanza()
    {
        // AC-06. La clave se pone en TRUE a propósito: si estuviera ausente, este test lo aprobaría
        // igual un código que solo mirara la clave y se olvidara del entorno, que es exactamente el
        // agujero de R-01. Con la clave activa, lo único que puede estar rechazando la identidad de
        // desarrollo es la condición del entorno.
        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeProduccion, clave: "true");

        using var ambito = aplicacion.Services.CreateScope();
        var proveedor = ambito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();

        Assert.IsType<EmpleadoActualNoConfigurado>(proveedor);

        // «Resolverlo del contenedor» no basta: lo que AC-06 exige es que USARLO lance, en vez de
        // devolver un empleado por defecto. Se ejercitan los dos miembros de la interfaz: dejar uno sin
        // cubrir deja abierta la vía por la que se obtiene una identidad fuera de desarrollo.
        var alPedirLaIdentidad = Assert.Throws<InvalidOperationException>(() => _ = proveedor.Identidad);
        var alSeleccionar = await Assert.ThrowsAsync<InvalidOperationException>(
            () => proveedor.SeleccionarAsync(1, TestContext.Current.CancellationToken));

        // El mensaje nombra RF-01 —la autenticación OAuth del PRD-001— como la vía correcta: quien se
        // encuentre con esta excepción tiene que saber qué falta, no solo que algo falló.
        Assert.Contains("RF-01", alPedirLaIdentidad.Message, StringComparison.Ordinal);
        Assert.Contains("RF-01", alSeleccionar.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task B4_T2_En_desarrollo_sin_la_clave_tambien_resuelve_el_proveedor_que_lanza()
    {
        // La otra mitad de la doble condición de R-01, y la que fija el valor por defecto seguro: la
        // clave ausente no habilita nada. Es el caso que ocurre en cualquier máquina que no la haya
        // declarado, así que el modo de fallo por omisión es el seguro.
        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeDesarrollo, clave: null);

        using var ambito = aplicacion.Services.CreateScope();

        Assert.IsType<EmpleadoActualNoConfigurado>(
            ambito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());

        // Mitigación de R-04: la nómina completa —nombres de toda la plantilla— queda ligada al MISMO
        // guardarraíl, no a uno propio. Sin la clave no existe el servicio que la lee, así que no hay
        // desde dónde volcarla.
        Assert.False(
            aplicacion.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(EmpleadosService)),
            "El servicio de nómina quedó registrado sin la clave de identidad de desarrollo: R-04 exige que dependa de la misma doble condición.");
    }

    [Theory]
    [InlineData("false")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("sí")]
    [InlineData("TRUE ")]
    public async Task Solo_el_valor_true_habilita_la_identidad_de_desarrollo(string clave)
    {
        // Complemento de B4-T2. La clave se interpreta por igualdad con «true», sin distinguir
        // mayúsculas, y cualquier otra cosa —incluido un valor que no se puede interpretar como
        // booleano— cae del lado seguro en vez de reventar el arranque o, peor, aceptarse. «1» y «yes»
        // están acá porque son las dos abreviaturas que uno escribe por costumbre y que, si se
        // aceptaran, ampliarían en silencio la superficie de R-01.
        //
        // «TRUE » con espacio al final también se rechaza: recortarlo sería adivinar la intención de
        // una clave mal escrita, y la dirección en la que conviene equivocarse es la de no habilitar.
        await using var aplicacion = HostConIdentidad.Construir(HostConIdentidad.EntornoDeDesarrollo, clave);

        using var ambito = aplicacion.Services.CreateScope();

        Assert.IsType<EmpleadoActualNoConfigurado>(
            ambito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    public async Task La_clave_habilita_en_las_capitalizaciones_que_produce_la_configuracion_real(string clave)
    {
        // La contracara imprescindible del test de los valores que NO habilitan: sin esta, «solo el
        // valor true habilita» lo cumpliría también una comparación sensible a mayúsculas, y esa
        // comparación rompe el entorno de desarrollo de TODO el mundo sin poner nada en rojo.
        //
        // «True» con T mayúscula no es una capitalización hipotética: es EXACTAMENTE la cadena que el
        // proveedor de configuración entrega al leer un booleano JSON de un appsettings. «TRUE» va
        // incluida porque es la grafía habitual de una variable de entorno, que desde la corrección de
        // R-01 es además la única fuente de esta clave en desarrollo (launchSettings.json).
        HostConIdentidad.SaltearSiElArtefactoNoEsDeDepuracion();

        await using var aplicacion = HostConIdentidad.Construir(HostConIdentidad.EntornoDeDesarrollo, clave);

        using var ambito = aplicacion.Services.CreateScope();

        Assert.IsType<EmpleadoActualDesarrollo>(
            ambito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());
    }

    [Fact]
    public async Task El_espacio_al_final_es_la_unica_diferencia_entre_la_clave_que_habilita_y_la_que_no()
    {
        // Los dos conjuntos —el que habilita y el que no— se tocan justo acá, y la diferencia entre sus
        // dos miembros más parecidos es un espacio. Afirmarla en un solo test la vuelve visible: leídos
        // por separado, «TRUE habilita» y «"TRUE " no habilita» parecen contradecirse y el próximo
        // lector va a querer «arreglar» uno de los dos.
        //
        // Que la capitalización se ignore y el espacio no es deliberado: la capitalización la produce el
        // propio proveedor de configuración, mientras que recortar el espacio sería adivinar la
        // intención de una clave mal escrita. La dirección en la que conviene equivocarse es la de no
        // habilitar.
        HostConIdentidad.SaltearSiElArtefactoNoEsDeDepuracion();

        await using var conMayusculas = HostConIdentidad.Construir(HostConIdentidad.EntornoDeDesarrollo, "TRUE");
        await using var conEspacioAlFinal = HostConIdentidad.Construir(HostConIdentidad.EntornoDeDesarrollo, "TRUE ");

        using var ambitoConMayusculas = conMayusculas.Services.CreateScope();
        using var ambitoConEspacio = conEspacioAlFinal.Services.CreateScope();

        Assert.IsType<EmpleadoActualDesarrollo>(
            ambitoConMayusculas.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());
        Assert.IsType<EmpleadoActualNoConfigurado>(
            ambitoConEspacio.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());
    }

    [Fact]
    public async Task La_configuracion_versionada_por_si_sola_NO_habilita_la_identidad_de_desarrollo()
    {
        // ESTE TEST ESTABA AL REVÉS, y afirmaba lo que resultó ser la vulnerabilidad. Decía «el
        // appsettings versionado habilita la identidad de desarrollo de punta a punta», y era verdad:
        // el archivo declaraba la clave y se copiaba al artefacto publicado, así que en un host con
        // ASPNETCORE_ENVIRONMENT=Development mal puesta la segunda condición de R-01 llegaba sola. Las
        // dos condiciones que el modelo de amenazas describe como independientes eran, en los hechos,
        // una sola variable de entorno.
        //
        // Ahora la clave vive en launchSettings.json —versionado, pero NO publicado— y lo que este test
        // afirma es lo contrario: con la configuración versionada como única fuente, el entorno de
        // desarrollo por sí solo no alcanza. Es el escenario del artefacto desplegado.
        var archivoVersionado = Path.Combine(
            HostConIdentidad.RaizDelProyectoWeb(), "appsettings.Development.json");

        using var documento = JsonDocument.Parse(
            await File.ReadAllTextAsync(archivoVersionado, TestContext.Current.CancellationToken));

        // El archivo ya no declara la clave. Lo fija también ClaveFueraDelArtefactoTests, desde el otro
        // lado —la forma del artefacto—; acá se lo comprueba como premisa de lo que sigue, para que
        // quede claro por qué el proveedor resuelto es el que niega.
        Assert.False(
            documento.RootElement.TryGetProperty(TramosDeLaClave[0], out var seccion)
                && seccion.TryGetProperty(TramosDeLaClave[1], out _));

        // La clave se deja AUSENTE del entorno a propósito: la única fuente posible es la configuración
        // versionada, que es exactamente lo que viaja dentro del artefacto publicado.
        //
        // Se apunta la raíz de contenido al proyecto Web DEL REPOSITORIO, no al directorio de salida:
        // ahí hay copias del build, y una copia no es evidencia de lo que se commiteó.
        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeDesarrollo,
            clave: null,
            raizDeContenido: HostConIdentidad.RaizDelProyectoWeb());

        var configuracion = aplicacion.Services.GetRequiredService<IConfiguration>();

        Assert.Null(configuracion[VerificacionDeIdentidad.ClaveDeConfiguracion]);

        using var ambito = aplicacion.Services.CreateScope();

        // Entorno de desarrollo y, aun así, el proveedor que niega: la segunda condición no está.
        Assert.IsType<EmpleadoActualNoConfigurado>(
            ambito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());
    }

    [Fact]
    public void B4_T4_Con_el_proveedor_de_desarrollo_forzado_fuera_de_desarrollo_el_arranque_falla()
    {
        // R-01, mitigación 2: fallo al arrancar y no al primer uso. Una aplicación que no levanta es un
        // incidente visible; una que levanta y suplanta identidades, no.
        //
        // El registro se FUERZA por el punto de extensión de la composición, que es la forma de
        // reproducir lo que en producción provocaría un error de composición o un host futuro que
        // copiara el registro sin copiar la condición. Que el forzado no alcance para saltear el
        // guardarraíl es justamente lo que este test afirma: la verificación corre después, sobre el
        // contenedor ya construido, y no sobre lo que la composición pretendía registrar.
        // En Release lanza igual, pero por la condición de compilación y con otro mensaje: lo que este
        // caso afirma —que el mensaje nombra el ENTORNO detectado— describe el artefacto de depuración.
        HostConIdentidad.SaltearSiElArtefactoNoEsDeDepuracion();

        var excepcion = Assert.Throws<InvalidOperationException>(() => HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeProduccion,
            clave: null,
            ajustarServicios: _forzarElProveedorDeDesarrollo));

        // El mensaje tiene que decir las dos cosas accionables: qué entorno se detectó y qué proveedor
        // quedó resuelto. Sin ellas, quien despliega ve «no arranca» y no sabe qué corregir.
        Assert.Contains(HostConIdentidad.EntornoDeProduccion, excepcion.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(EmpleadoActualDesarrollo), excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task En_desarrollo_el_mismo_forzado_no_hace_fallar_el_arranque()
    {
        // Contracara de B4-T4. Sin este test, «el arranque falla con el proveedor de desarrollo» lo
        // cumpliría también una verificación que rechazara ese proveedor SIEMPRE, con lo que la
        // aplicación no podría correr en desarrollo y el bloque entero quedaría inútil. El guardarraíl
        // está acotado al entorno, y esto lo fija.
        HostConIdentidad.SaltearSiElArtefactoNoEsDeDepuracion();

        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeDesarrollo,
            clave: null,
            ajustarServicios: _forzarElProveedorDeDesarrollo);

        using var ambito = aplicacion.Services.CreateScope();

        Assert.IsType<EmpleadoActualDesarrollo>(
            ambito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());
    }

    [Fact]
    public async Task B4_T5_El_proveedor_de_desarrollo_es_scoped_uno_por_circuito()
    {
        HostConIdentidad.SaltearSiElArtefactoNoEsDeDepuracion();

        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeDesarrollo, clave: "true");

        using var primerAmbito = aplicacion.Services.CreateScope();
        using var segundoAmbito = aplicacion.Services.CreateScope();

        var delPrimero = primerAmbito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();
        var delSegundo = segundoAmbito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();

        Assert.IsType<EmpleadoActualDesarrollo>(delPrimero);
        Assert.IsType<EmpleadoActualDesarrollo>(delSegundo);

        // Dos ámbitos, dos instancias: en Blazor Server el ámbito es el circuito, así que como
        // singleton el empleado que elige una persona se lo cambiaría a todas las demás y AC-07 se
        // rompería sin que ningún test unitario lo viera.
        Assert.NotSame(delPrimero, delSegundo);

        // Y la mitad que se olvida: DENTRO del mismo circuito la instancia es la MISMA. Un registro
        // transitorio también daría instancias distintas entre ámbitos y pasaría la afirmación de
        // arriba, pero le daría una copia propia a cada componente y la selección del selector no
        // llegaría ni al formulario ni al listado. «Scoped» es exactamente la conjunción de las dos.
        Assert.Same(delPrimero, primerAmbito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>());

        // Con la clave activa sí existe el servicio de nómina: es el que alimenta el desplegable y el
        // que valida que el Id recibido exista. Es la contracara del aserto de R-04 en B4-T2.
        Assert.True(
            aplicacion.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(EmpleadosService)),
            "Sin el servicio de nómina, el selector no tiene qué mostrar ni contra qué validar el Id recibido.");
    }

    [Theory]
    [InlineData(HostConIdentidad.EntornoDeDesarrollo, "true", typeof(EmpleadoActualDesarrollo))]
    [InlineData(HostConIdentidad.EntornoDeDesarrollo, null, typeof(EmpleadoActualNoConfigurado))]
    [InlineData(HostConIdentidad.EntornoDeProduccion, "true", typeof(EmpleadoActualNoConfigurado))]
    [InlineData(HostConIdentidad.EntornoDeProduccion, null, typeof(EmpleadoActualNoConfigurado))]
    public async Task B4_T7_La_interfaz_de_identidad_tiene_un_unico_registro_y_ninguna_via_alternativa(
        string entorno,
        string? clave,
        Type implementacionEsperada)
    {
        // NFR-06: «1 única interfaz, 0 llamadores que obtengan el empleado por otra vía». Las cuatro
        // combinaciones de la doble condición se recorren juntas para que quede fijado que en TODAS
        // hay exactamente un registro: dos descriptores harían que el proveedor efectivo dependiera
        // del orden de registro, que es la clase de detalle que nadie revisa.
        if (implementacionEsperada == typeof(EmpleadoActualDesarrollo))
        {
            HostConIdentidad.SaltearSiElArtefactoNoEsDeDepuracion();
        }

        IServiceCollection? coleccion = null;

        await using var aplicacion = HostConIdentidad.Construir(
            entorno, clave, ajustarServicios: servicios => coleccion = servicios);

        Assert.NotNull(coleccion);

        var descriptor = Assert.Single(
            coleccion,
            descriptor => descriptor.ServiceType == typeof(IEmpleadoActualProvider));

        Assert.Equal(implementacionEsperada, descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);

        // Las implementaciones concretas NO se resuelven por su propio tipo. Si lo hicieran, un
        // componente podría inyectar EmpleadoActualDesarrollo directamente y obtener el empleado sin
        // pasar por la interfaz, que es literalmente el «llamador por otra vía» que NFR-06 cuenta en
        // cero. Y en desarrollo obtendría además una instancia DISTINTA de la del selector, con lo que
        // la identidad del circuito dejaría de ser una.
        var esServicio = aplicacion.Services.GetRequiredService<IServiceProviderIsService>();

        Assert.False(
            esServicio.IsService(typeof(EmpleadoActualDesarrollo)),
            "EmpleadoActualDesarrollo se resuelve por su propio tipo: es una vía de acceso al empleado actual que no pasa por la interfaz.");
        Assert.False(
            esServicio.IsService(typeof(EmpleadoActualNoConfigurado)),
            "EmpleadoActualNoConfigurado se resuelve por su propio tipo: mismo problema por la otra puerta.");
    }

    [Theory]
    [InlineData(HostConIdentidad.EntornoDeDesarrollo, "true", true)]
    [InlineData(HostConIdentidad.EntornoDeDesarrollo, null, false)]
    [InlineData(HostConIdentidad.EntornoDeProduccion, "true", false)]
    [InlineData(HostConIdentidad.EntornoDeProduccion, null, false)]
    public async Task La_capacidad_de_elegir_empleado_la_contesta_la_interfaz_en_las_cuatro_combinaciones(
        string entorno,
        string? clave,
        bool seEsperaQuePermita)
    {
        // El Bloque 6 tiene que mostrar el selector SOLO cuando el proveedor de desarrollo está activo, y
        // esta es la única forma de que lo pregunte sin abrir una segunda sede de la identidad (NFR-06):
        // nombrar el tipo concreto lo prohíbe el guardarraíl de más abajo, capturar la excepción es
        // control de flujo por excepción —y AGENTS.md prohíbe el catch silencioso—, y releer el entorno
        // y la clave desde el .razor duplicaría la decisión de R-01 en la interfaz, que es peor que las
        // dos anteriores porque la copia queda lejos del original.
        //
        // Se recorren las cuatro combinaciones de la doble condición, no solo las dos «interesantes»:
        // la capacidad tiene que seguir a la MISMA condición que el registro, y las dos formas de no
        // satisfacerla —entorno equivocado, clave ausente— tienen que contestar lo mismo.
        if (seEsperaQuePermita)
        {
            HostConIdentidad.SaltearSiElArtefactoNoEsDeDepuracion();
        }

        await using var aplicacion = HostConIdentidad.Construir(entorno, clave);

        using var ambito = aplicacion.Services.CreateScope();
        var proveedor = ambito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();

        // Se lee por la INTERFAZ, que es lo que va a tener el componente en la mano.
        Assert.Equal(seEsperaQuePermita, proveedor.PermiteElegirEmpleado);
    }

    [Fact]
    public void B4_T7_Las_unicas_implementaciones_de_la_interfaz_son_las_dos_del_proyecto_Web()
    {
        // El registro de hoy es una foto; esto es una propiedad del código. Una tercera implementación
        // —un proveedor «de demo», un stub que alguien deja para probar— sería una segunda sede de la
        // resolución de identidad aunque nadie la registrara todavía, y NFR-06 dice 1.
        var implementaciones = new[] { typeof(IEmpleadoActualProvider).Assembly, typeof(EmpleadoActualDesarrollo).Assembly }
            .Distinct()
            .SelectMany(ensamblado => ensamblado.GetTypes())
            .Where(tipo => tipo is { IsClass: true, IsAbstract: false } &&
                           typeof(IEmpleadoActualProvider).IsAssignableFrom(tipo))
            .Select(tipo => tipo.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        var esperadas = new[] { typeof(EmpleadoActualDesarrollo).FullName!, typeof(EmpleadoActualNoConfigurado).FullName! }
            .Order(StringComparer.Ordinal)
            .ToList();

        // El mensaje nombra las dos listas: el diff de colecciones de la biblioteca de aserciones las
        // recorta justo donde está el nombre que importa.
        Assert.True(
            implementaciones.SequenceEqual(esperadas, StringComparer.Ordinal),
            $"Las implementaciones de la identidad son [{string.Join(", ", implementaciones)}] y NFR-06 " +
            $"exige exactamente [{string.Join(", ", esperadas)}]: cualquier otra es una segunda sede de " +
            "la resolución del empleado actual, aunque hoy nadie la registre.");
    }

    [Fact]
    public void B4_T7_Ningun_archivo_de_src_nombra_las_implementaciones_concretas_fuera_de_su_carpeta_y_del_arranque()
    {
        // La otra mitad de «0 llamadores por otra vía», y la que sobrevive a los bloques que faltan:
        // los componentes del Bloque 6 y los servicios del Bloque 5 tienen que consumir la INTERFAZ. Si
        // alguno nombra una implementación concreta, o la resuelve a mano, este test se pone rojo antes
        // de que la identidad tenga dos sedes.
        var raiz = RaizDelRepositorio.Localizar();
        var codigo = Path.Combine(raiz, "src");

        string[] lugaresPermitidos =
        [
            "src/GestionVacaciones.Web/Identidad/",
            "src/GestionVacaciones.Web/Program.cs",
        ];

        string[] nombresConcretos = [nameof(EmpleadoActualDesarrollo), nameof(EmpleadoActualNoConfigurado)];

        var archivos = Directory
            .EnumerateFiles(codigo, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(archivo => archivo.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                              archivo.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Select(archivo => Path.GetRelativePath(raiz, archivo).Replace('\\', '/'))
            .Where(relativa => !relativa.Split('/').Any(tramo => tramo is "bin" or "obj"))
            .ToList();

        // Sin esto el test pasaría en verde por no haber encontrado nada que revisar.
        Assert.NotEmpty(archivos);

        var infracciones = archivos
            .Where(relativa => !lugaresPermitidos.Any(permitido =>
                relativa.StartsWith(permitido, StringComparison.Ordinal)))
            .Where(relativa => nombresConcretos.Any(nombre =>
                File.ReadAllText(Path.Combine(raiz, relativa)).Contains(nombre, StringComparison.Ordinal)))
            .ToList();

        // El mensaje nombra los archivos: un «la colección no estaba vacía» obliga a repetir la búsqueda
        // a mano, y este test lo va a disparar alguien que está agregando un componente, no quien lo
        // escribió.
        Assert.True(
            infracciones.Count == 0,
            "Estos archivos nombran una implementación concreta de la identidad fuera de Web/Identidad/ " +
            $"y del arranque: {string.Join(", ", infracciones)}. La identidad se consume por la interfaz " +
            "(NFR-06); ni siquiera conviene nombrarlas en un comentario, porque el próximo paso después " +
            "de nombrarlas es inyectarlas.");
    }
}
