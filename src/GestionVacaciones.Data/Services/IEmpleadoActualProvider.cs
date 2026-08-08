namespace GestionVacaciones.Data.Services;

/// <summary>
/// Identidad del empleado actual, con <b>dos</b> estados y ninguno más: hay un empleado seleccionado,
/// o todavía no hay ninguno. No existe un tercer estado silencioso.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué es un tipo y no un <c>int?</c>.</b> «Todavía no hay empleado seleccionado» es un estado
/// legítimo del circuito —recién abierto, antes de que alguien elija— y tiene que ser
/// <b>distinguible</b> de «no tenés solicitudes» y de «algo falló»: las tres se ven parecidas en
/// pantalla y significan cosas distintas. Un <c>null</c> devuelto en silencio las confunde, y un
/// <c>0</c> es peor todavía, porque se lee como un empleado válido y por lo tanto como «cualquiera».
/// </para>
/// <para>
/// El tipo se construye solo por <see cref="De"/> —que rechaza los identificadores no positivos, que
/// ninguna clave <c>identity</c> produce— o por <see cref="SinSeleccionar"/>. Es el mismo carácter de
/// guardarraíl que <c>CatalogosSembrables</c> aplica en el Bloque 3: sin ningún valor que signifique
/// «cualquiera».
/// </para>
/// </remarks>
public sealed class IdentidadDelEmpleado
{
    private readonly int? _id;

    private IdentidadDelEmpleado(int? id) => _id = id;

    /// <summary>El circuito todavía no eligió a nadie. Es el estado inicial y es explícito.</summary>
    public static IdentidadDelEmpleado SinSeleccionar { get; } = new(null);

    /// <summary>¿Hay un empleado seleccionado? Lo que hay que preguntar antes de pedir el <see cref="Id"/>.</summary>
    public bool HayEmpleadoSeleccionado => _id is not null;

    /// <summary>Identificador del empleado seleccionado.</summary>
    /// <exception cref="SinEmpleadoSeleccionadoException">
    /// Si todavía no hay ninguno. Lanza en vez de devolver <c>0</c>: un cero se persistiría como autor
    /// de una solicitud y quedaría atribuida a un empleado que no existe.
    /// </exception>
    public int Id => _id ?? throw new SinEmpleadoSeleccionadoException();

    /// <summary>Identidad de un empleado concreto.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Si <paramref name="empleadoId"/> no es positivo. La clave es <c>identity</c> y arranca en 1, así
    /// que un 0 o un negativo no identifican a nadie: aceptarlos crearía la identidad fantasma que el
    /// estado explícito de <see cref="SinSeleccionar"/> existe para evitar.
    /// </exception>
    public static IdentidadDelEmpleado De(int empleadoId)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(empleadoId);

        return new IdentidadDelEmpleado(empleadoId);
    }

    /// <summary>
    /// Descripción para diagnóstico. Lleva el identificador y <b>nunca</b> nombre ni correo, que son
    /// PII (R-12): esto termina en un log o en un mensaje de error.
    /// </summary>
    public override string ToString() =>
        _id is null
            ? "IdentidadDelEmpleado { sin empleado seleccionado }"
            : $"IdentidadDelEmpleado {{ Id = {_id} }}";
}

/// <summary>Qué pasó al intentar cambiar el empleado actual.</summary>
public enum ResultadoDeSeleccion
{
    /// <summary>El empleado existía en la nómina y quedó como identidad del circuito.</summary>
    Seleccionado,

    /// <summary>
    /// El identificador recibido no existe en la nómina: se rechaza el cambio y se conserva el empleado
    /// anterior. No se persiste nada.
    /// </summary>
    RechazadaPorEmpleadoInexistente,
}

/// <summary>
/// Se pidió el empleado actual y todavía no hay ninguno seleccionado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué tiene su propio tipo.</b> Es lo que vuelve el estado no solo explícito sino
/// <b>distinguible</b>: «todavía no elegiste a nadie» —mostrale el selector— y «esta aplicación no
/// tiene identidad configurada» —AC-06, no debería estar corriendo así— son dos situaciones opuestas
/// que no entregan empleado. Compartiendo tipo de excepción, el único modo de separarlas sería mirar
/// el texto del mensaje, que es exactamente cómo se terminan tratando igual.
/// </para>
/// <para>
/// Deriva de <see cref="InvalidOperationException"/> porque eso es lo que es —una operación pedida en
/// un estado que no la admite— y porque así quien no necesite distinguirlas puede seguir capturando la
/// general.
/// </para>
/// </remarks>
public sealed class SinEmpleadoSeleccionadoException : InvalidOperationException
{
    /// <summary>
    /// Mensaje por defecto. No nombra RF-01: acá no hay nada mal configurado, solo un circuito que
    /// todavía no eligió.
    /// </summary>
    public const string MensajePorDefecto =
        "Todavía no hay ningún empleado seleccionado en este circuito. Mostrá el selector de empleado " +
        "en lugar de un listado vacío: un listado vacío se leería como «no enviaste solicitudes».";

    /// <summary>
    /// El <b>único</b> constructor, y a propósito: de los tres canónicos de una excepción —el sin
    /// parámetros, el que recibe mensaje y el que recibe mensaje y excepción interna— este es el sin
    /// parámetros, y los otros dos no tenían ningún llamador. Un estado que solo puede ocurrir por un
    /// motivo no necesita que cada sitio redacte su propio texto: el mensaje de
    /// <see cref="MensajePorDefecto"/> es el que corresponde siempre, y tenerlo en un solo lugar es lo
    /// que hace que la interfaz pueda distinguir este caso sin leer cadenas.
    /// </summary>
    public SinEmpleadoSeleccionadoException()
        : base(MensajePorDefecto)
    {
    }
}

/// <summary>
/// <b>Única</b> sede de la resolución de la identidad del empleado actual (FR-05, NFR-06).
/// </summary>
/// <remarks>
/// <para>
/// Todo el que necesite saber de quién es una solicitud —el alta, el listado, la interfaz— pasa por
/// acá. NFR-06 lo cuenta explícitamente: «1 única interfaz, 0 llamadores que obtengan el empleado por
/// otra vía». El día que llegue RF-01 (autenticación OAuth), se reemplaza la implementación y ningún
/// llamador se toca.
/// </para>
/// <para>
/// <b>Por qué <see cref="SeleccionarAsync"/> vive en la misma interfaz.</b> Es una tensión conocida:
/// con OAuth la identidad no se «elige», así que ese miembro quedará sin sentido. La alternativa
/// —una segunda interfaz para el selector— crearía una segunda sede de la identidad, que es
/// literalmente lo que NFR-06 cuenta en cero. Se prefiere la tensión de diseño, que está escrita acá,
/// a la vía alternativa, que nadie ve.
/// </para>
/// <para>
/// Las implementaciones se registran <b>scoped</b>: en Blazor Server, una por circuito. Viven en el
/// proyecto Web, que es el que conoce el entorno y la configuración que deciden cuál corresponde; este
/// proyecto no las nombra, ni siquiera en un comentario, porque no puede referenciarlas.
/// </para>
/// </remarks>
public interface IEmpleadoActualProvider
{
    /// <summary>
    /// ¿Puede el circuito <b>elegir</b> de quién es la identidad? Es la pregunta que hace la interfaz
    /// para decidir si muestra el selector de empleado, y el <b>único</b> miembro que contesta en vez de
    /// negar cuando la identidad no está configurada: <b>nunca lanza</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Por qué existe.</b> Sin este miembro, un componente que necesite saber si corresponde ofrecer
    /// la elección tiene tres caminos y los tres abren una segunda sede de la identidad, que es
    /// literalmente lo que NFR-06 cuenta en cero: nombrar la implementación concreta, llamar a
    /// <see cref="SeleccionarAsync"/> y capturar la excepción —control de flujo por excepción, y
    /// <c>AGENTS.md</c> prohíbe el catch silencioso—, o releer el entorno y la clave de configuración
    /// desde la vista, con lo que la decisión de R-01 quedaría escrita en dos lugares y el segundo lejos
    /// del primero. Preguntarle al propio proveedor mantiene la decisión en un punto único.
    /// </para>
    /// <para>
    /// Es una <b>capacidad</b>, no un estado: contesta lo mismo antes y después de que alguien elija. «Se
    /// puede elegir» y «ya se eligió» son preguntas distintas, y la segunda la contesta
    /// <see cref="IdentidadDelEmpleado.HayEmpleadoSeleccionado"/>.
    /// </para>
    /// <para>
    /// Con RF-01 (autenticación OAuth) la identidad no se elige, así que la implementación que venga
    /// contestará <c>false</c> y el selector desaparecerá sin tocar ningún componente. Es la misma
    /// tensión que <see cref="SeleccionarAsync"/> ya declara, resuelta del mismo modo.
    /// </para>
    /// </remarks>
    bool PermiteElegirEmpleado { get; }

    /// <summary>
    /// Identidad del empleado actual. Nunca <c>null</c>: si no hay ninguno seleccionado, lo dice
    /// (<see cref="IdentidadDelEmpleado.HayEmpleadoSeleccionado"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// En los entornos donde la identidad de desarrollo no está habilitada, resolver el empleado actual
    /// lanza en lugar de devolver uno por defecto (AC-06).
    /// </exception>
    IdentidadDelEmpleado Identidad { get; }

    /// <summary>
    /// Cambia el empleado actual del circuito, si <paramref name="empleadoId"/> existe en la nómina.
    /// </summary>
    /// <remarks>
    /// El identificador llega del navegador (frontera TB-1) y es <b>entrada no confiable</b>: que el
    /// desplegable solo ofrezca valores válidos es una propiedad de la interfaz, no un control.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// En los entornos donde la identidad de desarrollo no está habilitada (AC-06).
    /// </exception>
    Task<ResultadoDeSeleccion> SeleccionarAsync(int empleadoId, CancellationToken cancelacion = default);

    /// <summary>
    /// Se dispara cuando <see cref="Identidad"/> cambia de verdad (un <see cref="SeleccionarAsync"/>
    /// exitoso). Sin datos en el payload a propósito (<c>EventHandler</c>, no
    /// <c>EventHandler&lt;int&gt;</c>): quien se suscribe vuelve a preguntar <see cref="Identidad"/>,
    /// la única sede de la verdad (NFR-06), en vez de confiar en lo que el evento le pase.
    /// </summary>
    /// <remarks>
    /// Existe para que un componente que <b>no</b> es ancestro de quien elige la identidad —como
    /// <c>MainLayout</c>, que envuelve <c>@Body</c> en vez de vivir dentro de él— pueda enterarse de
    /// un cambio sin convertirse en uno (FIX-001). Antes de este evento, un componente así solo podía
    /// preguntar una vez, al arrancar, y quedaba desactualizado el resto del circuito.
    /// </remarks>
    event EventHandler? IdentidadCambiada;
}
