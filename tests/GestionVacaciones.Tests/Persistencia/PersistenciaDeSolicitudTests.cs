using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// B2-T7 y B2-T4: el camino feliz de la persistencia y la unicidad del correo, contra la instancia
/// SQL Server 2022 del entorno.
/// </summary>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class PersistenciaDeSolicitudTests
{
    private readonly BaseDeDatosDeTest _baseDeDatos;

    public PersistenciaDeSolicitudTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    [Fact]
    public async Task B2_T7_Una_solicitud_valida_se_relee_con_sus_siete_campos_intactos()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var fechaInicio = new DateOnly(2026, 1, 3);
        var fechaFin = new DateOnly(2026, 1, 5);
        // Offset distinto de cero a propósito: una columna `datetime2` en lugar de `datetimeoffset`
        // guardaría la hora y perdería el huso, y la relectura seguiría pareciendo correcta si el
        // test usara UTC.
        var fechaCreacion = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(-3));

        int id;
        await using (var contexto = _baseDeDatos.CrearContexto())
        {
            var solicitud = new Solicitud
            {
                EmpleadoId = empleado.Id,
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                DiasCorridos = 3,
                Estado = EstadoSolicitud.Pendiente,
                FechaCreacion = fechaCreacion,
            };

            contexto.Solicitudes.Add(solicitud);
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
            id = solicitud.Id;
        }

        Assert.NotEqual(0, id);

        // Contexto nuevo: con el mismo se releería la instancia rastreada en memoria y el test no
        // diría nada sobre lo que quedó en la base.
        await using var relectura = _baseDeDatos.CrearContexto();
        var releida = await relectura.Solicitudes
            .AsNoTracking()
            .SingleAsync(solicitud => solicitud.Id == id, TestContext.Current.CancellationToken);

        Assert.Equal(id, releida.Id);
        Assert.Equal(empleado.Id, releida.EmpleadoId);
        Assert.Equal(fechaInicio, releida.FechaInicio);
        Assert.Equal(fechaFin, releida.FechaFin);
        Assert.Equal(3, releida.DiasCorridos);
        Assert.Equal(EstadoSolicitud.Pendiente, releida.Estado);
        Assert.Equal(fechaCreacion, releida.FechaCreacion);
        Assert.Equal(fechaCreacion.Offset, releida.FechaCreacion.Offset);
    }

    [Fact]
    public async Task B2_T4_Dos_empleados_con_el_mismo_correo_violan_la_unicidad()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await using var contexto = _baseDeDatos.CrearContexto();
        contexto.Empleados.Add(new Empleado
        {
            Nombre = "Homónimo de prueba",
            Correo = empleado.Correo,
        });

        var excepcion = await Assert.ThrowsAsync<DbUpdateException>(
            () => contexto.SaveChangesAsync(TestContext.Current.CancellationToken));

        // El correo identifica a la persona en la nómina que alimenta el selector: duplicarlo crearía
        // dos identidades indistinguibles.
        Assert.Contains(VacacionesDbContext.IndiceDeCorreoUnico, excepcion.ToString(), StringComparison.Ordinal);

        await using var relectura = _baseDeDatos.CrearContexto();
        var cuantos = await relectura.Empleados
            .CountAsync(otro => otro.Correo == empleado.Correo, TestContext.Current.CancellationToken);

        Assert.Equal(1, cuantos);
    }

    [Fact]
    public async Task El_correo_admite_los_320_caracteres_que_declara_el_modelo()
    {
        // El límite no es decorativo: una columna más corta truncaría o rechazaría correos legítimos,
        // y 320 es el máximo de una dirección (64 de parte local + @ + 255 de dominio).
        _baseDeDatos.SaltearSiNoEstaDisponible();

        var dominio = new string('d', 255 - ".ejemplo".Length) + ".ejemplo";
        var correo = $"{new string('l', 64)}@{dominio}";
        Assert.Equal(320, correo.Length);

        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync(correo);

        await using var relectura = _baseDeDatos.CrearContexto();
        var releido = await relectura.Empleados
            .AsNoTracking()
            .SingleAsync(otro => otro.Id == empleado.Id, TestContext.Current.CancellationToken);

        Assert.Equal(correo, releido.Correo);
    }

    [Fact]
    public async Task Las_autorreferencias_de_manager_y_designado_se_persisten_y_se_releen()
    {
        // Son las dos FK que obligan a ON DELETE NO ACTION: en cascada SQL Server rechaza el ciclo, y
        // el fallo aparecería recién al aplicar la migración. Que existan y se relean es lo que
        // habilita la nómina circular Ana/Diego del Bloque 3.
        _baseDeDatos.SaltearSiNoEstaDisponible();

        await using var manager = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var designado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var subordinado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await using (var contexto = _baseDeDatos.CrearContexto())
        {
            var aModificar = await contexto.Empleados
                .SingleAsync(empleado => empleado.Id == subordinado.Id, TestContext.Current.CancellationToken);
            aModificar.ManagerId = manager.Id;
            aModificar.DesignadoId = designado.Id;
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var relectura = _baseDeDatos.CrearContexto();
        var releido = await relectura.Empleados
            .AsNoTracking()
            .Include(empleado => empleado.Manager)
            .Include(empleado => empleado.Designado)
            .SingleAsync(empleado => empleado.Id == subordinado.Id, TestContext.Current.CancellationToken);

        Assert.Equal(manager.Id, releido.ManagerId);
        Assert.Equal(designado.Id, releido.DesignadoId);
        Assert.Equal(manager.Id, releido.Manager?.Id);
        Assert.Equal(designado.Id, releido.Designado?.Id);
    }
}
