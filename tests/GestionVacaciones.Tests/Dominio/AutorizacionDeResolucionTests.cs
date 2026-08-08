using System.Data.Common;
using System.Globalization;
using GestionVacaciones.Data;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// FEAT-002, Bloque 2: los métodos async nuevos de <see cref="PermisosService"/> —constructor
/// sobrecargado con <see cref="IDbContextFactory{TContext}"/>— que deciden quién puede
/// <b>resolver</b> (aprobar o rechazar) una solicitud ajena, y quién tiene autoridad sobre quién
/// (<see cref="PermisosService.EmpleadosBajoAutoridadDeAsync"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>R-22 es el riesgo que ordena este archivo.</b> <see cref="Un_empleado_no_puede_resolver_su_propia_solicitud"/>
/// es el caso central: prueba que el <c>self</c> se excluye como condición <b>propia</b> del método, no
/// como ausencia accidental de una condición, sembrando el dato externo anómalo
/// (<c>Empleado.ManagerId == Empleado.Id</c>) que la mitigación tiene que resistir.
/// </para>
/// <para>
/// La mayoría necesita la instancia SQL Server 2022 del entorno: lo que se afirma es el resultado de
/// consultar el organigrama real. Los dos casos que fijan el orden como contrato —
/// <see cref="Un_fallo_de_persistencia_al_consultar_el_organigrama_se_propaga"/> y
/// <see cref="Sin_identidad_los_metodos_de_organigrama_no_se_degradan"/>— usan
/// <see cref="FabricaQueNadieDebeUsar"/> y por eso no la necesitan.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class AutorizacionDeResolucionTests
{
    private const int Propio = 7;
    private const int Ajeno = 8;

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public AutorizacionDeResolucionTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_manager_puede_resolver_las_solicitudes_de_su_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));
        var quienConsulta = IdentidadDelEmpleado.De(manager.Id);

        Assert.True(await permisos.PuedeResolverLasSolicitudesDeAsync(quienConsulta, subordinado.Id, Cancelacion));

        // Y exigirlo no lanza.
        await permisos.ExigirPoderResolverLasSolicitudesDeAsync(quienConsulta, subordinado.Id, Cancelacion);
    }

    [Fact]
    public async Task El_designado_puede_resolver_las_solicitudes_del_equipo_de_su_manager()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var designado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerYDesignadoAsync(subordinado.Id, manager.Id, designado.Id);

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));

        Assert.True(await permisos.PuedeResolverLasSolicitudesDeAsync(
            IdentidadDelEmpleado.De(designado.Id), subordinado.Id, Cancelacion));
    }

    /// <summary>
    /// El caso central de R-22. El dato de organigrama es <b>anómalo a propósito</b>
    /// (<c>ManagerId == Id</c> de la propia persona) para probar que la exclusión del <c>self</c> es una
    /// condición propia del método y no una consecuencia incidental de la consulta: si lo fuera, este
    /// caso se autoaprobaría.
    /// </summary>
    [Fact]
    public async Task Un_empleado_no_puede_resolver_su_propia_solicitud()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(empleado.Id, empleado.Id);

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));
        var identidad = IdentidadDelEmpleado.De(empleado.Id);

        // Contraprueba: para el mismo par, "ver" sí concede -- es la propia persona (FEAT-001a).
        Assert.True(await permisos.PuedeVerLasSolicitudesDeAsync(identidad, empleado.Id, Cancelacion));

        // Pero "resolver" no, ni siquiera con el dato anómalo de ManagerId == Id sembrado arriba.
        Assert.False(await permisos.PuedeResolverLasSolicitudesDeAsync(identidad, empleado.Id, Cancelacion));

        await Assert.ThrowsAsync<AccesoASolicitudesDenegadoException>(
            () => permisos.ExigirPoderResolverLasSolicitudesDeAsync(identidad, empleado.Id, Cancelacion));
    }

    /// <summary>Camino triste que valida FR-06/AC-08 en el camino de resolución.</summary>
    [Fact]
    public async Task Un_empleado_sin_relacion_no_puede_resolver_la_solicitud_de_otro()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var quienConsulta = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var ajeno = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));
        var identidad = IdentidadDelEmpleado.De(quienConsulta.Id);

        Assert.False(await permisos.PuedeResolverLasSolicitudesDeAsync(identidad, ajeno.Id, Cancelacion));

        var excepcion = await Assert.ThrowsAsync<AccesoASolicitudesDenegadoException>(
            () => permisos.ExigirPoderResolverLasSolicitudesDeAsync(identidad, ajeno.Id, Cancelacion));

        // R-12: los dos identificadores, nunca nombre ni correo.
        Assert.Contains(
            quienConsulta.Id.ToString(CultureInfo.InvariantCulture), excepcion.Message, StringComparison.Ordinal);
        Assert.Contains(ajeno.Id.ToString(CultureInfo.InvariantCulture), excepcion.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmpleadosBajoAutoridadDeAsync_devuelve_vacio_para_quien_no_tiene_equipo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var solo = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));

        var equipo = await permisos.EmpleadosBajoAutoridadDeAsync(IdentidadDelEmpleado.De(solo.Id), Cancelacion);

        // Vacío, no un error: es la respuesta legítima de alguien sin equipo a cargo.
        Assert.Empty(equipo);
    }

    [Fact]
    public async Task EmpleadosBajoAutoridadDeAsync_incluye_al_equipo_del_manager_y_al_del_designado()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var designado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinadoDirecto = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinadoDelegado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var ajeno = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // El manager es jefe directo de subordinadoDirecto, y designado de sí mismo para
        // subordinadoDelegado (el caso "el manager de X me delegó a mí"). ajeno no tiene relación con
        // nadie: si el filtro no filtrara, aparecería en cualquiera de los dos equipos.
        await AsignarManagerAsync(subordinadoDirecto.Id, manager.Id);
        await AsignarManagerYDesignadoAsync(subordinadoDelegado.Id, manager.Id, designado.Id);

        var permisos = new PermisosService(new FabricaDeLaBaseDeTest(_baseDeDatos));

        var equipoDelManager = await permisos.EmpleadosBajoAutoridadDeAsync(
            IdentidadDelEmpleado.De(manager.Id), Cancelacion);
        var equipoDelDesignado = await permisos.EmpleadosBajoAutoridadDeAsync(
            IdentidadDelEmpleado.De(designado.Id), Cancelacion);

        Assert.Equal(
            new[] { subordinadoDirecto.Id, subordinadoDelegado.Id }.OrderBy(id => id),
            equipoDelManager.OrderBy(id => id));
        Assert.Equal([subordinadoDelegado.Id], equipoDelDesignado);
        Assert.DoesNotContain(ajeno.Id, equipoDelManager);
    }

    /// <summary>
    /// Mitigación de R-22 (Information Disclosure, C15-I): la proyección de las tres consultas nuevas es
    /// mínima. Se captura el SQL real que EF Core emite y se afirma que ninguno pide <c>Nombre</c> ni
    /// <c>Correo</c>.
    /// </summary>
    [Fact]
    public async Task Las_consultas_de_organigrama_no_traen_nombre_ni_correo()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await AsignarManagerAsync(subordinado.Id, manager.Id);

        using var sonda = _baseDeDatos.CrearContexto();
        var cadena = sonda.Database.GetConnectionString()!;
        var captura = new InterceptorDeCaptura();
        var permisos = new PermisosService(new FabricaDeContextosConCaptura(cadena, captura));
        var quienConsulta = IdentidadDelEmpleado.De(manager.Id);

        // Ejercita los tres métodos que consultan el organigrama.
        await permisos.PuedeVerLasSolicitudesDeAsync(quienConsulta, subordinado.Id, Cancelacion);
        await permisos.PuedeResolverLasSolicitudesDeAsync(quienConsulta, subordinado.Id, Cancelacion);
        await permisos.EmpleadosBajoAutoridadDeAsync(quienConsulta, Cancelacion);

        Assert.NotEmpty(captura.Comandos);
        foreach (var comando in captura.Comandos)
        {
            Assert.DoesNotContain("Nombre", comando, StringComparison.Ordinal);
            Assert.DoesNotContain("Correo", comando, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Camino triste que cierra el hueco de F-SPEC-16: un fallo real de la base, en cualquiera de los
    /// tres métodos nuevos, se propaga tal cual y no se convierte en <c>false</c> ni en lista vacía.
    /// </summary>
    [Fact]
    public async Task Un_fallo_de_persistencia_al_consultar_el_organigrama_se_propaga()
    {
        var permisos = new PermisosService(new FabricaQueNadieDebeUsar());
        var identidad = IdentidadDelEmpleado.De(Propio);

        var primero = await Assert.ThrowsAsync<InvalidOperationException>(
            () => permisos.PuedeVerLasSolicitudesDeAsync(identidad, Ajeno, Cancelacion));
        Assert.Contains(FabricaQueNadieDebeUsar.Marca, primero.Message, StringComparison.Ordinal);

        var segundo = await Assert.ThrowsAsync<InvalidOperationException>(
            () => permisos.PuedeResolverLasSolicitudesDeAsync(identidad, Ajeno, Cancelacion));
        Assert.Contains(FabricaQueNadieDebeUsar.Marca, segundo.Message, StringComparison.Ordinal);

        var tercero = await Assert.ThrowsAsync<InvalidOperationException>(
            () => permisos.EmpleadosBajoAutoridadDeAsync(identidad, Cancelacion));
        Assert.Contains(FabricaQueNadieDebeUsar.Marca, tercero.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Camino triste que cierra el otro hueco de F-SPEC-16: sin empleado seleccionado, los tres métodos
    /// propagan <see cref="SinEmpleadoSeleccionadoException"/> <b>antes</b> de abrir ningún contexto —
    /// <see cref="FabricaQueNadieDebeUsar"/> lo demuestra: si se llegara a usar, revienta con su propio
    /// mensaje y no con el de la identidad.
    /// </summary>
    [Fact]
    public async Task Sin_identidad_los_metodos_de_organigrama_no_se_degradan()
    {
        var permisos = new PermisosService(new FabricaQueNadieDebeUsar());
        var sinIdentidad = IdentidadDelEmpleado.SinSeleccionar;

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(
            () => permisos.PuedeVerLasSolicitudesDeAsync(sinIdentidad, Propio, Cancelacion));

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(
            () => permisos.PuedeResolverLasSolicitudesDeAsync(sinIdentidad, Propio, Cancelacion));

        await Assert.ThrowsAsync<SinEmpleadoSeleccionadoException>(
            () => permisos.EmpleadosBajoAutoridadDeAsync(sinIdentidad, Cancelacion));
    }

    private async Task AsignarManagerAsync(int empleadoId, int managerId)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        var empleado = await contexto.Empleados
            .SingleAsync(candidato => candidato.Id == empleadoId, Cancelacion);
        empleado.ManagerId = managerId;
        await contexto.SaveChangesAsync(Cancelacion);
    }

    private async Task AsignarManagerYDesignadoAsync(int empleadoId, int managerId, int designadoId)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        var empleado = await contexto.Empleados
            .SingleAsync(candidato => candidato.Id == empleadoId, Cancelacion);
        empleado.ManagerId = managerId;
        empleado.DesignadoId = designadoId;
        await contexto.SaveChangesAsync(Cancelacion);
    }

    /// <summary>
    /// Fábrica de contextos contra la base descartable de la corrida, con un interceptor que
    /// <b>captura</b> el SQL en vez de romperlo. Hermana de
    /// <c>TopeAnualEnElAltaTests.FabricaDeContextosConInterceptor</c>, con otro propósito.
    /// </summary>
    private sealed class FabricaDeContextosConCaptura(string cadena, InterceptorDeCaptura interceptor)
        : IDbContextFactory<VacacionesDbContext>
    {
        public VacacionesDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<VacacionesDbContext>()
                .UseSqlServer(cadena)
                .AddInterceptors(interceptor)
                .Options);
    }

    /// <summary>Registra el texto de cada comando de lectura que el contexto ejecuta, sin alterarlo.</summary>
    private sealed class InterceptorDeCaptura : DbCommandInterceptor
    {
        private readonly List<string> _comandos = [];

        public IReadOnlyList<string> Comandos => _comandos;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _comandos.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
