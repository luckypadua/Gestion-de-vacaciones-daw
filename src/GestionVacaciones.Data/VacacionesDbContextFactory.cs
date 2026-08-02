using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GestionVacaciones.Data;

/// <summary>
/// Fábrica de tiempo de diseño para <c>dotnet ef</c>. <b>Es necesaria:</b> el contexto se registra
/// con <c>AddDbContextFactory</c> y nunca con <c>AddDbContext</c> (NFR-05), así que las herramientas
/// no lo encuentran por el camino habitual de inspeccionar los servicios del host.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué no hay cadena de conexión por defecto.</b> Generar una migración no abre ninguna
/// conexión: al proveedor de SQL Server le alcanza con conocer sus propias reglas de tipos. Por eso,
/// si no hay cadena en el entorno se llama a <c>UseSqlServer()</c> sin argumentos. Es lo contrario
/// de inventar un valor: quien ejecute <c>dotnet ef database update</c> sin definir
/// <c>VACACIONES_CONNECTION</c> recibe un fallo explícito de SqlClient —«The ConnectionString
/// property has not been initialized.»— en vez de aplicar migraciones contra una base adivinada. El
/// repositorio es público y una cadena escrita acá sería una credencial publicada de forma
/// permanente (R-02).
/// </para>
/// <para>
/// <b>Lo que esta clase NO hace, para que nadie lo dé por sentado.</b> La variable que lee,
/// <c>VACACIONES_CONNECTION</c>, es <b>la misma</b> de la que la aplicación toma su cadena fuera de
/// <c>Development</c> (<c>CadenaDeConexion</c>, en el proyecto Web). Acá <b>no hay separación de
/// credenciales</b>: si quien corre el comando exporta la variable con la cuenta de la aplicación,
/// las migraciones se aplican con esa cuenta. Que se apliquen con una distinta y de mayor privilegio
/// (mitigación R-07) es hoy una práctica del operador —exportar la variable con la credencial de
/// migraciones en la sesión donde corre <c>dotnet ef</c>—, no algo que el código imponga ni pueda
/// comprobar.
/// </para>
/// <para>
/// Tampoco se leen los user-secrets del proyecto Web, y no por elección de seguridad: <c>Data</c> no
/// referencia a <c>Web</c> —la dependencia va al revés— y no tiene manera de conocer su
/// <c>UserSecretsId</c>. El efecto útil es que en desarrollo la cadena de las migraciones hay que
/// ponerla a mano en el entorno, que es un acto deliberado y no la que la aplicación arrastra sin que
/// nadie lo piense.
/// </para>
/// </remarks>
public sealed class VacacionesDbContextFactory : IDesignTimeDbContextFactory<VacacionesDbContext>
{
    /// <summary>
    /// Variable de entorno de la que se toma la cadena, si está definida. Es la misma que consume la
    /// aplicación: un test ata las dos constantes para que renombrar una sola no deje a
    /// <c>dotnet ef</c> leyendo una variable que nadie define.
    /// </summary>
    public const string VariableDeEntorno = "VACACIONES_CONNECTION";

    public VacacionesDbContext CreateDbContext(string[] args)
    {
        var constructor = new DbContextOptionsBuilder<VacacionesDbContext>();
        var cadena = Environment.GetEnvironmentVariable(VariableDeEntorno);

        if (string.IsNullOrWhiteSpace(cadena))
        {
            constructor.UseSqlServer();
        }
        else
        {
            constructor.UseSqlServer(cadena);
        }

        return new VacacionesDbContext(constructor.Options);
    }
}
