using GestionVacaciones.Data;
using GestionVacaciones.Tests.Andamiaje;
using GestionVacaciones.Tests.Identidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// El punto de extensión de la composición —la costura que los tests usan para <b>forzar</b> registros
/// que la composición normal no haría— no puede debilitar los guardarraíles del arranque.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué no vive dentro de <see cref="ComposicionDeAccesoADatosTests"/>.</b> Ese construye el host
/// <i>sin</i> costura, que es lo que corresponde para afirmar la composición normal de NFR-05. Lo que
/// se verifica acá es otra cosa: que la costura, que es lo último que corre antes de construir el
/// contenedor, no alcance para reabrir el agujero. Son dos preocupaciones con dos modos de fallo
/// distintos, y mezclarlas haría que el nombre de la clase mintiera sobre una de las dos.
/// </para>
/// <para>
/// <b>Por qué hace falta el test.</b> El guardarraíl de R-01 corre sobre el contenedor ya construido y
/// por eso cubre a la costura por construcción. El de NFR-05 es un <c>RemoveAll</c> sobre la colección
/// de servicios, es decir una operación de un momento determinado: si ocurriera <i>antes</i> de la
/// costura, un delegado de dos líneas volvería a registrar el <c>DbContext</c> por ámbito y ninguno de
/// los tests existentes lo vería —el de composición construye sin costura, y el que escanea el código
/// fuente busca el literal <c>AddDbContext&lt;</c>, que este delegado no escribe—.
/// </para>
/// <para>
/// Va en la colección del entorno de proceso porque construir el host fija variables de entorno, que
/// son estado global.
/// </para>
/// </remarks>
[Collection(ColeccionDeEntornoDeProceso.Nombre)]
public sealed class CosturaDeComposicionTests
{
    [Fact]
    public async Task La_costura_de_la_composicion_no_puede_reabrir_el_DbContext_por_circuito()
    {
        // El delegado registra el contexto por ámbito SIN escribir «AddDbContext» en ningún lado: pide
        // la fábrica y devuelve un contexto. Es exactamente el descriptor que AddDbContextFactory agrega
        // por su cuenta y que el Bloque 2 retira, o sea el agujero de NFR-05 reabierto por la puerta que
        // el escaneo de fuente no cubre.
        var elAgujeroQuedoAbiertoEnLaCostura = false;

        await using var aplicacion = HostConIdentidad.Construir(
            HostConIdentidad.EntornoDeProduccion,
            clave: null,
            ajustarServicios: servicios =>
            {
                servicios.AddScoped(proveedor =>
                    proveedor.GetRequiredService<IDbContextFactory<VacacionesDbContext>>().CreateDbContext());

                elAgujeroQuedoAbiertoEnLaCostura =
                    servicios.Any(descriptor => descriptor.ServiceType == typeof(VacacionesDbContext));
            });

        // Sin esta afirmación el test podría estar pasando porque el registro forzado nunca ocurrió, y
        // entonces no demostraría nada: «no hay descriptor» se cumple igual si nadie intentó agregarlo.
        Assert.True(
            elAgujeroQuedoAbiertoEnLaCostura,
            "La costura no llegó a registrar el contexto, así que este test no está reproduciendo el agujero que dice cerrar.");

        // Y el guardarraíl gana: corre DESPUÉS de la costura, así que el contenedor construido no
        // resuelve el contexto por más que la costura lo haya registrado.
        Assert.False(
            aplicacion.Services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(VacacionesDbContext)),
            "El contenedor resuelve VacacionesDbContext: la costura de test reabrió el DbContext por circuito que NFR-05 prohíbe, y el RemoveAll del arranque quedó antes de ella.");
    }
}
