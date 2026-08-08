using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// Colección de los tests de integración que comparten la base de datos descartable
/// <c>GestionVacacionesV2_Test</c>. La comparten para aplicar las migraciones una sola vez por
/// corrida; cada test sigue creando y limpiando sus propios datos (regla #0 de testing).
/// </summary>
/// <remarks>
/// <c>DisableParallelization</c> no es decorativo: <see cref="Andamiaje.ColeccionDeEntornoDeProceso"/>
/// contiene tests que anulan temporalmente <c>VACACIONES_CONNECTION_TEST</c>. Si esta colección
/// corriera en paralelo con aquella, el fixture podría leer la variable justo mientras está anulada y
/// saltear toda la verificación de NFR-04 sin que nadie lo note.
/// </remarks>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionDeBaseDeDatos : ICollectionFixture<BaseDeDatosDeTest>
{
    public const string Nombre = "BaseDeDatosDeTest";
}
