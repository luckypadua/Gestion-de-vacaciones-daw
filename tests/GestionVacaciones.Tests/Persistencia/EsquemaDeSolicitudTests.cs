using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// B2-T1, B2-T2, B2-T3, B2-T9 y B2-T5: las invariantes que NFR-04 exige reforzar en la base, más el
/// índice que sirve al listado de FR-04.
/// </summary>
/// <remarks>
/// <para>
/// Corren contra la instancia SQL Server 2022 del entorno, sobre <c>GestionVacacionesV2_Test</c>. El
/// proveedor InMemory <b>ignora por completo los check constraints</b>: un test escrito contra él
/// pasaría en verde afirmando lo contrario de lo que NFR-04 exige.
/// </para>
/// <para>
/// <b>Sobre qué constraint nombra SQL Server.</b> Tres de las cuatro no son aislables entre sí:
/// <c>CK_Solicitud_PeriodoCoherente</c> se deduce de las otras dos —si los días son positivos y
/// coinciden con <c>DATEDIFF+1</c>, la fecha de fin no puede ser anterior a la de inicio— y
/// <c>CK_Solicitud_DiasPositivos</c> se deduce de las otras dos por el mismo camino. No existe fila
/// que viole una sola. Cuando varias se violan a la vez SQL Server informa una y no documenta cuál,
/// así que estos tests afirman que la rechazada está entre las que la fila viola de verdad, y
/// <see cref="Las_cuatro_check_constraints_de_NFR_04_existen_en_la_base"/> cierra el hueco
/// comprobando que las cuatro existen con su definición.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class EsquemaDeSolicitudTests
{
    /// <summary>
    /// El nombre de la tabla no vive en el modelo: EF lo deriva del <c>DbSet</c> y no hay constante
    /// que consumir. Los nombres de las constraints y del índice sí, y se toman de
    /// <see cref="VacacionesDbContext"/>: repetirlos acá como literales dejaría que renombrar uno en
    /// el modelo pasara sin que nada se ponga en rojo.
    /// </summary>
    private const string TablaDeSolicitudes = "dbo.Solicitudes";

    private static readonly DateTimeOffset _momentoDeCreacion =
        new(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public EsquemaDeSolicitudTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    [Fact]
    public async Task B2_T1_Un_periodo_invertido_es_rechazado_por_la_base()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // 5-ene → 3-ene, con los días que le corresponderían a ese período invertido (DATEDIFF+1 = -1):
        // así la fila no viola CK_Solicitud_DiasCoincidenConPeriodo y el conjunto de sospechosos se
        // reduce a dos.
        var solicitud = new Solicitud
        {
            EmpleadoId = empleado.Id,
            FechaInicio = new DateOnly(2026, 1, 5),
            FechaFin = new DateOnly(2026, 1, 3),
            DiasCorridos = -1,
            Estado = EstadoSolicitud.Pendiente,
            FechaCreacion = _momentoDeCreacion,
        };

        var excepcion = await IntentarPersistirAsync(solicitud);

        AfirmarQueLaRechazoAlgunaDe(
            excepcion, VacacionesDbContext.PeriodoCoherente, VacacionesDbContext.DiasPositivos);
        await AfirmarQueNoQuedoNadaPersistidoAsync(empleado.Id);
    }

    [Fact]
    public async Task B2_T2_Cero_dias_corridos_es_rechazado_por_la_base()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // Período válido de 3 días con DiasCorridos = 0: viola «los días son positivos» y, de paso,
        // «los días coinciden con el período». No hay fila que viole solo la primera.
        var solicitud = new Solicitud
        {
            EmpleadoId = empleado.Id,
            FechaInicio = new DateOnly(2026, 1, 3),
            FechaFin = new DateOnly(2026, 1, 5),
            DiasCorridos = 0,
            Estado = EstadoSolicitud.Pendiente,
            FechaCreacion = _momentoDeCreacion,
        };

        var excepcion = await IntentarPersistirAsync(solicitud);

        AfirmarQueLaRechazoAlgunaDe(
            excepcion, VacacionesDbContext.DiasPositivos, VacacionesDbContext.DiasCoincidenConPeriodo);
        await AfirmarQueNoQuedoNadaPersistidoAsync(empleado.Id);
    }

    [Fact]
    public async Task B2_T3_Dias_corridos_que_no_coinciden_con_el_periodo_son_rechazados_por_la_base()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // 3-ene → 5-ene son 3 días. Con 99 la fila viola UNA sola constraint, así que acá sí se puede
        // nombrar exactamente cuál. Es la que impide que la interfaz muestre un número (AC-01) y la
        // base guarde otro (AC-04).
        var solicitud = new Solicitud
        {
            EmpleadoId = empleado.Id,
            FechaInicio = new DateOnly(2026, 1, 3),
            FechaFin = new DateOnly(2026, 1, 5),
            DiasCorridos = 99,
            Estado = EstadoSolicitud.Pendiente,
            FechaCreacion = _momentoDeCreacion,
        };

        var excepcion = await IntentarPersistirAsync(solicitud);

        AfirmarQueLaRechazoAlgunaDe(excepcion, VacacionesDbContext.DiasCoincidenConPeriodo);
        await AfirmarQueNoQuedoNadaPersistidoAsync(empleado.Id);
    }

    [Fact]
    public async Task B2_T9_Un_estado_fuera_del_rango_del_enum_es_rechazado_por_la_base()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        // El estado se persiste como int: nada en el tipo impide un 99. Es alcanzable por carga
        // externa o por un cast en C#, y las otras tres constraints no lo miran.
        var solicitud = new Solicitud
        {
            EmpleadoId = empleado.Id,
            FechaInicio = new DateOnly(2026, 1, 3),
            FechaFin = new DateOnly(2026, 1, 5),
            DiasCorridos = 3,
            Estado = (EstadoSolicitud)99,
            FechaCreacion = _momentoDeCreacion,
        };

        var excepcion = await IntentarPersistirAsync(solicitud);

        AfirmarQueLaRechazoAlgunaDe(excepcion, VacacionesDbContext.EstadoValido);
        await AfirmarQueNoQuedoNadaPersistidoAsync(empleado.Id);
    }

    [Fact]
    public async Task B2_T5_La_migracion_crea_el_indice_del_listado_con_la_fecha_descendente()
    {
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var contexto = _baseDeDatos.CrearContexto();

        var columnas = await contexto.Database
            .SqlQuery<string>($"""
                SELECT CONCAT(c.name, N':', ic.is_descending_key) AS Value
                FROM sys.indexes AS i
                INNER JOIN sys.index_columns AS ic
                    ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                INNER JOIN sys.columns AS c
                    ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID({TablaDeSolicitudes}) AND i.name = {VacacionesDbContext.IndiceDelListado}
                ORDER BY ic.key_ordinal
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        // El orden y la dirección son el punto: el listado de FR-04 ordena descendente por fecha de
        // creación dentro de un empleado. Un índice con las mismas columnas ascendentes existiría pero
        // obligaría a ordenar en memoria, que es justo lo que NFR-01 quiere evitar.
        Assert.Equal(new[] { "EmpleadoId:0", "FechaCreacion:1" }, columnas);
    }

    [Fact]
    public async Task Las_cuatro_check_constraints_de_NFR_04_existen_en_la_base()
    {
        // Cierra el hueco que dejan B2-T1 y B2-T2: como ninguna de sus filas viola una sola
        // constraint, borrar CK_Solicitud_PeriodoCoherente del modelo los dejaría en verde. Acá no.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var contexto = _baseDeDatos.CrearContexto();

        var existentes = await contexto.Database
            .SqlQuery<string>($"""
                SELECT name AS Value
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID({TablaDeSolicitudes})
                ORDER BY name
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            new[]
            {
                VacacionesDbContext.DiasCoincidenConPeriodo,
                VacacionesDbContext.DiasPositivos,
                VacacionesDbContext.EstadoValido,
                VacacionesDbContext.PeriodoCoherente,
            },
            existentes);
    }

    [Fact]
    public async Task La_definicion_de_cada_check_constraint_menciona_las_columnas_que_debe_vigilar()
    {
        // Que existan cuatro nombres no dice que vigilen algo: una constraint «1 = 1» pasaría el test
        // anterior. Esto ata cada nombre a las columnas de su invariante.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var contexto = _baseDeDatos.CrearContexto();

        var definiciones = await contexto.Database
            .SqlQuery<string>($"""
                SELECT CONCAT(name, N'|', definition) AS Value
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID({TablaDeSolicitudes})
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        var porNombre = definiciones
            .Select(fila => fila.Split('|', 2))
            .ToDictionary(partes => partes[0], partes => partes[1], StringComparer.Ordinal);

        Assert.Contains("FechaInicio", porNombre[VacacionesDbContext.PeriodoCoherente], StringComparison.Ordinal);
        Assert.Contains("FechaFin", porNombre[VacacionesDbContext.PeriodoCoherente], StringComparison.Ordinal);
        Assert.Contains("DiasCorridos", porNombre[VacacionesDbContext.DiasPositivos], StringComparison.Ordinal);
        Assert.Contains(
            "datediff", porNombre[VacacionesDbContext.DiasCoincidenConPeriodo], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Estado", porNombre[VacacionesDbContext.EstadoValido], StringComparison.Ordinal);
    }

    private async Task<DbUpdateException> IntentarPersistirAsync(Solicitud solicitud)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        contexto.Solicitudes.Add(solicitud);

        return await Assert.ThrowsAsync<DbUpdateException>(
            () => contexto.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    private async Task AfirmarQueNoQuedoNadaPersistidoAsync(int empleadoId)
    {
        // La defensa en profundidad no sirve de nada si la fila entra igual: el SaveChanges es
        // transaccional y el rechazo tiene que dejar la tabla como estaba.
        await using var contexto = _baseDeDatos.CrearContexto();
        var cuantas = await contexto.Solicitudes
            .CountAsync(solicitud => solicitud.EmpleadoId == empleadoId, TestContext.Current.CancellationToken);

        Assert.Equal(0, cuantas);
    }

    private static void AfirmarQueLaRechazoAlgunaDe(DbUpdateException excepcion, params string[] esperadas)
    {
        var detalle = excepcion.ToString();

        Assert.True(
            esperadas.Any(nombre => detalle.Contains(nombre, StringComparison.Ordinal)),
            $"Se esperaba el rechazo de alguna de [{string.Join(", ", esperadas)}], y SQL Server informó: {detalle}");
    }
}
