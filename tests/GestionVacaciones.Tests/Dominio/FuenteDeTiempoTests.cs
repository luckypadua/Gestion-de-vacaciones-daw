using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Tests.Identidad;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Guardarraíl del criterio de cierre del bloque: el tiempo entra por <see cref="TimeProvider"/>
/// inyectado y por ningún otro lado, y la composición del host lo registra junto a los dos servicios del
/// dominio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué el escaneo del código fuente, si ya hay tests con el reloj detenido.</b> Aquellos afirman
/// que el servicio de hoy usa el proveedor; esto es una propiedad del proyecto. Una sola llamada a
/// <c>DateTime.Today</c> agregada más adelante —en una regla de saldo, en un cálculo de vencimiento—
/// vuelve el resultado dependiente del reloj de la máquina y de su huso, y el síntoma aparece recién en
/// la corrida que cruza la medianoche o en la máquina configurada en UTC.
/// </para>
/// <para>
/// Va en la colección del entorno de proceso porque construir el host fija variables de entorno, que son
/// estado global.
/// </para>
/// </remarks>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class FuenteDeTiempoTests
{
    /// <summary>
    /// Las cinco formas de leer el reloj del sistema sin pasar por <see cref="TimeProvider"/>. El
    /// criterio de cierre nombra las dos primeras; las otras tres entran porque son las que uno escribe
    /// cuando la primera «no compila»: <c>FechaCreacion</c> es un <c>DateTimeOffset</c>, así que
    /// <c>DateTimeOffset.UtcNow</c> es el camino más corto para saltear el criterio al pie de la letra
    /// cumpliéndolo.
    /// </summary>
    private static readonly string[] _lecturasDirectasDelReloj =
    [
        "DateTime.Today",
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
    ];

    [Fact]
    public void El_proyecto_Data_no_lee_el_reloj_del_sistema_por_ningun_camino_directo()
    {
        var raiz = RaizDelRepositorio.Localizar();
        var codigo = Path.Combine(raiz, "src", "GestionVacaciones.Data");

        var archivos = Directory
            .EnumerateFiles(codigo, "*.cs", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Select(archivo => Path.GetRelativePath(raiz, archivo).Replace('\\', '/'))
            .Where(relativa => !relativa.Split('/').Any(tramo => tramo is "bin" or "obj"))
            .ToList();

        // Sin esto el test pasaría en verde por no haber encontrado nada que revisar.
        Assert.NotEmpty(archivos);

        var infracciones = new List<string>();
        foreach (var relativa in archivos)
        {
            var contenido = File.ReadAllText(Path.Combine(raiz, relativa));

            infracciones.AddRange(_lecturasDirectasDelReloj
                .Where(lectura => contenido.Contains(lectura, StringComparison.Ordinal))
                .Select(lectura => $"{relativa} usa {lectura}"));
        }

        Assert.True(
            infracciones.Count == 0,
            $"El proyecto Data lee el reloj del sistema directamente: {string.Join(", ", infracciones)}. " +
            "El tiempo entra por TimeProvider inyectado: sin eso, los tests de AC-02 dependen del día en " +
            "que se corran y del huso de la máquina.");
    }

    [Fact]
    public async Task El_host_registra_el_TimeProvider_y_los_dos_servicios_del_dominio()
    {
        // Sin el registro, los componentes del Bloque 6 no pueden inyectar nada y el bloque entero queda
        // inalcanzable desde la aplicación. Se construye en Production a propósito: los servicios del
        // dominio NO dependen de la doble condición de R-01 —el alta y el listado son funcionalidad del
        // producto, no andamiaje de desarrollo— así que tienen que existir en cualquier entorno.
        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeProduccion, clave: null);

        using var ambito = aplicacion.Services.CreateScope();

        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<TimeProvider>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<PermisosService>());
        Assert.NotNull(ambito.ServiceProvider.GetRequiredService<SolicitudesService>());
    }

    [Fact]
    public async Task El_TimeProvider_registrado_es_el_del_sistema_y_es_uno_solo()
    {
        // Que exista no alcanza: un TimeProvider distinto por ámbito permitiría que dos componentes del
        // mismo circuito discrepen sobre qué día es hoy. Y tiene que ser el del sistema: un doble
        // registrado por descuido en la composición productiva congelaría el «hoy» de la aplicación.
        //
        // NOTA HONESTA sobre este caso: el host de ASP.NET Core ya registra TimeProvider.System por su
        // cuenta, así que estos dos asertos pasaban ANTES de que la composición del bloque lo registrara.
        // Lo que agrega valor —y lo que este caso sí puede romper— es el aserto del descriptor único de
        // más abajo: registrar con Add en vez de con TryAdd deja dos descriptores, y ahí el TimeProvider
        // efectivo pasa a depender del orden de registro.
        IServiceCollection? coleccion = null;

        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeProduccion,
            clave: null,
            ajustarServicios: servicios => coleccion = servicios);

        using var primerAmbito = aplicacion.Services.CreateScope();
        using var segundoAmbito = aplicacion.Services.CreateScope();

        var delPrimero = primerAmbito.ServiceProvider.GetRequiredService<TimeProvider>();

        Assert.Same(TimeProvider.System, delPrimero);
        Assert.Same(delPrimero, segundoAmbito.ServiceProvider.GetRequiredService<TimeProvider>());

        Assert.NotNull(coleccion);

        var descriptor = Assert.Single(coleccion, descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    [Fact]
    public async Task Los_servicios_del_dominio_son_scoped_uno_por_circuito()
    {
        // Mismo tiempo de vida que la identidad, que es scoped porque en Blazor Server el ámbito es el
        // circuito. Como singleton, SolicitudesService capturaría el IEmpleadoActualProvider del primer
        // circuito que lo resolviera y crearía las solicitudes de todos a nombre de ese empleado: AC-07
        // roto de la peor forma, sin excepción y sin síntoma.
        //
        // El contenedor de Development valida los ámbitos al construirse, así que un registro singleton
        // que dependa de un scoped ya no llegaría hasta acá; el test lo fija igual, porque la validación
        // solo corre en Development y este es un riesgo de producción.
        IServiceCollection? coleccion = null;

        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeProduccion,
            clave: null,
            ajustarServicios: servicios => coleccion = servicios);

        Assert.NotNull(coleccion);

        var deSolicitudes = Assert.Single(
            coleccion, descriptor => descriptor.ServiceType == typeof(SolicitudesService));
        var dePermisos = Assert.Single(
            coleccion, descriptor => descriptor.ServiceType == typeof(PermisosService));

        Assert.Equal(ServiceLifetime.Scoped, deSolicitudes.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, dePermisos.Lifetime);
    }
}
