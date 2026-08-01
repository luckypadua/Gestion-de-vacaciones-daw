namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// Fija una variable de entorno durante el alcance de un test y restaura el valor previo al salir.
/// Las variables de entorno son estado global del proceso: sin restaurarlas, un test le cambia la
/// configuración al siguiente.
/// </summary>
internal sealed class VariableDeEntornoTemporal : IDisposable
{
    private readonly string _nombre;
    private readonly string? _valorPrevio;

    public VariableDeEntornoTemporal(string nombre, string? valor)
    {
        _nombre = nombre;
        _valorPrevio = Environment.GetEnvironmentVariable(nombre);
        Environment.SetEnvironmentVariable(nombre, valor);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(_nombre, _valorPrevio);
}
