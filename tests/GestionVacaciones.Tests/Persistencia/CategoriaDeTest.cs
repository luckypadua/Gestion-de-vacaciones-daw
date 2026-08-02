namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// Vocabulario único de las categorías de test. Existe para que el nombre del trait y su valor no se
/// escriban a mano en cada clase: un «Integración» con tilde o un «Category» en minúscula dejarían el
/// test fuera del filtro sin que nada se ponga en rojo.
/// </summary>
/// <remarks>
/// <para>
/// La clave se llama <c>Category</c> —en inglés, contra la convención de nombres del resto del
/// repositorio— por <b>convención de tooling, no por necesidad técnica</b>: VSTest filtra por
/// cualquier clave de trait, así que <c>Categoria</c> funcionaría exactamente igual y
/// <c>--filter "Categoria=Integracion"</c> seleccionaría los mismos tests. Lo que aporta
/// <c>Category</c> es que es la clave que el resto de las herramientas ya espera: es como agrupa el
/// Test Explorer y es a lo que mapea <c>TestCategory</c> en Azure DevOps. El valor sí va en español,
/// como todo lo demás.
/// </para>
/// <para>
/// Para qué sirve: <c>dotnet test --filter "Category!=Integracion"</c> deja fuera los tests que
/// necesitan la instancia SQL Server del entorno. Es la mitad de la regla que decide <b>desde
/// afuera</b>; la otra mitad —saltear con motivo cuando no hay cadena configurada— la decide el
/// fixture desde adentro.
/// </para>
/// </remarks>
public static class CategoriaDeTest
{
    /// <summary>Nombre del trait. El que entiende <c>dotnet test --filter</c>.</summary>
    public const string Clave = "Category";

    /// <summary>
    /// Tests que necesitan la instancia SQL Server 2022 del entorno. Son <b>exactamente</b> los que se
    /// saltean cuando no hay cadena de test configurada: las dos mitades de la regla de la spec hablan
    /// del mismo conjunto.
    /// </summary>
    /// <remarks>
    /// El criterio es «necesita el motor del entorno», no «abre un socket». Por eso quedan
    /// <b>fuera</b> los tests que apuntan a un puerto local muerto a propósito —B2-T8 y B2-T13—: lo
    /// que necesitan es la <i>ausencia</i> de un servidor, valen en cualquier máquina y excluirlos del
    /// filtro perdería cobertura justo donde no hay motor, que es donde más falta hace.
    /// </remarks>
    public const string Integracion = "Integracion";
}
