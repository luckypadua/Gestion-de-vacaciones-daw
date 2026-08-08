using Xunit;

namespace GestionVacaciones.Tests.Andamiaje;

/// <summary>
/// Cuando el circuito se cae, <c>blazor.web.js</c> busca el elemento <c>#blazor-error-ui</c> y lo
/// hace visible. Si no existe en el marcado, el usuario no ve nada: la aplicación queda muda y él
/// cree que sigue funcionando. El estilo sin el elemento es CSS muerto; el elemento sin el estilo,
/// un aviso que no se distingue del contenido.
/// </summary>
public sealed class RetroalimentacionDeCircuitoCaidoTests
{
    [Fact]
    public void El_marcado_declara_el_contenedor_de_error_de_circuito_que_el_CSS_estiliza()
    {
        var raiz = RaizDelRepositorio.Localizar();

        var marcado = File.ReadAllText(
            Path.Combine(raiz, "src", "GestionVacaciones.Web", "Components", "App.razor"));
        var estilos = File.ReadAllText(
            Path.Combine(raiz, "src", "GestionVacaciones.Web", "wwwroot", "app.css"));

        Assert.Contains("id=\"blazor-error-ui\"", marcado, StringComparison.Ordinal);
        Assert.Contains("#blazor-error-ui", estilos, StringComparison.Ordinal);
    }
}
