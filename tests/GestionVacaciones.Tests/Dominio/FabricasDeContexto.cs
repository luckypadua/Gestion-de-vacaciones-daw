using GestionVacaciones.Data;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Fábrica de contextos que <b>lanza en cuanto alguien la usa</b>. No es un doble para simular la base:
/// es la afirmación de que el caso bajo prueba <b>no la necesita</b>.
/// </summary>
/// <remarks>
/// Es la forma más fuerte de fijar «no persiste» de B5-T3, B5-T4 y B5-T6. Un aserto sobre la tabla
/// después del rechazo demuestra que no quedó la fila; esto demuestra algo más estricto: que la
/// validación ocurrió <b>antes</b> de abrir un contexto, así que ni siquiera hubo un viaje que pudiera
/// haber escrito.
/// </remarks>
internal sealed class FabricaQueNadieDebeUsar : IDbContextFactory<VacacionesDbContext>
{
    public const string Marca = "el caso bajo prueba no debe abrir ningún contexto";

    public VacacionesDbContext CreateDbContext() => throw new InvalidOperationException(Marca);
}

/// <summary>
/// Fábrica contra la base descartable de la corrida. Es el puente entre el fixture de integración del
/// Bloque 2 —que entrega contextos— y los servicios del dominio, que reciben una fábrica porque NFR-05
/// exige que cada operación abra y cierre el suyo.
/// </summary>
internal sealed class FabricaDeLaBaseDeTest : IDbContextFactory<VacacionesDbContext>
{
    private readonly BaseDeDatosDeTest _baseDeDatos;

    public FabricaDeLaBaseDeTest(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    public VacacionesDbContext CreateDbContext() => _baseDeDatos.CrearContexto();
}
