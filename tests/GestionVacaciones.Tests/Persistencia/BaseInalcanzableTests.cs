using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// B2-T8: con la base inalcanzable la excepción de conexión <b>se propaga</b>. Es el caso peligroso
/// del bloque: degradar a lista vacía le diría al empleado «no tenés solicitudes» mientras la base
/// está caída, y él no tendría forma de distinguirlo de la verdad.
/// </summary>
/// <remarks>
/// No necesita la instancia del entorno —necesita justo lo contrario—, así que no entra en
/// <see cref="ColeccionDeBaseDeDatos"/> ni se saltea nunca: la invariante que fija vale en cualquier
/// máquina.
/// </remarks>
public sealed class BaseInalcanzableTests
{
    /// <summary>
    /// Endpoint local sin nadie escuchando: el sistema operativo rechaza el TCP de inmediato, así que
    /// el test no depende de un timeout ni de resolver un nombre inexistente.
    /// </summary>
    private const string CadenaHaciaNadie =
        "Server=127.0.0.1,14330;Initial Catalog=GestionVacacionesV2_Test;User ID=usuario-ficticio;" +
        "Password=valor-ficticio-de-test;Encrypt=True;TrustServerCertificate=True;" +
        "Connect Timeout=2;ConnectRetryCount=0";

    [Fact]
    public async Task B2_T8_Consultar_con_la_base_caida_lanza_en_vez_de_devolver_una_lista_vacia()
    {
        await using var contexto = BaseDeDatosDeTest.CrearContexto(CadenaHaciaNadie);

        var excepcion = await Record.ExceptionAsync(
            () => contexto.Solicitudes.ToListAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(excepcion);
        Assert.IsAssignableFrom<DbException>(excepcion);
    }

    [Fact]
    public async Task B2_T8_Tampoco_se_degrada_a_vacio_al_filtrar_por_empleado()
    {
        // La consulta del listado de FR-04 lleva filtro y orden. Se ejercita esa forma y no solo el
        // ToList() pelado: una captura de excepciones puesta en el servicio del Bloque 5 podría
        // devolver vacío justo en este camino y el test de arriba no se enteraría.
        await using var contexto = BaseDeDatosDeTest.CrearContexto(CadenaHaciaNadie);

        var excepcion = await Record.ExceptionAsync(() => contexto.Solicitudes
            .Where(solicitud => solicitud.EmpleadoId == 1)
            .OrderByDescending(solicitud => solicitud.FechaCreacion)
            .ToListAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(excepcion);
        Assert.IsAssignableFrom<DbException>(excepcion);
    }
}
