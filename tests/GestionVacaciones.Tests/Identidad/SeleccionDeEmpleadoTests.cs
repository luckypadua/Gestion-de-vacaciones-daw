using GestionVacaciones.Data;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GestionVacaciones.Tests.Identidad;

/// <summary>
/// B4-T3 y B4-T6: la selección del empleado actual sobre la nómina sembrada, contra la instancia SQL
/// Server 2022 del entorno.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué necesitan motor.</b> AC-07 habla de «un empleado de la nómina sembrada», y la validación
/// del bloque es que el identificador recibido <b>exista en la nómina</b>. Las dos cosas son
/// afirmaciones sobre filas de la tabla <c>Empleados</c>: con un doble en memoria, «el Id inexistente
/// se rechaza» pasaría en verde sin haber consultado nada, que es justo la confianza que la validación
/// no debe tener.
/// </para>
/// <para>
/// La nómina la escribe <see cref="SeedDatos"/>, igual que en desarrollo, declarando como sembrable el
/// catálogo descartable de la corrida. Es la dependencia que la spec fija entre bloques —«B4 necesita
/// la nómina sembrada por B3»— ejercitada de verdad y no simulada.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class SeleccionDeEmpleadoTests
{
    private readonly BaseDeDatosDeTest _baseDeDatos;

    public SeleccionDeEmpleadoTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    /// <summary>Los cuatro nombres de la nómina sembrada, en el orden alfabético que espera el selector.</summary>
    private static string[] NominaEnOrden =>
        [SeedDatos.NombreDeAna, SeedDatos.NombreDeBruno, SeedDatos.NombreDeCarla, SeedDatos.NombreDeDiego];

    [Fact]
    public async Task B4_T3_El_empleado_elegido_de_la_nomina_sembrada_queda_como_identidad_del_circuito()
    {
        // AC-07, hasta donde llega este bloque: el empleado elegido es el que verán como autor y como
        // sujeto del listado TODOS los consumidores del circuito. Que el alta lo use como autor y el
        // listado como sujeto lo cierran B5-T5, B5-T7 y B6-T5, que son de bloques que todavía no
        // existen; lo que se fija acá es la propiedad de la que ambos dependen.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var limpieza = new NominaDescartable(_baseDeDatos);
        await SembrarLaNominaAsync();

        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeDesarrollo, clave: "true", cadena: CadenaDeLaCorrida());

        using var circuito = aplicacion.Services.CreateScope();

        var nomina = await circuito.ServiceProvider
            .GetRequiredService<EmpleadosService>()
            .ListarNominaAsync(Cancelacion);

        // El selector tiene qué mostrar, y en un orden estable: un desplegable cuyo orden cambia entre
        // consultas es una trampa para quien elige rápido.
        Assert.Equal(NominaEnOrden, nomina.Select(empleado => empleado.Nombre).Where(NominaEnOrden.Contains));

        var ana = Assert.Single(nomina, empleado => empleado.Nombre == SeedDatos.NombreDeAna);

        // Dos resoluciones dentro del MISMO ámbito: es lo que le pasa al formulario de alta y al
        // listado, que son dos componentes del mismo circuito. El selector cambia el empleado en uno y
        // el otro tiene que ver el cambio, o AC-07 se cumple a medias.
        var elDelAlta = circuito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();
        var elDelListado = circuito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();

        Assert.False(elDelAlta.Identidad.HayEmpleadoSeleccionado);

        Assert.Equal(ResultadoDeSeleccion.Seleccionado, await elDelAlta.SeleccionarAsync(ana.Id, Cancelacion));

        Assert.True(elDelAlta.Identidad.HayEmpleadoSeleccionado);
        Assert.Equal(ana.Id, elDelAlta.Identidad.Id);
        Assert.Equal(ana.Id, elDelListado.Identidad.Id);

        // Y la elección NO se le escapa a otro circuito. Es la mitad de «scoped» que importa para el
        // producto: sin esto, la persona que elige un empleado se lo cambia a todas las demás.
        using var otroCircuito = aplicacion.Services.CreateScope();
        var deOtroCircuito = otroCircuito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();

        Assert.False(deOtroCircuito.Identidad.HayEmpleadoSeleccionado);
    }

    [Fact]
    public async Task B4_T6_Seleccionar_un_Id_inexistente_se_rechaza_y_conserva_el_anterior()
    {
        // El Id llega del navegador por el circuito (TB-1) y es entrada no confiable: que el desplegable
        // solo ofrezca valores válidos es una propiedad de la interfaz, no un control. Un evento
        // fabricado a mano trae cualquier número.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var limpieza = new NominaDescartable(_baseDeDatos);
        await SembrarLaNominaAsync();

        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeDesarrollo, clave: "true", cadena: CadenaDeLaCorrida());

        using var circuito = aplicacion.Services.CreateScope();

        var nomina = await circuito.ServiceProvider
            .GetRequiredService<EmpleadosService>()
            .ListarNominaAsync(Cancelacion);

        var proveedor = circuito.ServiceProvider.GetRequiredService<IEmpleadoActualProvider>();
        var idInexistente = nomina.Max(empleado => empleado.Id) + 1_000;

        // Primero sin nadie elegido todavía: el rechazo no puede dejar «cualquiera» seleccionado.
        Assert.Equal(
            ResultadoDeSeleccion.RechazadaPorEmpleadoInexistente,
            await proveedor.SeleccionarAsync(idInexistente, Cancelacion));
        Assert.False(proveedor.Identidad.HayEmpleadoSeleccionado);

        var bruno = Assert.Single(nomina, empleado => empleado.Nombre == SeedDatos.NombreDeBruno);
        Assert.Equal(ResultadoDeSeleccion.Seleccionado, await proveedor.SeleccionarAsync(bruno.Id, Cancelacion));

        // Y ahora con alguien elegido: se conserva el anterior. Vaciar la identidad ante una entrada
        // inválida sería tan malo como aceptarla —le cambiaría el sujeto del listado a quien no pidió
        // nada—, y quedar en un estado intermedio es peor que las dos.
        Assert.Equal(
            ResultadoDeSeleccion.RechazadaPorEmpleadoInexistente,
            await proveedor.SeleccionarAsync(idInexistente, Cancelacion));
        Assert.Equal(bruno.Id, proveedor.Identidad.Id);

        // «No se persiste nada»: el rechazo no crea el empleado que faltaba ni toca la nómina.
        await using var contexto = _baseDeDatos.CrearContexto();
        Assert.False(await contexto.Empleados.AnyAsync(empleado => empleado.Id == idInexistente, Cancelacion));
        Assert.Equal(nomina.Count, await contexto.Empleados.CountAsync(Cancelacion));
    }

    /// <summary>
    /// Cadena de la base descartable de esta corrida. Se toma de un contexto del fixture y no de la
    /// configuración: así el guardarraíl del sufijo <c>_Test</c> ya se aplicó y el host que se
    /// construye acá no puede apuntar a otra base.
    /// </summary>
    private string CadenaDeLaCorrida()
    {
        using var sonda = _baseDeDatos.CrearContexto();
        return sonda.Database.GetConnectionString()!;
    }

    /// <summary>
    /// Siembra la nómina con la misma clase que usa el arranque en desarrollo, declarando sembrable el
    /// catálogo descartable de la corrida. El guardarraíl de R-03 no se afloja: se le declara otro
    /// conjunto, que es el punto de <see cref="CatalogosSembrables"/>.
    /// </summary>
    private async Task SembrarLaNominaAsync()
    {
        var semilla = new SeedDatos(
            new FabricaDelFixture(_baseDeDatos),
            CatalogosSembrables.Declarar(_baseDeDatos.Catalogo),
            NullLogger<SeedDatos>.Instance);

        Assert.Equal(ResultadoDeSemilla.Sembrada, await semilla.SembrarAsync(Cancelacion));
    }

    private sealed class FabricaDelFixture(BaseDeDatosDeTest baseDeDatos)
        : IDbContextFactory<VacacionesDbContext>
    {
        public VacacionesDbContext CreateDbContext() => baseDeDatos.CrearContexto();
    }

    /// <summary>
    /// Borra la nómina sembrada al salir del alcance, pase lo que pase con el test (regla #0 de
    /// testing: cada test crea sus datos y los limpia).
    /// </summary>
    private sealed class NominaDescartable(BaseDeDatosDeTest baseDeDatos) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var contexto = baseDeDatos.CrearContexto();

            var nomina = await contexto.Empleados
                .Where(empleado => SeedDatos.CorreosDeLaNomina.Contains(empleado.Correo))
                .ToListAsync(Cancelacion);

            if (nomina.Count == 0)
            {
                return;
            }

            // Primero se cortan las relaciones y después se borra: las dos FK son autorreferencias con
            // ON DELETE NO ACTION —el ciclo Ana↔Diego obliga a NO ACTION— y SQL Server rechaza borrar
            // una fila a la que otra sigue apuntando.
            foreach (var empleado in nomina)
            {
                empleado.ManagerId = null;
                empleado.DesignadoId = null;
            }

            await contexto.SaveChangesAsync(Cancelacion);

            contexto.Empleados.RemoveRange(nomina);
            await contexto.SaveChangesAsync(Cancelacion);
        }
    }
}
