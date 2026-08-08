using GestionVacaciones.Data;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Web.Configuracion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using HostWeb = GestionVacaciones.Web.Program;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// B2-T6: composición del acceso a datos (NFR-05). El contenedor expone
/// <see cref="IDbContextFactory{TContext}"/> y ningún descriptor del <see cref="VacacionesDbContext"/>
/// directo.
/// </summary>
/// <remarks>
/// No es purismo. En Blazor Server un <c>DbContext</c> scoped vive todo el circuito, y dos
/// componentes que consultan a la vez lo usan concurrentemente: EF lanza «A second operation was
/// started on this context instance» y la página se cae. <c>AGENTS.md</c> lo registra como cicatriz.
/// </remarks>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class ComposicionDeAccesoADatosTests
{
    /// <summary>
    /// Cadena bien formada que no apunta a nada. El catálogo es deliberadamente ficticio y <b>no</b>
    /// nombra <c>GestionVacacionesV2</c>: ninguno de estos casos abre conexión hoy, pero agregar acá
    /// una consulta es lo más natural del mundo, y entonces el nombre de una base real dejaría de ser
    /// inofensivo. El guardarraíl del fixture protege esos dos catálogos justamente porque nombrarlos
    /// en un test es el primer paso para tocarlos.
    /// </summary>
    private const string CadenaValida =
        "Server=localhost,1433;Initial Catalog=CatalogoFicticioDeComposicion;Integrated Security=True;" +
        "Encrypt=True;TrustServerCertificate=False";

    private const string VariableDeEntornoDeAspNet = "ASPNETCORE_ENVIRONMENT";
    private const string VariableDeEntornoDeDotnet = "DOTNET_ENVIRONMENT";

    [Fact]
    public async Task B2_T6_El_contenedor_expone_la_fabrica_y_ningun_DbContext_directo()
    {
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, "Production");
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaValida);

        await using var aplicacion = HostWeb.ConstruirAplicacion([]);
        var esServicio = aplicacion.Services.GetRequiredService<IServiceProviderIsService>();

        Assert.True(
            esServicio.IsService(typeof(IDbContextFactory<VacacionesDbContext>)),
            "El contenedor no expone IDbContextFactory<VacacionesDbContext>: no hay forma de acceder a datos como exige NFR-05.");

        // Ni el contexto ni su tipo base se resuelven directamente. Ojo: no alcanza con NO escribir
        // AddDbContext. AddDbContextFactory agrega por su cuenta un descriptor scoped de
        // VacacionesDbContext que delega en la fábrica, y scoped en Blazor Server es «uno por
        // circuito»: el componente que lo inyectara volvería a compartir contexto entre operaciones
        // concurrentes. Program.cs lo retira a propósito, y esto es lo que lo fija.
        Assert.False(
            esServicio.IsService(typeof(VacacionesDbContext)),
            "El contenedor resuelve VacacionesDbContext directamente: es un contexto por circuito, que es lo que NFR-05 prohíbe.");
        Assert.False(
            esServicio.IsService(typeof(DbContext)),
            "El contenedor resuelve DbContext directamente: mismo problema por la puerta del tipo base.");
    }

    [Fact]
    public async Task B2_T6_Las_opciones_del_contexto_son_singleton_y_no_por_ambito()
    {
        // El discriminante entre las dos formas de registrar: AddDbContextFactory deja
        // DbContextOptions<T> como SINGLETON, y AddDbContext lo deja por ámbito. Sin esta afirmación,
        // «no hay descriptor del contexto» se cumpliría también si alguien registrara AddDbContext y
        // después borrara el descriptor a mano, que se ve igual y no lo es.
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, "Production");
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaValida);

        await using var aplicacion = HostWeb.ConstruirAplicacion([]);

        using var primerAmbito = aplicacion.Services.CreateScope();
        using var segundoAmbito = aplicacion.Services.CreateScope();

        var opcionesDelPrimero = primerAmbito.ServiceProvider.GetRequiredService<DbContextOptions<VacacionesDbContext>>();
        var opcionesDelSegundo = segundoAmbito.ServiceProvider.GetRequiredService<DbContextOptions<VacacionesDbContext>>();

        Assert.Same(opcionesDelPrimero, opcionesDelSegundo);
    }

    [Fact]
    public async Task B2_T6_La_fabrica_entrega_un_contexto_distinto_en_cada_llamada()
    {
        // Que el descriptor exista no prueba que sirva. Dos contextos distintos por llamada es la
        // propiedad concreta que evita el «A second operation was started on this context instance»:
        // si la fábrica devolviera siempre la misma instancia, el registro sería decorativo.
        using var entornoAspNet = new VariableDeEntornoTemporal(VariableDeEntornoDeAspNet, "Production");
        using var entornoDotnet = new VariableDeEntornoTemporal(VariableDeEntornoDeDotnet, null);
        using var variable = new VariableDeEntornoTemporal(CadenaDeConexion.VariableDeEntorno, CadenaValida);

        await using var aplicacion = HostWeb.ConstruirAplicacion([]);
        var fabrica = aplicacion.Services.GetRequiredService<IDbContextFactory<VacacionesDbContext>>();

        // Crear el contexto no abre conexión: la cadena de arriba no apunta a ninguna instancia viva.
        await using var primero = fabrica.CreateDbContext();
        await using var segundo = fabrica.CreateDbContext();

        Assert.NotSame(primero, segundo);
    }

    [Fact]
    public void B2_T6_Ningun_archivo_de_src_registra_el_contexto_con_AddDbContext()
    {
        // NFR-05 dice «0 registros de DbContext mediante AddDbContext», y eso es una propiedad del
        // código fuente, no solo del contenedor de hoy. Sin esta comprobación, alguien agrega
        // AddDbContext en un segundo host —o en un job futuro— y el test de composición de arriba
        // sigue verde porque mira otra composición.
        var raiz = RaizDelRepositorio.Localizar();
        var codigo = Path.Combine(raiz, "src");

        var archivos = Directory
            .EnumerateFiles(codigo, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(archivo => archivo.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                              archivo.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Where(archivo => !RutaRelativa(raiz, archivo).Split('/').Any(tramo => tramo is "bin" or "obj"))
            .ToList();

        // Sin esta afirmación el test pasaría en verde por no haber encontrado nada que revisar.
        Assert.NotEmpty(archivos);

        var infracciones = new List<string>();
        foreach (var archivo in archivos)
        {
            var contenido = File.ReadAllText(archivo);
            // Se busca la llamada «AddDbContext<» y se descuenta «AddDbContextFactory<», que es la
            // permitida y contiene a la otra como prefijo.
            var apariciones = ContarApariciones(contenido, "AddDbContext<") -
                              ContarApariciones(contenido, "AddDbContextFactory<");
            if (apariciones > 0)
            {
                infracciones.Add($"{RutaRelativa(raiz, archivo)} registra el contexto con AddDbContext");
            }
        }

        Assert.Empty(infracciones);
    }

    private static string RutaRelativa(string raiz, string archivo) =>
        Path.GetRelativePath(raiz, archivo).Replace('\\', '/');

    private static int ContarApariciones(string contenido, string patron)
    {
        var total = 0;
        var indice = contenido.IndexOf(patron, StringComparison.Ordinal);
        while (indice >= 0)
        {
            total++;
            indice = contenido.IndexOf(patron, indice + patron.Length, StringComparison.Ordinal);
        }

        return total;
    }
}
