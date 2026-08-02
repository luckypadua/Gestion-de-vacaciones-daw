using System.Reflection;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// La categoría de integración que exige la spec —«los tests que tocan la base llevan <c>Trait</c> de
/// categoría»— marca <b>exactamente</b> los tests que necesitan la instancia SQL Server del entorno.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué esto se comprueba y no se confía.</b> <c>[Collection]</c> no sustituye a la categoría:
/// xUnit no expone las colecciones como traits, así que <c>--filter Category!=Integracion</c> no las
/// alcanza. Y una categoría puesta a mano en cada clase se olvida en la siguiente: el test que se
/// agregue mañana a <see cref="ColeccionDeBaseDeDatos"/> sin marcar quedaría dentro del filtro y
/// rompería la corrida en cualquier máquina sin motor, que es justo lo que la categoría evita.
/// </para>
/// <para>
/// La comprobación es en los <b>dos</b> sentidos. Que falte la marca deja un test de base corriendo
/// donde no hay base; que sobre la marca deja fuera del filtro un test que no necesita motor y que
/// por lo tanto debería correr siempre —el del rango del enum es el caso—, y esa pérdida de cobertura
/// no la delata nadie porque la suite queda verde.
/// </para>
/// <para>
/// <b>Cómo se decide quién usa la base.</b> Por el <b>tipo del fixture</b>: se resuelve a qué clase
/// de definición pertenece cada test y se pregunta si esa definición provee
/// <see cref="BaseDeDatosDeTest"/>. No por el nombre del atributo, porque xunit.v3 acepta tres
/// grafías de la misma colección —<c>[Collection("nombre")]</c>, <c>[Collection(typeof(T))]</c> y
/// <c>[Collection&lt;T&gt;]</c>— y reconocer una sola dejaba a las otras dos fuera de las <b>dos</b>
/// listas: sin categoría tampoco entran en la otra, y el olvido pasaba en verde.
/// </para>
/// <para>
/// <b>Hasta dónde llega.</b> El conjunto de referencia son las clases que reciben el fixture de
/// <see cref="ColeccionDeBaseDeDatos"/>, que es por donde xUnit lo inyecta: no hay otra forma de
/// obtenerlo. Un test que se armara el contexto por su cuenta con
/// <c>BaseDeDatosDeTest.CrearContexto(cadena)</c> quedaría fuera de esta comprobación —el
/// guardarraíl del catálogo descartable, que esa primitiva aplica, es lo que lo contiene—.
/// </para>
/// </remarks>
public sealed class CategoriaDeIntegracionTests
{
    [Fact]
    public void La_categoria_de_integracion_marca_exactamente_los_tests_que_usan_la_base()
    {
        var universo = ClasesQueElRunnerDescubre();
        var queUsanLaBase = TestsQueUsanLaBase(universo);
        var marcados = TestsConLaCategoria(universo, CategoriaDeTest.Integracion);

        // Sin esta afirmación el test pasaría en verde por no haber encontrado nada que revisar: dos
        // listas vacías son iguales.
        Assert.NotEmpty(queUsanLaBase);

        Assert.Equal(queUsanLaBase, marcados);
    }

    [Fact]
    public void Las_tres_grafias_del_atributo_de_coleccion_cuentan_como_uso_de_la_base()
    {
        // xunit.v3 acepta tres formas de escribir la MISMA colección y las tres reciben el fixture
        // igual. Reconocer solo una es peor que no comprobar nada: la clase escrita con otra grafía
        // queda fuera de esta lista y, si además se olvidan la categoría, tampoco está en la otra
        // —queda fuera de las DOS— y la suite se queda verde justo en el olvido que este test existe
        // para delatar.
        (Type Sonda, string Grafia)[] sondas =
        [
            (typeof(SondaEnLaColeccionPorNombre), "[Collection(\"nombre\")]"),
            (typeof(SondaEnLaColeccionPorTipo), "[Collection(typeof(T))]"),
            (typeof(SondaEnLaColeccionGenerica), "[Collection<T>]"),
        ];

        foreach (var (sonda, grafia) in sondas)
        {
            // Ninguna sonda lleva categoría: es la mitad que vuelve invisible al olvido.
            Assert.Empty(Traits(sonda));

            Assert.True(
                UsaLaBaseDeDatos(sonda),
                $"El guardarraíl no reconoce {grafia} como pertenencia a la colección de la base. " +
                "xunit.v3 le inyecta el fixture igual, así que una clase escrita así y sin categoría " +
                "no aparecería en ninguna de las dos listas y nadie la delataría.");
        }
    }

    [Fact]
    public void Un_test_heredado_de_una_clase_base_no_queda_invisible()
    {
        // Hoy ninguna clase de la suite hereda sus casos, pero el día que aparezca una clase base de
        // tests, sus casos tienen que seguir contando y atribuidos a la clase CONCRETA, que es la que
        // lleva la colección y la categoría. Un helper que solo mire los métodos declarados los
        // pierde a todos sin decir nada.
        var universo = new[] { typeof(SondaQueHeredaSuCaso) };

        Assert.Equal(["SondaQueHeredaSuCaso.Caso_de_la_clase_base"], TestsQueUsanLaBase(universo));
    }

    /// <summary>Tests de las clases del universo que reciben el fixture de la base.</summary>
    private static IReadOnlyList<string> TestsQueUsanLaBase(IEnumerable<Type> universo) =>
        MetodosDeTest(universo)
            .Where(metodo => UsaLaBaseDeDatos(metodo.ReflectedType!))
            .Select(Describir)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Tests que declaran la categoría, sea en el método o en su clase.</summary>
    private static IReadOnlyList<string> TestsConLaCategoria(IEnumerable<Type> universo, string categoria) =>
        MetodosDeTest(universo)
            .Where(metodo => CategoriasDe(metodo).Contains(categoria, StringComparer.Ordinal))
            .Select(Describir)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Las clases que el runner descubre como tests: públicas y concretas. La visibilidad no es un
    /// detalle caprichoso: el analizador xUnit1000 rechaza un <c>[Fact]</c> en una clase que no sea
    /// pública, así que una clase no visible nunca es un test de esta suite. Es lo que permite que
    /// las sondas de más abajo sirvan de entrada al guardarraíl sin convertirse en tests.
    /// </summary>
    private static IEnumerable<Type> ClasesQueElRunnerDescubre() =>
        typeof(CategoriaDeIntegracionTests).Assembly
            .GetTypes()
            .Where(tipo => tipo is { IsClass: true, IsAbstract: false, IsVisible: true });

    /// <summary>
    /// ¿La clase recibe el fixture <see cref="BaseDeDatosDeTest"/>? Se resuelve por el <b>tipo del
    /// fixture</b>, no por el nombre del atributo: xunit.v3 acepta tres grafías para entrar en la
    /// misma colección —<c>[Collection("nombre")]</c>, <c>[Collection(typeof(T))]</c> y
    /// <c>[Collection&lt;T&gt;]</c>— y las tres inyectan el fixture igual. Comparar el nombre del
    /// atributo solo reconocía la primera; llegar hasta el fixture reconoce las tres y sobrevive
    /// además a que alguien renombre la constante de la colección.
    /// </summary>
    private static bool UsaLaBaseDeDatos(Type tipo) =>
        DefinicionDeLaColeccion(tipo) is { } definicion &&
        definicion.GetInterfaces().Any(interfaz =>
            interfaz.IsGenericType &&
            interfaz.GetGenericTypeDefinition() == typeof(ICollectionFixture<>) &&
            interfaz.GenericTypeArguments[0] == typeof(BaseDeDatosDeTest));

    /// <summary>
    /// Sin <c>DeclaredOnly</c>: un caso heredado de una clase base es un test que el runner ejecuta,
    /// y dejarlo invisible acá lo sacaría de las dos listas. Por eso la clase que cuenta es
    /// <see cref="MemberInfo.ReflectedType"/> —la concreta, la que lleva la colección y la
    /// categoría— y no <c>DeclaringType</c>, que sería la base.
    /// </summary>
    private static IEnumerable<MethodInfo> MetodosDeTest(IEnumerable<Type> universo) =>
        universo
            .SelectMany(tipo => tipo.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            // TheoryAttribute deriva de FactAttribute, así que las teorías entran por el mismo camino.
            .Where(metodo => metodo.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0);

    private static IEnumerable<string> CategoriasDe(MethodInfo metodo) =>
        Traits(metodo)
            .Concat(ConSusBases(metodo.ReflectedType).SelectMany(Traits))
            .Where(par => string.Equals(par.Key, CategoriaDeTest.Clave, StringComparison.Ordinal))
            .Select(par => par.Value);

    /// <summary>
    /// Se leen los argumentos del constructor con <see cref="CustomAttributeData"/> y no las
    /// propiedades del atributo: así la comprobación no depende de qué expone <c>TraitAttribute</c> en
    /// la versión de xUnit del día, que es detalle interno del framework.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> Traits(MemberInfo miembro) =>
        miembro.GetCustomAttributesData()
            .Where(dato => dato.AttributeType == typeof(TraitAttribute))
            .Select(dato => new KeyValuePair<string, string>(
                (string)dato.ConstructorArguments[0].Value!,
                (string)dato.ConstructorArguments[1].Value!));

    /// <summary>
    /// Clase que define la colección a la que pertenece <paramref name="tipo"/>, o <c>null</c> si no
    /// está en ninguna. Se mira también la cadena de bases porque los atributos de clase de xUnit se
    /// heredan y <see cref="MemberInfo.GetCustomAttributesData"/> no los trae heredados.
    /// </summary>
    private static Type? DefinicionDeLaColeccion(Type? tipo) =>
        ConSusBases(tipo).Select(DefinicionDeclaradaEn).FirstOrDefault(definicion => definicion is not null);

    private static Type? DefinicionDeclaradaEn(Type tipo)
    {
        foreach (var dato in tipo.GetCustomAttributesData())
        {
            // [Collection<T>] es otro atributo, CollectionAttribute<T>: la definición es su
            // argumento genérico y no aparece entre los argumentos del constructor.
            if (dato.AttributeType.IsGenericType &&
                dato.AttributeType.GetGenericTypeDefinition() == typeof(CollectionAttribute<>))
            {
                return dato.AttributeType.GenericTypeArguments[0];
            }

            if (dato.AttributeType != typeof(CollectionAttribute) || dato.ConstructorArguments.Count != 1)
            {
                continue;
            }

            // [Collection(typeof(T))] trae el tipo directo; [Collection("nombre")] deja en los
            // metadatos solo el nombre, así que hay que buscar quién define esa colección.
            return dato.ConstructorArguments[0].Value switch
            {
                Type porTipo => porTipo,
                string porNombre => DefinicionLlamada(porNombre),
                _ => null,
            };
        }

        return null;
    }

    private static Type? DefinicionLlamada(string nombre) =>
        typeof(CategoriaDeIntegracionTests).Assembly
            .GetTypes()
            .FirstOrDefault(tipo => string.Equals(NombreDeLaDefinicion(tipo), nombre, StringComparison.Ordinal));

    private static string? NombreDeLaDefinicion(Type tipo) =>
        tipo.GetCustomAttributesData()
            .Where(dato => dato.AttributeType == typeof(CollectionDefinitionAttribute))
            // Sin nombre explícito, xUnit nombra la colección con el nombre completo del tipo.
            .Select(dato => dato.ConstructorArguments.Count == 1
                ? dato.ConstructorArguments[0].Value as string
                : tipo.FullName)
            .FirstOrDefault();

    /// <summary>La clase y sus bases, de la más derivada a la más general.</summary>
    private static IEnumerable<Type> ConSusBases(Type? tipo)
    {
        for (var actual = tipo; actual is not null && actual != typeof(object); actual = actual.BaseType)
        {
            yield return actual;
        }
    }

    private static string Describir(MethodInfo metodo) => $"{metodo.ReflectedType!.Name}.{metodo.Name}";

    /// <summary>
    /// Clase base con un caso, para la sonda de herencia. Es <c>public</c> porque el analizador
    /// xUnit1000 rechaza un <c>[Fact]</c> en una clase no pública, y <c>abstract</c> para que el
    /// runner no la descubra: las sondas no son tests de esta suite, son entrada del guardarraíl.
    /// </summary>
    public abstract class SondaConCasoHeredado
    {
        [Fact]
        public void Caso_de_la_clase_base()
        {
        }
    }

    // Las tres grafías de la MISMA colección, tal como xunit.v3 las acepta. Son privadas —el runner
    // solo descubre clases públicas— y no declaran casos propios, así que no agregan tests a la
    // suite: existen únicamente para que el guardarraíl tenga contra qué ejercitarse.
    [Collection(ColeccionDeBaseDeDatos.Nombre)]
    private sealed class SondaEnLaColeccionPorNombre;

    [Collection(typeof(ColeccionDeBaseDeDatos))]
    private sealed class SondaEnLaColeccionPorTipo;

    [Collection<ColeccionDeBaseDeDatos>]
    private sealed class SondaEnLaColeccionGenerica;

    /// <summary>Sonda de herencia: la colección la declara ella, el caso lo pone su clase base.</summary>
    [Collection(ColeccionDeBaseDeDatos.Nombre)]
    private sealed class SondaQueHeredaSuCaso : SondaConCasoHeredado;
}
