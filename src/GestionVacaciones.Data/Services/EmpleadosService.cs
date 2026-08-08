using Microsoft.EntityFrameworkCore;

namespace GestionVacaciones.Data.Services;

/// <summary>
/// Empleado tal como lo necesita el selector de identidad: quién es y cómo se llama.
/// </summary>
/// <remarks>
/// Es una proyección y no la entidad <c>Empleado</c> a propósito. El correo y el organigrama
/// —<c>ManagerId</c>, <c>DesignadoId</c>— son PII (F-TM-05) que el desplegable no necesita para nada, y
/// R-04 ya advierte que este servicio devuelve la plantilla completa a cualquiera que abra la
/// aplicación. Devolver solo lo que hace falta no reemplaza a la mitigación de R-04 —que es no existir
/// fuera de desarrollo— pero recorta lo que se expone si algún día existiera.
/// </remarks>
public sealed record EmpleadoDeLaNomina(int Id, string Nombre)
{
    /// <summary>
    /// Descripción para diagnóstico. Lleva el identificador y <b>nunca</b> el nombre, que es PII (R-12):
    /// esto termina en un log o en un mensaje de error.
    /// </summary>
    /// <remarks>
    /// <b>Por qué se escribe a mano.</b> El <c>ToString()</c> que genera un <c>record</c> imprime
    /// <i>todas</i> sus propiedades, o sea el nombre del empleado. Hoy nadie registra este objeto, pero
    /// el camino corto para loguear o para redactar un mensaje de error es interpolarlo entero, y por ahí
    /// la PII vuelve sin que se escriba una sola línea que lo delate en la revisión. Es la misma
    /// mitigación, por el mismo motivo, que <c>IdentidadDelEmpleado</c> aplica sobre el suyo.
    /// </remarks>
    public override string ToString() => $"{nameof(EmpleadoDeLaNomina)} {{ Id = {Id} }}";
}

/// <summary>
/// Lectura de la nómina: alimenta el desplegable del selector y responde si un identificador recibido
/// corresponde a alguien de verdad.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué existe.</b> <see cref="IEmpleadoActualProvider"/> no puede tocar el <c>DbContext</c>
/// —<c>AGENTS.md</c>: la UI y la identidad pasan siempre por los servicios de <c>Data/Services/</c>— y
/// sin este servicio el selector no tendría de dónde sacar la nómina ni contra qué validar el
/// identificador que llega del navegador.
/// </para>
/// <para>
/// Se registra bajo la <b>misma doble condición</b> que el proveedor de desarrollo (mitigación de
/// R-04): fuera de ahí no existe, así que no hay desde dónde volcar la plantilla.
/// </para>
/// <para>
/// Acceso a datos siempre con <see cref="IDbContextFactory{TContext}"/>, nunca con un <c>DbContext</c>
/// inyectado (NFR-05): cada operación abre y cierra el suyo.
/// </para>
/// </remarks>
public sealed class EmpleadosService
{
    private readonly IDbContextFactory<VacacionesDbContext> _fabrica;

    public EmpleadosService(IDbContextFactory<VacacionesDbContext> fabrica)
    {
        ArgumentNullException.ThrowIfNull(fabrica);

        _fabrica = fabrica;
    }

    /// <summary>
    /// La nómina completa, ordenada por nombre. El orden es parte del contrato: un desplegable que
    /// cambia de orden entre consultas es una trampa para quien elige rápido.
    /// </summary>
    public async Task<IReadOnlyList<EmpleadoDeLaNomina>> ListarNominaAsync(CancellationToken cancelacion = default)
    {
        await using var contexto = await _fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        return await contexto.Empleados
            .AsNoTracking()
            .OrderBy(empleado => empleado.Nombre)
            .Select(empleado => new EmpleadoDeLaNomina(empleado.Id, empleado.Nombre))
            .ToListAsync(cancelacion)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// ¿Existe ese empleado en la nómina? Es la validación del identificador que llega del navegador
    /// (TB-1, entrada no confiable).
    /// </summary>
    /// <remarks>
    /// Los identificadores no positivos se rechazan <b>sin consultar</b>: la clave es <c>identity</c> y
    /// arranca en 1, así que no hay nada que buscar y un flujo de valores basura no se convierte en un
    /// flujo de consultas. La comparación viaja parametrizada por EF Core: no hay SQL concatenado
    /// (R-08).
    /// </remarks>
    public async Task<bool> ExisteEnLaNominaAsync(int empleadoId, CancellationToken cancelacion = default)
    {
        if (empleadoId <= 0)
        {
            return false;
        }

        await using var contexto = await _fabrica.CreateDbContextAsync(cancelacion).ConfigureAwait(false);

        return await contexto.Empleados
            .AsNoTracking()
            .AnyAsync(empleado => empleado.Id == empleadoId, cancelacion)
            .ConfigureAwait(false);
    }
}
