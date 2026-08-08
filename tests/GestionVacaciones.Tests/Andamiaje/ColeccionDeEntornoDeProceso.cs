using Xunit;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// Colección que serializa los tests que modifican variables de entorno del proceso. Sin ella,
/// xUnit ejecuta las clases en paralelo y una establecería <c>VACACIONES_CONNECTION</c> mientras
/// otra afirma que no existe.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionDeEntornoDeProceso
{
    public const string Nombre = "EntornoDeProceso";
}
