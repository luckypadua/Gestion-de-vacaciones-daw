using System.Text.Json;
using System.Xml.Linq;
using GestionVacaciones.Web.Identidad;
using Xunit;

namespace GestionVacaciones.Tests.Identidad;

/// <summary>
/// <b>Capa 1 de la corrección de R-01:</b> la segunda condición —la clave
/// <c>Vacaciones:PermitirIdentidadDeDesarrollo</c>— no puede viajar dentro del artefacto publicado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Qué se había roto.</b> El modelo de amenazas afirma que las dos condiciones de R-01 son
/// <i>independientes</i>, y no lo eran: la clave vivía en <c>appsettings.Development.json</c>, un
/// archivo que (a) solo se carga cuando ya se cumple la primera condición —el entorno—, con lo que no
/// aportaba nada nuevo, y (b) <b>se copiaba al publicado</b>. Con eso, un host productivo arrancado
/// con <c>ASPNETCORE_ENVIRONMENT=Development</c> mal puesta obtenía la segunda condición <i>gratis</i>:
/// el desplegable de identidades sin credencial, la nómina completa a la vista, <c>DetailedErrors</c>
/// activo y la cadena de conexión sin exigir cifrado. Una variable de entorno, no dos condiciones.
/// </para>
/// <para>
/// <b>Por qué <c>launchSettings.json</c> y no user-secrets.</b> Está versionado, así que un clon nuevo
/// conserva la experiencia de desarrollo sin pasos manuales —y un paso manual olvidado termina en
/// alguien poniendo la clave donde no va—, y <b>no se publica</b>: <c>Properties/</c> no existe en la
/// salida de <c>dotnet publish</c>. Es el archivo que ya cumplía ese papel para
/// <c>ASPNETCORE_ENVIRONMENT</c>.
/// </para>
/// </remarks>
public sealed class ClaveFueraDelArtefactoTests
{
    /// <summary>Grafía de la clave como variable de entorno: el doble guion bajo son los dos puntos.</summary>
    private const string VariableDeEntornoDeLaClave = "Vacaciones__PermitirIdentidadDeDesarrollo";

    private const string ArchivoDeDesarrollo = "appsettings.Development.json";

    [Fact]
    public async Task El_appsettings_de_desarrollo_versionado_no_declara_la_clave_de_identidad()
    {
        // El corazón del hallazgo. Este archivo viajaba en el publicado; mientras la clave esté acá, la
        // segunda condición de R-01 la aporta el propio artefacto.
        var archivo = Path.Combine(HostConIdentidad.RaizDelProyectoWeb(), ArchivoDeDesarrollo);

        using var documento = JsonDocument.Parse(
            await File.ReadAllTextAsync(archivo, TestContext.Current.CancellationToken));

        var tramos = VerificacionDeIdentidad.ClaveDeConfiguracion.Split(':');

        Assert.False(
            documento.RootElement.TryGetProperty(tramos[0], out var seccion)
                && seccion.TryGetProperty(tramos[1], out _),
            $"{ArchivoDeDesarrollo} volvió a declarar «{VerificacionDeIdentidad.ClaveDeConfiguracion}». " +
            "Ese archivo se copia al artefacto publicado, así que la clave deja de ser una condición " +
            "independiente del entorno: va en launchSettings.json, que no se publica.");
    }

    [Fact]
    public async Task El_launchSettings_versionado_aporta_la_clave_en_todo_perfil_de_desarrollo()
    {
        // La contracara imprescindible: sacar la clave sin ponerla en ningún lado rompería el entorno de
        // desarrollo de todo el mundo, y el próximo que clone el repositorio la devolvería al
        // appsettings. La condición se mueve, no se elimina.
        var archivo = Path.Combine(HostConIdentidad.RaizDelProyectoWeb(), "Properties", "launchSettings.json");

        using var documento = JsonDocument.Parse(
            await File.ReadAllTextAsync(archivo, TestContext.Current.CancellationToken));

        var perfiles = documento.RootElement.GetProperty("profiles").EnumerateObject().ToList();

        Assert.NotEmpty(perfiles);

        foreach (var perfil in perfiles)
        {
            var variables = perfil.Value.GetProperty("environmentVariables");

            // Solo se exige en los perfiles que además declaran el entorno de desarrollo: un perfil que
            // apuntara a otro entorno no debe traer la clave.
            if (variables.GetProperty(HostConIdentidad.VariableDeEntornoDeAspNet).GetString()
                != HostConIdentidad.EntornoDeDesarrollo)
            {
                continue;
            }

            Assert.True(
                variables.TryGetProperty(VariableDeEntornoDeLaClave, out var valor),
                $"El perfil «{perfil.Name}» declara el entorno de desarrollo pero no la clave de " +
                "identidad de desarrollo: quien lo use no va a tener selector de empleado.");

            Assert.Equal("true", valor.GetString());
        }
    }

    [Fact]
    public async Task El_proyecto_Web_excluye_del_publicado_el_appsettings_de_desarrollo()
    {
        // Segunda barrera de la misma capa. La primera es que la clave ya no esté en el archivo; esta es
        // que el archivo entero no viaje. Sirve para lo que se agregue mañana: cualquier ajuste de
        // desarrollo que alguien escriba ahí —DetailedErrors, un endpoint de diagnóstico— queda fuera
        // del artefacto sin que haga falta acordarse de esta discusión.
        var proyecto = Path.Combine(
            HostConIdentidad.RaizDelProyectoWeb(), "GestionVacaciones.Web.csproj");

        var documento = XDocument.Parse(await File.ReadAllTextAsync(proyecto, TestContext.Current.CancellationToken));

        var exclusion = documento.Descendants("Content")
            .FirstOrDefault(elemento => string.Equals(
                elemento.Attribute("Update")?.Value, ArchivoDeDesarrollo, StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(exclusion);
        Assert.Equal("Never", exclusion.Attribute("CopyToPublishDirectory")?.Value);
    }
}
