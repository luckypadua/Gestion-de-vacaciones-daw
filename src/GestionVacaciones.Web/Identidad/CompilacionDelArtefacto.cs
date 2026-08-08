namespace GestionVacaciones.Web.Identidad;

/// <summary>
/// Cómo se compiló <b>este</b> ensamblado. Es la tercera condición de la mitigación de
/// <b>R-01 (CRITICAL)</b>, y la única que no sale de la configuración.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué hace falta.</b> Las otras dos condiciones —el entorno <c>Development</c> y la clave
/// <c>Vacaciones:PermitirIdentidadDeDesarrollo</c>— salen las dos de la configuración, así que las
/// controla quien controla el entorno de ejecución: dos variables exportadas y el sustituto de
/// identidad sin credencial queda activo. Esta condición no se puede exportar. Se decide al compilar,
/// y lo que se despliega se compila en <c>Release</c>.
/// </para>
/// <para>
/// <b>La contrapartida es intencional y conviene conocerla:</b> correr la aplicación localmente en
/// <c>Release</c> —<c>dotnet run -c Release</c>, o el publicado sobre la propia máquina— <b>no ofrece
/// el selector de empleado</b> ni siquiera con el entorno y la clave puestos. No es un defecto: es
/// exactamente lo que este archivo existe para garantizar. Para desarrollar con selector se compila en
/// <c>Debug</c>, que es el valor por defecto de <c>dotnet run</c> y de la depuración del IDE.
/// </para>
/// <para>
/// <b>Este es el único lugar del proyecto Web con una directiva de compilación condicional</b>, y lo
/// fija un test (<c>GuardarrailDeCompilacionTests</c>). Un segundo <c>#if</c> en cualquier otro
/// archivo sería una decisión de compilación que ningún test puede observar —la suite corre en
/// <c>Debug</c>— y por lo tanto un agujero del mismo tipo que el que esto cierra.
/// </para>
/// </remarks>
public static class CompilacionDelArtefacto
{
    /// <summary>
    /// ¿Se compiló este ensamblado con el símbolo <c>DEBUG</c>?
    /// </summary>
    /// <remarks>
    /// Es una propiedad con inicializador y <b>no</b> una <c>const</c>: el valor de una constante se
    /// incrusta en cada ensamblado que la lee <i>al compilarlo</i>, así que un consumidor compilado
    /// aparte podría quedar afirmando lo contrario de lo que dice el ensamblado que de verdad se
    /// despliega. Así se lee en tiempo de ejecución, del artefacto, que es de lo que se habla.
    /// </remarks>
    public static bool EsDeDepuracion { get; } =
#if DEBUG
        true;
#else
        false;
#endif
}
