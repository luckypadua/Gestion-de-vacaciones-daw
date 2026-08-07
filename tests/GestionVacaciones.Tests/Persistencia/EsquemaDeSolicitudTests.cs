using GestionVacaciones.Data;
using GestionVacaciones.Data.Entidades;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GestionVacaciones.Tests.Persistencia;

/// <summary>
/// B2-T1, B2-T2, B2-T3, B2-T9 y B2-T5: las invariantes que NFR-04 exige reforzar en la base, más el
/// índice que sirve al listado de FR-04. Desde el Bloque 1 de FEAT-002 se suma la quinta check
/// constraint (<see cref="VacacionesDbContext.ResolucionCoherente"/>) y la FK de
/// <c>ResueltoPorId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Corren contra la instancia SQL Server 2022 del entorno, sobre <c>GestionVacacionesV2_Test</c>. El
/// proveedor InMemory <b>ignora por completo los check constraints</b>: un test escrito contra él
/// pasaría en verde afirmando lo contrario de lo que NFR-04 exige.
/// </para>
/// <para>
/// <b>Sobre qué constraint nombra SQL Server.</b> Tres de las cuatro originales no son aislables
/// entre sí: <c>CK_Solicitud_PeriodoCoherente</c> se deduce de las otras dos —si los días son
/// positivos y coinciden con <c>DATEDIFF+1</c>, la fecha de fin no puede ser anterior a la de
/// inicio— y <c>CK_Solicitud_DiasPositivos</c> se deduce de las otras dos por el mismo camino. No
/// existe fila que viole una sola. Cuando varias se violan a la vez SQL Server informa una y no
/// documenta cuál, así que estos tests afirman que la rechazada está entre las que la fila viola de
/// verdad, y <see cref="Las_cinco_check_constraints_de_NFR_04_existen_en_la_base"/> cierra el hueco
/// comprobando que las cinco existen con su definición.
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

    /// <summary>Las cuatro check constraints anteriores al Bloque 1 de FEAT-002, en orden alfabético.</summary>
    private static readonly string[] _lasCuatroConstraintsAnteriores =
    [
        VacacionesDbContext.DiasCoincidenConPeriodo,
        VacacionesDbContext.DiasPositivos,
        VacacionesDbContext.EstadoValido,
        VacacionesDbContext.PeriodoCoherente,
    ];

    /// <summary>Las cinco check constraints vigentes desde el Bloque 1 de FEAT-002, en orden alfabético.</summary>
    private static readonly string[] _lasCincoConstraints =
    [
        VacacionesDbContext.DiasCoincidenConPeriodo,
        VacacionesDbContext.DiasPositivos,
        VacacionesDbContext.EstadoValido,
        VacacionesDbContext.PeriodoCoherente,
        VacacionesDbContext.ResolucionCoherente,
    ];

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

    /// <summary>
    /// Nombre de la migración inmediatamente anterior a la de este bloque
    /// (<c>ColumnasDeResolucion</c>, FEAT-002 Bloque 1). Es el destino de la reversión que
    /// <see cref="La_migracion_revierte_dejando_el_esquema_anterior"/> ejercita. Vive acá como
    /// literal porque es un hecho del historial de migraciones, no del modelo: no hay una constante
    /// del lado de producción a la que atarlo.
    /// </summary>
    private const string NombreDeLaMigracionAnterior = "20260806034345_IndiceDelSaldo";

    [Fact]
    public async Task El_indice_del_saldo_existe_con_sus_columnas_y_su_orden()
    {
        // El saldo filtra por empleado, estado y rango de fechas (NFR-03): sin este índice, cada
        // cálculo de SaldoService escanea las solicitudes del empleado enteras.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var contexto = _baseDeDatos.CrearContexto();

        var columnas = await ColumnasDelIndiceAsync(contexto, VacacionesDbContext.IndiceDelSaldo);

        Assert.Equal(new[] { "EmpleadoId:0", "Estado:0", "FechaInicio:0" }, columnas);
    }

    [Fact]
    public async Task Las_check_constraints_siguen_siendo_las_mismas_cinco()
    {
        // El Bloque 1 de FEAT-002 agrega CK_Solicitud_ResolucionCoherente, la quinta. Confirmación
        // explícita del bloque, distinta de Las_cinco_check_constraints_de_NFR_04_existen_en_la_base
        // (que además ata cada nombre a la definición completa).
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var contexto = _baseDeDatos.CrearContexto();

        var existentes = await ObtenerCheckConstraintsAsync(contexto);

        Assert.Equal(_lasCincoConstraints, existentes);
    }

    [Fact]
    public async Task La_migracion_revierte_dejando_el_esquema_anterior()
    {
        // Camino triste del Bloque 1 de FEAT-002: la migración aplica y revierte limpiamente. Ida:
        // la quinta constraint y las tres columnas de resolución existen. Vuelta: desaparecen las
        // tres columnas y la quinta constraint, y las cuatro anteriores siguen tal cual estaban.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var contexto = _baseDeDatos.CrearContexto();
        var migrador = ObtenerMigrador(contexto);

        try
        {
            // Ida: el fixture ya aplicó todas las migraciones al preparar la base, pero se reafirma
            // acá para que el test no dependa de ese orden implícito.
            await migrador.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(_lasCincoConstraints, await ObtenerCheckConstraintsAsync(contexto));
            Assert.Equal(
                new[] { "FechaResolucion", "MotivoDeRechazo", "ResueltoPorId" },
                await ColumnasDeResolucionAsync(contexto));

            // Vuelta: revierte a la migración anterior a la de este bloque.
            await migrador.MigrateAsync(NombreDeLaMigracionAnterior, TestContext.Current.CancellationToken);

            Assert.Equal(_lasCuatroConstraintsAnteriores, await ObtenerCheckConstraintsAsync(contexto));
            Assert.Empty(await ColumnasDeResolucionAsync(contexto));
        }
        finally
        {
            // Reaplica siempre, incluso si una aserción de arriba falla: los demás tests de esta
            // colección comparten la misma base y no pueden heredar un esquema a mitad de camino.
            await migrador.MigrateAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        Assert.Equal(_lasCincoConstraints, await ObtenerCheckConstraintsAsync(contexto));
        Assert.Equal(
            new[] { "FechaResolucion", "MotivoDeRechazo", "ResueltoPorId" },
            await ColumnasDeResolucionAsync(contexto));
    }

    [Fact]
    public async Task Las_cinco_check_constraints_de_NFR_04_existen_en_la_base()
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

        Assert.Equal(_lasCincoConstraints, existentes);
    }

    [Fact]
    public async Task La_definicion_de_cada_check_constraint_menciona_las_columnas_que_debe_vigilar()
    {
        // Que existan cinco nombres no dice que vigilen algo: una constraint «1 = 1» pasaría el test
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
        Assert.Contains("ResueltoPorId", porNombre[VacacionesDbContext.ResolucionCoherente], StringComparison.Ordinal);
        Assert.Contains("FechaResolucion", porNombre[VacacionesDbContext.ResolucionCoherente], StringComparison.Ordinal);
        Assert.Contains("MotivoDeRechazo", porNombre[VacacionesDbContext.ResolucionCoherente], StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_check_constraint_de_resolucion_exige_coherencia_con_el_estado()
    {
        // Camino triste del Bloque 1 de FEAT-002: las tres combinaciones incoherentes que
        // CK_Solicitud_ResolucionCoherente tiene que rechazar, insertadas directamente por SQL (no
        // vía SaveChanges) para probar la constraint en sí misma, no el mapeo de EF.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var resolutor = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var contexto = _baseDeDatos.CrearContexto();

        // Pendiente con datos de resolución: la constraint exige que los tres campos sean NULL.
        var pendienteConResolucion = await IntentarInsertarPorSqlAsync(
            contexto,
            empleado.Id,
            EstadoSolicitud.Pendiente,
            resueltoPorId: resolutor.Id,
            fechaResolucion: _momentoDeCreacion,
            motivoDeRechazo: null);
        AfirmarQueMencionaLaConstraintDeResolucion(pendienteConResolucion);

        // Aprobada sin quién ni cuándo resolvió.
        var aprobadaSinResolucion = await IntentarInsertarPorSqlAsync(
            contexto,
            empleado.Id,
            EstadoSolicitud.Aprobada,
            resueltoPorId: null,
            fechaResolucion: null,
            motivoDeRechazo: null);
        AfirmarQueMencionaLaConstraintDeResolucion(aprobadaSinResolucion);

        // Rechazada sin motivo.
        var rechazadaSinMotivo = await IntentarInsertarPorSqlAsync(
            contexto,
            empleado.Id,
            EstadoSolicitud.Rechazada,
            resueltoPorId: resolutor.Id,
            fechaResolucion: _momentoDeCreacion,
            motivoDeRechazo: null);
        AfirmarQueMencionaLaConstraintDeResolucion(rechazadaSinMotivo);

        await AfirmarQueNoQuedoNadaPersistidoAsync(empleado.Id);
    }

    [Fact]
    public async Task La_fk_de_resueltoPorId_no_hace_cascada()
    {
        // Camino triste del Bloque 1 de FEAT-002: igual que ya pasa con EmpleadoId, borrar a quien
        // resolvió una solicitud falla en vez de arrastrarla con él (DeleteBehavior.NoAction).
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var resolutor = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var solicitante = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await using (var contexto = _baseDeDatos.CrearContexto())
        {
            contexto.Solicitudes.Add(new Solicitud
            {
                EmpleadoId = solicitante.Id,
                FechaInicio = new DateOnly(2026, 1, 3),
                FechaFin = new DateOnly(2026, 1, 5),
                DiasCorridos = 3,
                Estado = EstadoSolicitud.Aprobada,
                FechaCreacion = _momentoDeCreacion,
                ResueltoPorId = resolutor.Id,
                FechaResolucion = _momentoDeCreacion,
            });

            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var contexto = _baseDeDatos.CrearContexto())
        {
            var empleadoAEliminar = await contexto.Empleados.SingleAsync(
                empleado => empleado.Id == resolutor.Id, TestContext.Current.CancellationToken);
            contexto.Empleados.Remove(empleadoAEliminar);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => contexto.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        // La solicitud de `solicitante` se borra al salir del alcance (EmpleadoDescartable), lo que
        // libera a `resolutor` antes de que este se borre a su vez: `await using` dispone en el
        // orden inverso al de declaración, así que `solicitante` (declarado después) se limpia
        // primero.
    }

    /// <summary>Servicio de migraciones del contexto, para migrar a un destino puntual (Block 5).</summary>
    private static IMigrator ObtenerMigrador(VacacionesDbContext contexto) =>
        ((IInfrastructure<IServiceProvider>)contexto.Database).Instance.GetRequiredService<IMigrator>();

    /// <summary>Columnas del índice indicado, en orden, como «Nombre:EsDescendente». Vacío si no existe.</summary>
    private async Task<List<string>> ColumnasDelIndiceAsync(VacacionesDbContext contexto, string nombreIndice) =>
        await contexto.Database
            .SqlQuery<string>($"""
                SELECT CONCAT(c.name, N':', ic.is_descending_key) AS Value
                FROM sys.indexes AS i
                INNER JOIN sys.index_columns AS ic
                    ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                INNER JOIN sys.columns AS c
                    ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID({TablaDeSolicitudes}) AND i.name = {nombreIndice}
                ORDER BY ic.key_ordinal
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

    /// <summary>Nombres de las check constraints de la tabla de solicitudes, en orden alfabético.</summary>
    private async Task<List<string>> ObtenerCheckConstraintsAsync(VacacionesDbContext contexto) =>
        await contexto.Database
            .SqlQuery<string>($"""
                SELECT name AS Value
                FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID({TablaDeSolicitudes})
                ORDER BY name
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

    /// <summary>
    /// Las tres columnas de resolución del Bloque 1 de FEAT-002, las que existan de verdad en la
    /// base en este momento, en orden alfabético. Vacío si ninguna existe (esquema anterior).
    /// </summary>
    private async Task<List<string>> ColumnasDeResolucionAsync(VacacionesDbContext contexto) =>
        await contexto.Database
            .SqlQuery<string>($"""
                SELECT name AS Value
                FROM sys.columns
                WHERE object_id = OBJECT_ID({TablaDeSolicitudes})
                  AND name IN (N'ResueltoPorId', N'FechaResolucion', N'MotivoDeRechazo')
                ORDER BY name
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

    private async Task<DbUpdateException> IntentarPersistirAsync(Solicitud solicitud)
    {
        await using var contexto = _baseDeDatos.CrearContexto();
        contexto.Solicitudes.Add(solicitud);

        return await Assert.ThrowsAsync<DbUpdateException>(
            () => contexto.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Inserta directamente por SQL (no vía <c>SaveChanges</c>), para que el fallo sea de la
    /// constraint en sí y no del mapeo de EF. El período (3-ene → 5-ene, 3 días) siempre es
    /// coherente con las otras cuatro constraints, así que la única que puede rechazar la fila es
    /// <see cref="VacacionesDbContext.ResolucionCoherente"/>.
    /// </summary>
    private async Task<SqlException> IntentarInsertarPorSqlAsync(
        VacacionesDbContext contexto,
        int empleadoId,
        EstadoSolicitud estado,
        int? resueltoPorId,
        DateTimeOffset? fechaResolucion,
        string? motivoDeRechazo)
    {
        var inicio = new DateOnly(2026, 1, 3);
        var fin = new DateOnly(2026, 1, 5);

        return await Assert.ThrowsAsync<SqlException>(() => contexto.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO dbo.Solicitudes
                (EmpleadoId, FechaInicio, FechaFin, DiasCorridos, Estado, FechaCreacion,
                 ResueltoPorId, FechaResolucion, MotivoDeRechazo)
            VALUES
                ({empleadoId}, {inicio}, {fin}, 3, {(int)estado}, {_momentoDeCreacion},
                 {resueltoPorId}, {fechaResolucion}, {motivoDeRechazo})
            """,
            TestContext.Current.CancellationToken));
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

    private static void AfirmarQueMencionaLaConstraintDeResolucion(SqlException excepcion) =>
        Assert.Contains(VacacionesDbContext.ResolucionCoherente, excepcion.Message, StringComparison.Ordinal);
}
