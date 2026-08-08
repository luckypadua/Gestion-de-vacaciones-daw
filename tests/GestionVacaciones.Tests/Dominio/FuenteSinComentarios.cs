namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// El código de un archivo fuente <b>sin sus comentarios</b>: el criterio que comparten los escaneos
/// estructurales de este proyecto.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué descartar los comentarios es parte del criterio y no una comodidad.</b> Documentar lo que
/// el escaneo persigue es legítimo y necesario —<c>TopeAnual</c> explica de dónde sale el número,
/// <c>SaldoService</c> cita el límite de años al justificarlo— así que un escaneo que contara los
/// comentarios obligaría a escribir la documentación en clave para no romper el test. Y al revés: un
/// escaneo que los cuente como código se puede satisfacer <b>comentando</b> la línea que vigila, con lo
/// que deja de vigilar nada.
/// </para>
/// <para>
/// Vive en un tipo propio, como <c>TiempoFijo</c> o <c>FabricasDeContexto</c>, porque lo usan escaneos de
/// dos preocupaciones distintas —el punto único del tope y el orden de la comprobación de permisos— y una
/// segunda copia del criterio es exactamente lo que haría que uno de los dos deje de cubrir sin que nada
/// avise.
/// </para>
/// </remarks>
internal static class FuenteSinComentarios
{
    /// <summary>La línea sin su comentario: cadena vacía si la línea entera era comentario.</summary>
    /// <remarks>
    /// <b>Es un recorte por texto y tiene un hueco conocido: corta en el primer <c>//</c> aunque esté
    /// dentro de un literal de cadena.</b> Un número o un token escrito a la derecha de un <c>//</c>
    /// entrecomillado —una URL, un patrón— queda invisible para todos los escaneos que usen esto. Se deja
    /// así a propósito: es un <i>falso negativo</i>, no un falso positivo, así que no puede poner en rojo
    /// código legítimo, y distinguir comentario de literal exige un analizador de sintaxis. Lo que no se
    /// puede es creer que el escaneo es total: quien encuentre este hueco tiene que saber que ya estaba
    /// visto y medido, en vez de descubrirlo el día que necesite confiar en el guardarraíl.
    /// </remarks>
    public static string DeUnaLinea(string linea)
    {
        var recortada = linea.TrimStart();

        if (recortada.StartsWith("//", StringComparison.Ordinal)
            || recortada.StartsWith("*", StringComparison.Ordinal)
            || recortada.StartsWith("/*", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var comentarioAlFinal = recortada.IndexOf("//", StringComparison.Ordinal);

        return comentarioAlFinal < 0 ? recortada : recortada[..comentarioAlFinal];
    }

    /// <summary>
    /// El archivo entero sin comentarios, conservando una línea por línea.
    /// </summary>
    /// <remarks>
    /// Los saltos se conservan para que el resultado siga siendo legible en un mensaje de fallo y para que
    /// buscar una subcadena no pueda encontrarla a caballo de dos líneas que el recorte pegó.
    /// </remarks>
    public static string DeUnArchivo(string ruta) =>
        string.Join('\n', File.ReadAllLines(ruta).Select(DeUnaLinea));
}
