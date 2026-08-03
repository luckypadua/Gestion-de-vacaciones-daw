namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// <see cref="TimeProvider"/> detenido en un instante concreto. Es lo que vuelve verificable la regla
/// de AC-02 —«la fecha de inicio no puede ser anterior a hoy»— sin depender del reloj de la máquina.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué se escribe a mano y no se agrega <c>Microsoft.Extensions.TimeProvider.Testing</c>.</b> La
/// spec fija ocho dependencias con versión exacta y exige justificar cualquier otra; lo que hace falta
/// acá son tres miembros sobreescritos, así que sumar un paquete al grafo de la cadena de suministro
/// (W-TM-01) por eso no se justifica.
/// </para>
/// <para>
/// <b>Se sobreescribe también <see cref="LocalTimeZone"/>, y no solo <see cref="GetUtcNow"/>.</b> El
/// «hoy» del dominio es el local —quien pide vacaciones vive en un huso, no en UTC— y la
/// implementación por defecto de <c>GetLocalNow()</c> convierte el instante usando esta propiedad. Sin
/// fijarla, el test seguiría dependiendo del huso de la máquina: los mismos datos darían un «hoy»
/// distinto en la máquina de desarrollo y en un CI configurado en UTC.
/// </para>
/// </remarks>
internal sealed class TiempoFijo : TimeProvider
{
    private readonly DateTimeOffset _ahora;

    public TiempoFijo(DateTimeOffset ahora)
    {
        _ahora = ahora;

        // Huso con el desplazamiento del propio instante: así GetLocalNow() devuelve exactamente
        // `ahora`, y «hoy» es la fecha que el test escribió y no la que resulte de una conversión.
        LocalTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            id: "TiempoFijoDeTest",
            baseUtcOffset: ahora.Offset,
            displayName: "Tiempo fijo de test",
            standardDisplayName: "Tiempo fijo de test");
    }

    public override TimeZoneInfo LocalTimeZone { get; }

    /// <summary>«Hoy» del dominio, según este reloj detenido.</summary>
    public DateOnly Hoy => DateOnly.FromDateTime(_ahora.DateTime);

    /// <summary>El instante en el que está detenido, tal como se escribió: con su desplazamiento.</summary>
    public DateTimeOffset Ahora => _ahora;

    public override DateTimeOffset GetUtcNow() => _ahora.ToUniversalTime();
}
