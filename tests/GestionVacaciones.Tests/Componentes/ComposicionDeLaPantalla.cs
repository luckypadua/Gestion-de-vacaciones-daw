using GestionVacaciones.Data;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Dominio;
using GestionVacaciones.Tests.Persistencia;
using GestionVacaciones.Web.Identidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// La composición que necesita la pantalla del empleado para funcionar contra la base descartable de
/// la corrida: los servicios del dominio del Bloque 5 y el proveedor de identidad <b>productivo</b> de
/// desarrollo del Bloque 4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué en un punto único y no repetida en cada clase de test.</b> Es la composición bajo
/// prueba, no un detalle de armado: si dos clases la escriben por separado y una queda distinta —un
/// tiempo de vida cambiado, una implementación de identidad sustituida por un doble— las dos siguen en
/// verde midiendo cosas distintas, y la que se aleja de la composición real es la que deja de valer.
/// </para>
/// <para>
/// Los tiempos de vida son los mismos que registra <c>Program.RegistrarIdentidad</c> y
/// <c>Program.RegistrarDominio</c>: <c>scoped</c>, que en Blazor Server es «uno por circuito».
/// </para>
/// </remarks>
internal static class ComposicionDeLaPantalla
{
    public static void RegistrarContraLaBaseDeTest(
        IServiceCollection servicios,
        BaseDeDatosDeTest baseDeDatos,
        TimeProvider tiempo)
    {
        servicios.AddSingleton(tiempo);
        servicios.AddSingleton<IDbContextFactory<VacacionesDbContext>>(new FabricaDeLaBaseDeTest(baseDeDatos));
        servicios.AddScoped<EmpleadosService>();
        servicios.AddScoped<PermisosService>();
        servicios.AddScoped<SolicitudesService>();
        servicios.AddScoped<SaldoService>();

        // La composición real de Development, con su doble condición ya satisfecha por el host: acá se
        // registra la implementación que aquel elige, no un sustituto del sustituto.
        servicios.AddScoped<IEmpleadoActualProvider, EmpleadoActualDesarrollo>();
    }
}
