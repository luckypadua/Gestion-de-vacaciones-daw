using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace GestionVacaciones.Tests.Componentes;

/// <summary>
/// Base de los tests de componentes del Bloque 6: un <see cref="BunitContext"/> con lo mínimo que
/// necesita cualquier marcado construido con MudBlazor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué una base y no tres líneas repetidas en cada clase.</b> Las tres son condiciones para que
/// el componente <i>se renderice</i>, no para lo que cada test afirma: olvidada cualquiera de ellas, el
/// fallo no es un aserto en rojo sino una excepción del renderizador, que manda a diagnosticar el test
/// en vez del componente. Escritas una sola vez, ningún test futuro puede olvidarlas.
/// </para>
/// <para>
/// <b>bUnit no abre ningún navegador</b> (NFR-03): renderiza el árbol de componentes en memoria. Lo que
/// estos tests verifican es el marcado que produce cada componente y a qué servicios llama, no cómo se
/// ve. La compatibilidad con Chrome, Edge y Firefox queda como verificación manual, tal como la spec lo
/// declara.
/// </para>
/// </remarks>
public abstract class ContextoDeComponentes : BunitContext
{
    protected ContextoDeComponentes()
    {
        // MudBlazor resuelve por contenedor sus propios servicios —el de popovers, el de teclado, el
        // de scroll—. Sin ellos, un MudSelect o un MudDatePicker revientan al activarse.
        Services.AddMudServices();

        // Los componentes registran los fallos que atrapan (mitigación R-12: solo EmpleadoId). Sin
        // ILogger registrado, la inyección falla antes de que ningún aserto llegue a correr.
        Services.AddLogging();

        // Los componentes de MudBlazor llaman a JS para posicionar popovers y medir elementos. En modo
        // estricto —el de por defecto— cada llamada no declarada lanza; acá no se afirma nada sobre JS.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }
}
