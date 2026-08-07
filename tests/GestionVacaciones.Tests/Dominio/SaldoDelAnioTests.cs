using GestionVacaciones.Data.Entidades;
using GestionVacaciones.Data.Services;
using GestionVacaciones.Tests.Persistencia;
using Xunit;

namespace GestionVacaciones.Tests.Dominio;

/// <summary>
/// Bloque 2 de FEAT-001b: el saldo de un año calendario — 14 menos los días imputados a ese año por
/// solicitudes <c>Pendiente</c> o <c>Aprobada</c> (FR-03, AC-04).
/// </summary>
/// <remarks>
/// <para>
/// Necesitan la instancia SQL Server 2022 del entorno, porque lo que se afirma es el resultado de una
/// consulta real: el filtro por empleado, estado y rango de fechas viaja a la base, y solo el reparto
/// por año ocurre en memoria. Si no hay cadena configurada se saltean con motivo explícito. Cada caso
/// crea su propio empleado y lo borra al terminar.
/// </para>
/// <para>
/// <b>Aviso para cuando alguno de estos casos se ponga en rojo:</b> los asertos comparan el
/// <c>record SaldoDelAnio</c> completo, y su <c>ToString()</c> imprime <b>solo el año</b> a propósito —los
/// días son un dato personal (R-12/R-14)—. El mensaje de xUnit va a mostrar dos cadenas idénticas y no en
/// qué difieren. Los números esperados están en el comentario de cada caso; el precio de no filtrar el
/// saldo a un log se paga acá.
/// </para>
/// </remarks>
[Collection(ColeccionDeBaseDeDatos.Nombre)]
[Trait(CategoriaDeTest.Clave, CategoriaDeTest.Integracion)]
public sealed class SaldoDelAnioTests
{
    /// <summary>Reloj detenido en 2026, con desplazamiento distinto de cero.</summary>
    private static readonly DateTimeOffset _instanteEn2026 =
        new(2026, 8, 3, 9, 15, 30, TimeSpan.FromHours(-3));

    private readonly BaseDeDatosDeTest _baseDeDatos;

    public SaldoDelAnioTests(BaseDeDatosDeTest baseDeDatos) => _baseDeDatos = baseDeDatos;

    private static CancellationToken Cancelacion => TestContext.Current.CancellationToken;

    [Fact]
    public async Task El_saldo_descuenta_los_dias_aprobados_y_los_pendientes()
    {
        // AC-04, y el punto que el PRD marca como riesgo: las pendientes RESERVAN saldo. Si contaran
        // solo las aprobadas, un empleado podría pedir 14 días hoy y otros 14 mañana mientras las
        // primeras esperan resolución, que es la sobreasignación que este ticket viene a impedir.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), EstadoSolicitud.Aprobada);
        await SembrarAsync(empleado.Id, new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 6), EstadoSolicitud.Pendiente);

        var saldo = await ServicioPara(empleado.Id).DeLosAniosAsync([2026], Cancelacion);

        // 5 aprobados + 3 pendientes = 8 usados, y el resto del tope disponible. El número disponible se
        // DERIVA de TopeAnual.Dias por el motivo que ya está escrito más abajo, en
        // El_saldo_de_un_anio_sin_solicitudes_es_el_tope_completo: repetirlo acá pondría el test en rojo
        // por la razón equivocada el día que la normativa cambie el tope.
        Assert.Equal(new SaldoDelAnio(2026, 8, TopeAnual.Dias - 8), saldo[0]);
    }

    [Fact]
    public async Task El_saldo_ignora_las_rechazadas()
    {
        // La contracara del anterior. Una solicitud rechazada no consumió nada: si contara, el empleado
        // pagaría por habérsela pedido. El PRD no lo enuncia como criterio pero la fórmula lo implica,
        // y una fórmula implícita sin test es una fórmula que alguien va a cambiar sin darse cuenta.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 11), EstadoSolicitud.Rechazada);

        var saldo = await ServicioPara(empleado.Id).DeLosAniosAsync([2026], Cancelacion);

        Assert.Equal(new SaldoDelAnio(2026, 0, TopeAnual.Dias), saldo[0]);
    }

    [Fact]
    public async Task Una_solicitud_a_caballo_descuenta_de_cada_anio_lo_suyo()
    {
        // AC-01 y AC-04 juntos, con el ejemplo numérico del PRD: del 28-dic-2026 al 5-ene-2027 son 9
        // días corridos, 4 contra 2026 y 5 contra 2027. Es el caso que obligó a decidir la imputación
        // por año, y el único donde una misma fila mueve dos saldos.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await SembrarAsync(empleado.Id, new DateOnly(2026, 12, 28), new DateOnly(2027, 1, 5), EstadoSolicitud.Pendiente);

        var saldos = await ServicioPara(empleado.Id).DeLosAniosAsync([2026, 2027], Cancelacion);

        Assert.Equal(new SaldoDelAnio(2026, 4, TopeAnual.Dias - 4), saldos[0]);
        Assert.Equal(new SaldoDelAnio(2027, 5, TopeAnual.Dias - 5), saldos[1]);
    }

    [Fact]
    public async Task El_saldo_de_un_anio_sin_solicitudes_es_el_tope_completo()
    {
        // El punto de partida de todo empleado, y el borde superior del saldo. Se afirma contra
        // TopeAnual.Dias y no contra un 14 escrito acá: si el test repitiera el número, cambiar el tope
        // dejaría el test en rojo por la razón equivocada.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var saldo = await ServicioPara(empleado.Id).DeLosAniosAsync([2026], Cancelacion);

        Assert.Equal(new SaldoDelAnio(2026, 0, TopeAnual.Dias), saldo[0]);
    }

    [Fact]
    public async Task Un_periodo_mas_largo_que_el_tope_deja_el_saldo_en_cero_y_los_usados_a_la_vista()
    {
        // El contrato del acotado a 0, que hasta ahora estaba solo documentado. FEAT-001a acepta un
        // período de cualquier duración —lo declara el PRD—, así que un empleado puede tener 20 días
        // imputados a un año contra un tope de 14: no es hipotético, es el estado que este mismo ticket
        // hereda.
        //
        // Lo que se afirma son las dos mitades juntas: DiasDisponibles va a 0 y no a −6, porque «te quedan
        // −6 días» no significa nada para quien lo lee; y DiasUsados conserva el 20, que es donde la
        // anomalía queda visible para quien pueda interpretarla. La cobertura no delata este hueco: el
        // Math.Max es una llamada y no una rama, así que la línea se cuenta como ejecutada aunque nadie
        // haya pasado nunca por el caso en que recorta.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        var inicio = new DateOnly(2026, 4, 1);

        await SembrarAsync(empleado.Id, inicio, inicio.AddDays(19), EstadoSolicitud.Aprobada);

        var saldo = await ServicioPara(empleado.Id).DeLosAniosAsync([2026], Cancelacion);

        Assert.Equal(new SaldoDelAnio(2026, 20, 0), saldo[0]);
    }

    [Fact]
    public async Task El_anio_en_curso_es_el_local_y_no_el_de_UTC()
    {
        // El borde que hace observable la elección de GetLocalNow() sobre GetUtcNow(). Con el reloj a las
        // 21:30 del 31 de diciembre en UTC-3, el año LOCAL es 2026 y el de UTC ya es 2027: es el mismo
        // desdoblamiento que el alta fija para la fecha de creación, y sin este caso cambiar la llamada a
        // GetUtcNow() no rompería nada, porque el instante de los demás casos es de agosto al mediodía.
        //
        // El empleado que consulta su saldo a las nueve y media de la noche del 31 de diciembre tiene que
        // ver los días del año que está terminando —los que todavía puede tomarse— y no los del que
        // todavía no empezó.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), EstadoSolicitud.Aprobada);
        await SembrarAsync(empleado.Id, new DateOnly(2027, 3, 2), new DateOnly(2027, 3, 4), EstadoSolicitud.Pendiente);

        var enLaNocheDelUltimoDia = new TiempoFijo(
            new DateTimeOffset(2026, 12, 31, 21, 30, 0, TimeSpan.FromHours(-3)));

        // La precondición del caso, escrita para que nadie la tenga que deducir: si algún día el
        // desplazamiento del instante cambiara, el test dejaría de estar sobre el borde y habría que
        // saberlo acá y no descubrirlo en verde.
        Assert.Equal(2026, enLaNocheDelUltimoDia.Ahora.Year);
        Assert.Equal(2027, enLaNocheDelUltimoDia.GetUtcNow().Year);

        var saldo = await ServicioPara(empleado.Id, enLaNocheDelUltimoDia).DelAnioEnCursoAsync(Cancelacion);

        Assert.Equal(new SaldoDelAnio(2026, 5, TopeAnual.Dias - 5), saldo);
    }

    [Fact]
    public async Task Las_solicitudes_de_otro_empleado_no_tocan_mi_saldo()
    {
        // El filtro por empleado, afirmado desde afuera. Sin él, dos personas con vacaciones el mismo
        // mes se descontarían días entre sí — y el síntoma sería un saldo que baja solo, casi imposible
        // de diagnosticar mirando las solicitudes propias.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var mio = await _baseDeDatos.CrearEmpleadoDescartableAsync();
        await using var ajeno = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await SembrarAsync(ajeno.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 11), EstadoSolicitud.Aprobada);

        var saldo = await ServicioPara(mio.Id).DeLosAniosAsync([2026], Cancelacion);

        Assert.Equal(TopeAnual.Dias, saldo[0].DiasDisponibles);
    }

    [Fact]
    public async Task El_anio_en_curso_sale_del_TimeProvider_y_no_del_reloj()
    {
        // Con el mismo empleado y los mismos datos, dos relojes distintos dan dos saldos distintos. Es
        // lo que hace verificable el reinicio del saldo cada 1 de enero sin esperar a que llegue, y lo
        // que impide que esta suite empiece a fallar sola el año que viene.
        _baseDeDatos.SaltearSiNoEstaDisponible();
        await using var empleado = await _baseDeDatos.CrearEmpleadoDescartableAsync();

        await SembrarAsync(empleado.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 6), EstadoSolicitud.Aprobada);

        var en2026 = await ServicioPara(empleado.Id, new TiempoFijo(_instanteEn2026))
            .DelAnioEnCursoAsync(Cancelacion);

        var en2027 = await ServicioPara(empleado.Id, new TiempoFijo(_instanteEn2026.AddYears(1)))
            .DelAnioEnCursoAsync(Cancelacion);

        Assert.Equal(new SaldoDelAnio(2026, 5, TopeAnual.Dias - 5), en2026);
        Assert.Equal(new SaldoDelAnio(2027, 0, TopeAnual.Dias), en2027);
    }

    private async Task SembrarAsync(int empleadoId, DateOnly inicio, DateOnly fin, EstadoSolicitud estado)
    {
        await using var contexto = _baseDeDatos.CrearContexto();

        contexto.Solicitudes.Add(new Solicitud
        {
            EmpleadoId = empleadoId,
            FechaInicio = inicio,
            FechaFin = fin,

            // El mismo cálculo que el dominio: CK_Solicitud_DiasCoincidenConPeriodo rechaza cualquier
            // otro número, así que la siembra no puede inventarlo.
            DiasCorridos = CalculadorDeDiasCorridos.Contar(inicio, fin),
            Estado = estado,
            FechaCreacion = _instanteEn2026,

            // Desde el Bloque 1 de FEAT-002, CK_Solicitud_ResolucionCoherente exige estos tres datos
            // cuando el estado no es Pendiente. Quién "resolvió" no es lo que este test verifica (el
            // saldo), así que se reutiliza el propio empleado.
            ResueltoPorId = estado == EstadoSolicitud.Pendiente ? null : empleadoId,
            FechaResolucion = estado == EstadoSolicitud.Pendiente ? null : _instanteEn2026,
            MotivoDeRechazo = estado == EstadoSolicitud.Rechazada ? "Motivo de prueba" : null,
        });

        await contexto.SaveChangesAsync(Cancelacion);
    }

    private SaldoService ServicioPara(int empleadoId, TimeProvider? tiempo = null) =>
        new(new FabricaDeLaBaseDeTest(_baseDeDatos),
            IdentidadDePrueba.De(empleadoId),
            new PermisosService(),
            tiempo ?? new TiempoFijo(_instanteEn2026));
}
