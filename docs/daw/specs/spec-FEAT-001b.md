# Spec FEAT-001b: Imputación de días por año calendario, tope anual y saldo

| Field | Value |
|-------|-------|
| Ticket | FEAT-001b |
| PRD | `docs/daw/prd/prd-FEAT-001b.md` |
| Tier | FEATURE |
| Date | 2026-08-04 |
| Spec loops | 2 |

## Summary

El tope anual de 14 días se convierte en una regla que el sistema hace cumplir, y el saldo que la
hace visible se calcula en un único lugar consumido por los dos caminos: el que muestra y el que
bloquea. La pieza nueva se parte en dos por una razón estructural — una función **estática pura**
que imputa los días de un período a cada año calendario, hermana de `CalculadorDeDiasCorridos`, y un
**servicio inyectado** que la aplica sobre lo que devuelve la base. La validación del tope se engancha
en `SolicitudesService.CrearAsync` después de las validaciones de fecha, dentro de una transacción
serializable que cierra la carrera entre dos envíos simultáneos del mismo empleado. En pantalla, el
saldo del año en curso siempre, el del otro año cuando el período abarca dos, y un estado propio
—sin número— cuando el cálculo falla.

## Coverage: PRD → blocks

| Requirement | Covered by |
|---|---|
| FR-01 (imputar los días al año al que pertenece cada fecha) | Block 1 |
| FR-02 (validar el tope en cada año afectado) | Block 3 |
| FR-03 (calcular el saldo del año) | Block 2 |
| FR-04 (mostrar utilizados y disponibles del año en curso) | Block 4 |
| FR-05 (mostrar el saldo del otro año cuando el período cruza) | Block 4 |
| NFR-01 (el tope en 1 única declaración) | Strategy: `TopeAnual.Dias` es la única aparición del número en `src/`; un test de escaneo sobre todo `src/` lo verifica, como ya hacen `FuenteDeTiempoTests` y `ComposicionDeAccesoADatosTests` |
| NFR-02 (cobertura ≥ 80 % en líneas, ramas y funciones del código nuevo) | Strategy: gate `daw-test` con `--collect:"XPlat Code Coverage"`; el proyecto viene del 94,6 % y no baja |
| NFR-03 (p95 < 3 s con 50 concurrentes) | Strategy: índice `IX_Solicitud_EmpleadoId_Estado_FechaInicio` del Block 5, que es lo que hace que la consulta del saldo no escanee la tabla. **La verificación de carga queda diferida a un ticket de performance propio**, igual que en FEAT-001a: esta spec entrega la condición estructural, no la medición |
| NFR-04 (1 única función de cálculo, 0 alternativas) | Strategy: `SaldoService` es el único llamador de `ImputacionPorAnio` fuera de los tests, y el único productor de `SaldoDelAnio`. La imputación **no** se escribe en LINQ traducible a SQL: el filtro viaja a la base, el reparto por año ocurre en memoria. Un test de escaneo verifica que no exista una segunda implementación |

**AC → tests** está en el bloque de cada uno; el mapa completo se recompone en *Final verification*.

## Dependencies between blocks

```
Block 1  (imputación pura)
   └─> Block 2  (saldo y tope)
          ├─> Block 3  (el alta valida)
          └─> Block 4  (el saldo en pantalla)
Block 5  (índice y migración) — independiente; puede ir en cualquier momento,
          pero antes de cerrar CODE porque NFR-03 se apoya en él
```

Orden de ejecución: **1 → 2 → 3 → 4 → 5**. Los bloques 3 y 4 son independientes entre sí y solo
comparten el 2.

---

## Block 1 — Imputación por año calendario

**Files**
- `src/GestionVacaciones.Data/Services/ImputacionPorAnio.cs` (nuevo) — función pura que reparte los
  días de un período entre los años que abarca.
- `tests/GestionVacaciones.Tests/Dominio/ImputacionPorAnioTests.cs` (nuevo).

**Logic**

`public static class ImputacionPorAnio`, estático por la misma razón que
`CalculadorDeDiasCorridos` (`CalculadorDeDiasCorridos.cs:16-20`): sin instancia no puede haber dos
implementaciones registradas en el contenedor.

- `public static int DiasEnElAnio(DateOnly fechaInicio, DateOnly fechaFin, int anio)` — recorta el
  período contra `[1-ene-anio, 31-dic-anio]` y **delega el conteo a
  `CalculadorDeDiasCorridos.Contar`**. Devuelve `0` si no hay solapamiento. La fórmula del conteo no
  se reescribe: eso es lo que hace que NFR-04 valga también acá.
- `public static IReadOnlyList<int> AniosAbarcados(DateOnly fechaInicio, DateOnly fechaFin)` — los
  años calendario que toca el período, en orden ascendente.

Precondición idéntica a la de su hermana: **lanza `ArgumentOutOfRangeException` si
`fechaFin < fechaInicio`**. Un período invertido no tiene días que imputar, por la misma razón que no
tiene días corridos (`CalculadorDeDiasCorridos.cs:37-41`).

**Error handling**
- Período invertido → `ArgumentOutOfRangeException`, con el mismo argumento que la hermana: es una
  precondición incumplida del llamador, no un rechazo de validación.
- Sin solapamiento con el año pedido → `0`, que es una respuesta legítima y no un error.

**Required tests**
- [ ] `Un_periodo_a_caballo_de_dos_anios_imputa_a_cada_uno_lo_suyo` — 28-dic-2026 → 5-ene-2027:
  9 días corridos, 4 a 2026 y 5 a 2027 — **el ejemplo numérico de AC-01, literal**.
- [ ] `Los_dias_imputados_suman_los_dias_corridos_del_periodo` — para cada año abarcado, la suma
  coincide con `CalculadorDeDiasCorridos.Contar`. Valida AC-01 en su forma general.
- [ ] `Un_periodo_dentro_de_un_solo_anio_imputa_todo_a_ese_anio` — valida AC-01, caso simple.
- [ ] `Un_anio_ajeno_al_periodo_recibe_cero_dias` — camino triste: se pregunta por un año que el
  período no toca.
- [ ] `Un_anio_bisiesto_no_altera_el_conteo` — 29-feb dentro del período.
- [ ] `Un_periodo_invertido_lanza` — camino triste: precondición incumplida.
- [ ] `AniosAbarcados_devuelve_los_anios_en_orden_ascendente` — uno y dos años.

**Completion criterion**
Los 7 tests pasan, y borrar la llamada a `CalculadorDeDiasCorridos.Contar` dentro de
`DiasEnElAnio` —sustituyéndola por una resta propia— no deja la suite en verde.

---

## Block 2 — Saldo del año y tope anual

**Files**
- `src/GestionVacaciones.Data/Services/TopeAnual.cs` (nuevo) — la única declaración del número 14.
- `src/GestionVacaciones.Data/Services/SaldoService.cs` (nuevo) — el servicio inyectado y el
  `record SaldoDelAnio`.
- `src/GestionVacaciones.Data/Services/ErroresDeSolicitud.cs` (modificado) — los dos formatos nuevos
  y el compositor.
- `src/GestionVacaciones.Web/Program.cs` (modificado) — `RegistrarDominio`, `:217-223`.
- `tests/GestionVacaciones.Tests/Componentes/ComposicionDeLaPantalla.cs` (modificado) — `:30-44`,
  **la segunda sede de DI**.
- `tests/GestionVacaciones.Tests/Dominio/FuenteDeTiempoTests.cs` (modificado) — `:79-93`, la lista
  de servicios de dominio registrados.
- `AGENTS.md` (modificado) — *Architecture conventions*, la enumeración de `Data/Services/`.
- `tests/GestionVacaciones.Tests/Dominio/SaldoDelAnioTests.cs` (nuevo).
- `tests/GestionVacaciones.Tests/Dominio/MensajesLiteralesDeFeat001bTests.cs` (nuevo) — hermano de
  `MensajesLiteralesDelPrdTests`, que hoy apunta solo a `prd-FEAT-001a.md` (`:32`).
- `tests/GestionVacaciones.Tests/Dominio/PuntoUnicoDelTopeTests.cs` (nuevo) — NFR-01 y NFR-04.

**Logic**

`TopeAnual.Dias = 14` como `const int`. **Es la única aparición del número en `src/`**, y de ahí sale
tanto el bloqueo como el saldo.

`SaldoService`, `sealed`, con las mismas cuatro dependencias que `SolicitudesService`
(`SolicitudesService.cs:174-189`): `IDbContextFactory<VacacionesDbContext>`,
`IEmpleadoActualProvider`, `PermisosService` y `TimeProvider`.

- `public sealed record SaldoDelAnio(int Anio, int DiasUsados, int DiasDisponibles)` —
  `DiasDisponibles = TopeAnual.Dias - DiasUsados`, nunca negativo por construcción del cálculo.
  **Lleva el año adentro**: es lo que permite que la pantalla lo muestre sin saber en qué año está
  (decisión D, forzada por `ComponentesSinAccesoADatosTests.cs:36-37`, que prohíbe el token
  `TimeProvider` en cualquier `.razor`).
- `public async Task<SaldoDelAnio> DelAnioEnCursoAsync(CancellationToken)` — el año sale de
  `_tiempo.GetLocalNow()`, **local y no UTC**, por el mismo argumento que ya está escrito en
  `SolicitudesService.cs:202-207`.
- `public async Task<IReadOnlyList<SaldoDelAnio>> DeLosAniosAsync(IReadOnlyList<int> anios, CancellationToken)`
  — para FR-05.

Ambos preguntan a `PermisosService.ExigirPoderVerLasSolicitudesDe` antes de consultar, como ya hace
`ListarPropiasAsync` (`SolicitudesService.cs:301`): el saldo de un empleado es tan suyo como su
listado, y la sede de esa decisión es una sola.

> **Ninguna firma pública acepta un identificador de empleado** (mitigación de **R-13** del modelo
> de amenazas). El sujeto sale siempre de `IEmpleadoActualProvider`. Un
> `SaldoDeAsync(int empleadoId, int anio)` sería la referencia directa a objeto insegura de siempre:
> cualquier circuito preguntando por cualquiera, con la única defensa de que todos los llamadores se
> acuerden de validar. El día que el manager necesite ver a su equipo, esa capacidad se agrega **con**
> su comprobación en `PermisosService`, no destapando un parámetro.

**La consulta** filtra en SQL por `EmpleadoId`, `Estado ∈ {Pendiente, Aprobada}` y solapamiento del
período con el rango del año (`FechaInicio <= 31-dic-anio && FechaFin >= 1-ene-anio`), trae
`FechaInicio`/`FechaFin`, y **imputa en memoria** con `ImputacionPorAnio.DiasEnElAnio`. La imputación
no se escribe dentro del `Where`/`Sum`: un árbol de expresión traducible sería una segunda
implementación de la regla, que es lo que NFR-04 cuenta en cero. El precedente del proyecto solo
admite duplicar una fórmula en SQL como check constraint defensiva
(`VacacionesDbContext.cs:123-133`), nunca como camino de lectura.

**«Días tomados» ≡ `Aprobada`** (decisión del PLAN): el glosario usa dos palabras para una sola cosa
y el modelo tiene tres estados, ninguno «Tomada». No se toca `EstadoSolicitud` ni
`CK_Solicitud_EstadoValido`.

**Los literales.** `ErroresDeSolicitud` gana dos `const` con marcadores y un compositor:

```
SaldoInsuficiente        = "No dispones de días suficientes. Tu saldo actual es de {0} días"
SaldoInsuficienteDosAnios = "No dispones de días suficientes. Tu saldo actual es de {0} días en {1} y de {2} días en {3}"
```

más `public static string ComponerSaldoInsuficiente(...)`. El compositor existe porque
`DiagnosticoSinPiiTests.cs:174-179` lee los `const` **por reflexión** y un mensaje armado con
`string.Format` disperso por el código se le escapa entero.

**Input validation**
- `DeLosAniosAsync` con la lista vacía → lista vacía, sin consultar.
- Año fuera de `[1, 9999]` → `ArgumentOutOfRangeException`: construir `new DateOnly(anio, 1, 1)`
  fallaría con una excepción que no dice de qué parámetro vino.
- **`DeLosAniosAsync` acepta como máximo 2 años por llamada** → más es
  `ArgumentOutOfRangeException`, no una consulta cara. Mitigación de **R-15**: el conjunto de años lo
  elige el cliente (frontera TB-6) y `DateOnly` llega hasta el 9999, así que sin este límite un
  circuito sin autenticar puede pedir un rango que ningún índice acota. Dos es el máximo que un
  período legítimo necesita, porque uno de tres años ya no cabe en el tope.

**Error handling**
- Sin identidad resuelta → se propaga desde el proveedor, **no se degrada a saldo cero**. Un cero y
  un error se ven igual en pantalla y significan lo contrario (AC-07).
- `PermisosService` niega → `AccesoASolicitudesDenegadoException`, sin degradar.
- Fallo de persistencia → se propaga. Sin `catch` en el servicio, como el resto del dominio
  (`SolicitudesService.cs:150-151`).

**Required tests**
- [ ] `El_saldo_descuenta_los_dias_aprobados_y_los_pendientes` — valida AC-04, y fija que Pendiente
  reserva.
- [ ] `El_saldo_ignora_las_rechazadas` — valida AC-04, camino que el enunciado no dice pero la
  fórmula implica.
- [ ] `Una_solicitud_a_caballo_descuenta_de_cada_anio_lo_suyo` — valida AC-01 y AC-04 juntos, con el
  ejemplo de AC-01.
- [ ] `El_saldo_de_un_anio_sin_solicitudes_es_el_tope_completo` — 14.
- [ ] `El_anio_en_curso_sale_del_TimeProvider_y_no_del_reloj` — con `TiempoFijo` en dos años
  distintos.
- [ ] `Sin_identidad_el_saldo_no_se_degrada_a_cero` — camino triste, valida la mitad de AC-07.
- [ ] `Permisos_negados_no_se_degradan_a_cero` — camino triste.
- [ ] `Un_anio_invalido_lanza_indicando_el_parametro` — camino triste.
- [ ] `Un_fallo_de_persistencia_se_propaga_en_vez_de_devolver_saldo_cero` — camino triste: la
  excepción sale del servicio sin `catch`. Un cero silencioso ante una base caída es la trampa que
  AC-07 existe para impedir.
- [ ] `Una_lista_de_anios_vacia_no_consulta_la_base` — camino triste, montado con
  `FabricaQueNadieDebeUsar`: si el servicio abriera contexto, el test lanza.
- [ ] `Mas_de_dos_anios_por_llamada_lanza_sin_consultar` — camino triste. Mitigación de R-15.
- [ ] `Ninguna_firma_publica_acepta_un_empleadoId` — por reflexión sobre los métodos públicos de
  `SaldoService`. Mitigación de **R-13**: es el test que impide que alguien agregue la sobrecarga
  «para reusar» cuando llegue el ticket del manager.
- [ ] `Los_literales_coinciden_con_prd_FEAT_001b` — lee `docs/daw/prd/prd-FEAT-001b.md` y compara
  los entrecomillados de AC-02 y AC-03 contra los `const`. **Los marcadores `X`, `{año1}` y `{año2}`
  del PRD se normalizan contra `{0}`…`{3}` antes de comparar** — es el hueco que
  `MensajesLiteralesDelPrdTests.cs:42-43` no cubre, porque su `Assert.Contains` compara la cadena
  cruda.
- [ ] `El_mensaje_compuesto_no_lleva_identificadores_de_persona` — ejecuta el compositor con
  centinelas y verifica que la salida no contiene nombre, legajo ni `EmpleadoId`. Cierra el hueco de
  `DiagnosticoSinPiiTests.cs:174-179`.
- [ ] `El_numero_14_aparece_una_sola_vez_en_src` — escaneo de todo `src/`, en la línea de
  `FuenteDeTiempoTests.cs:37-76`. Valida NFR-01.
- [ ] `Solo_SaldoService_consume_ImputacionPorAnio` — escaneo de `src/`. Valida NFR-04.

**Completion criterion**
Los 16 tests pasan; `SaldoService` está registrado en **las dos** sedes de DI y quitarlo de
`ComposicionDeLaPantalla.cs` rompe tests; y cambiar `TopeAnual.Dias` a 20 mueve el saldo de todos los
tests que lo afirman sin tocar ninguna otra línea de `src/`.

---

## Block 3 — El alta hace cumplir el tope

**Files**
- `src/GestionVacaciones.Data/Services/SolicitudesService.cs` (modificado) — quinta dependencia, la
  validación entre `:242` y `:244`, y la transacción alrededor de `:263-266`.
- `tests/GestionVacaciones.Tests/Dominio/AltaDeSolicitudTests.cs` (modificado) — `:110-135`.
- `tests/GestionVacaciones.Tests/Dominio/TopeAnualEnElAltaTests.cs` (nuevo).

**Logic**

`SolicitudesService` recibe `SaldoService` como quinta dependencia. La validación del tope va
**después** de las dos de fecha y **antes** de construir la entidad, es decir entre las líneas
actuales `:242` y `:244`.

**Ese orden es contrato, no detalle.** Cuatro casos de `ValidacionDelAltaTests.cs:37-99` montan el
servicio con `FabricaQueNadieDebeUsar` (`FabricasDeContexto.cs:17-22`), que **lanza en cuanto alguien
pide un contexto**: si la consulta del saldo se hiciera antes que las validaciones de fecha, los
cuatro reventarían. Que un período invertido se rechace sin tocar la base pasa a estar fijado por
esos tests, y hay que decirlo en el código.

Para cada año que `ImputacionPorAnio.AniosAbarcados` devuelva, se compara los días imputados contra
el saldo de ese año. Si alguno no alcanza:
- un solo año afectado → `ErroresDeSolicitud.SaldoInsuficiente` con el saldo de ese año (AC-02);
- dos años afectados → el mensaje desglosado (AC-03), **aunque solo uno de los dos no alcance**: el
  desglose existe para que el empleado vea contra qué se lo comparó.

**Dos cosas más que exige el modelo de amenazas.** `ResultadoDelAlta.ToString()`
(`SolicitudesService.cs:81-84`) **deja de incluir `MensajeDeError`** cuando el rechazo viene del tope:
ese texto lleva el saldo, y un saldo junto al `EmpleadoId` que ya se registra dice cuánto se ausentó
una persona identificable (**R-14**). Y el rechazo por tope **se registra en el log** a nivel
information con `EmpleadoId`, el año afectado y los días solicitados —nunca el mensaje compuesto—,
porque este ticket introduce la primera decisión del sistema en contra de un empleado y hoy no
quedaría rastro de por qué (**R-18**).

**La transacción.** `CrearAsync` abre `BeginTransactionAsync(IsolationLevel.Serializable)` que
envuelve *leer el saldo y guardar*. Sin ella, dos pestañas del mismo empleado leen el mismo saldo y
las dos pasan, y el tope anual **no es expresable como check constraint de fila**, así que la base no
puede atraparlo como sí atrapa un período invertido. El proyecto ya usa `BeginTransactionAsync`
cuando la invariante lo pide (`SeedDatos.cs:279-281`).

**Input validation**

El período llega del formulario como dos `DateOnly`. Las dos validaciones de fecha que ya existen no
cambian: inicio no anterior a hoy (AC-02) y fin no anterior a inicio (AC-03), en ese orden.

**El período está acotado sin necesidad de un máximo arbitrario.** Si
`ImputacionPorAnio.AniosAbarcados` devuelve **más de dos años**, hay al menos un año calendario
íntegro dentro del período, y un año íntegro imputa 365 o 366 días, que supera el tope de 14 en
cualquier saldo posible. Ese caso **se rechaza sin consultar la base**, con el mensaje de AC-02
correspondiente al primer año afectado. Así, la consulta del saldo nunca recorre más de dos años y
una entrada absurda no cuesta un viaje a la base.

**Error handling**
- Tope superado → `ResultadoDelAlta.Rechazada(...)`, no excepción. Es validación de usuario y viaja
  por el tipo resultado que ya existe (`SolicitudesService.cs:10-23`).
- Período de más de dos años → rechazo, resuelto en memoria (ver *Input validation*).
- Fallo de persistencia dentro de la transacción → rollback y se propaga, sin `catch`.
- Conflicto de serialización de SQL Server (error 1205, deadlock víctima) → **se propaga, y no se
  reintenta en bucle**. Es un fallo de infraestructura, no del período: convertirlo en un rechazo le
  mostraría al empleado un mensaje de validación por una colisión del motor, y reintentar
  automáticamente ante contención amplifica la carga en vez de aliviarla (**R-16**).
- Rechazo por tope → además del `ResultadoDelAlta`, una entrada de log sin el mensaje compuesto
  (**R-18**).

**Required tests**
- [ ] `Una_solicitud_que_cabe_en_el_saldo_se_crea` — camino feliz.
- [ ] `Una_solicitud_que_supera_el_tope_se_rechaza_con_el_mensaje_de_AC_02` — literal exacto, un año.
- [ ] `Una_solicitud_a_caballo_que_supera_en_un_anio_se_rechaza_con_el_desglose_de_AC_03` — literal
  exacto, dos años. Valida AC-03.
- [ ] `Las_pendientes_reservan_saldo_y_bloquean_la_siguiente` — valida FR-02 sobre el caso que el PRD
  marca como riesgo.
- [ ] `El_tope_se_valida_despues_de_las_fechas_y_sin_tocar_la_base` — con `FabricaQueNadieDebeUsar`:
  un período invertido se rechaza sin abrir contexto. **Fija el orden como contrato.**
- [ ] `Justo_14_dias_se_acepta_y_15_no` — el borde exacto del tope.
- [ ] `Dos_altas_concurrentes_no_superan_el_tope` — dos `CrearAsync` en paralelo contra la instancia
  real; la suma de lo creado no pasa de 14. Valida la mitigación de la carrera.
- [ ] `Un_periodo_de_mas_de_dos_anios_se_rechaza_sin_tocar_la_base` — camino triste, con
  `FabricaQueNadieDebeUsar`: fija que el atajo de *Input validation* ocurre antes de consultar.
- [ ] `Un_fallo_al_guardar_revierte_la_transaccion_y_se_propaga` — camino triste: tras la excepción,
  la base no conserva la solicitud a medio crear.
- [ ] `Una_excepcion_de_base_que_no_es_de_validacion_se_propaga_sin_convertirse_en_rechazo` — camino
  triste, y es el que cubre el conflicto de serialización. **Testea la decisión, no el motor:** se
  inyecta la excepción en lugar de provocar un deadlock real, porque lo que puede romperse por
  descuido es que alguien envuelva el bloque en un `catch` y devuelva `Rechazada`, no que SQL Server
  deje de detectar deadlocks. Un test que fuerza un interbloqueo real contra la instancia es de los
  que se vuelven intermitentes y terminan deshabilitados, y un test deshabilitado protege menos que
  ninguno.
- [ ] `El_ToString_del_resultado_no_expone_el_saldo` — camino triste. Mitigación de **R-14**: el
  diagnóstico que termina en el log no puede llevar los días que le quedan a una persona
  identificable.
- [ ] `El_rechazo_por_tope_queda_registrado_sin_el_mensaje` — mitigación de **R-18**: hay entrada de
  log con `EmpleadoId` y año, y no está el texto compuesto.
- [ ] **Modificado** — `AltaDeSolicitudTests.cs:110-135`,
  `Un_periodo_de_duracion_arbitraria_se_acepta_en_este_ticket`: se **jubila explícitamente**. Su
  comentario (`:112-116`) dice que existe para que nadie arregle de paso lo que FEAT-001b tiene que
  entregar, así que se reemplaza por `Un_periodo_de_300_dias_ahora_se_rechaza_por_el_tope`, con una
  nota que registre que este ticket es el que lo jubiló. **No se borra en silencio.**

**Completion criterion**
Los 13 tests pasan; los 4 casos de `ValidacionDelAltaTests` siguen verdes sin tocarlos; y quitar la
transacción deja `Dos_altas_concurrentes_no_superan_el_tope` en rojo.

---

## Block 4 — El saldo en pantalla

**Files**
- `src/GestionVacaciones.Web/Components/Solicitudes/SaldoDelEmpleado.razor` (nuevo).
- `src/GestionVacaciones.Web/Components/Solicitudes/FormularioDeAlta.razor` (modificado) — un evento
  que avisa que el período cambió.
- `src/GestionVacaciones.Web/Components/Pages/MisSolicitudes.razor` (modificado) — rama
  `EstadoListado` (`:40-52`) y `RecargarAsync` (`:105-145`).
- `tests/GestionVacaciones.Tests/Componentes/SaldoEnPantallaTests.cs` (nuevo).

**Logic**

Componente nuevo en la rama `EstadoListado`, **antes** de `<FormularioDeAlta>` (`:47`): es el dato
que el empleado necesita para decidir cuántos días pedir.

- Muestra `SaldoDelAnio` del año en curso: días utilizados o reservados, y días disponibles (AC-05).
- Cuando el período que hay en el formulario abarca dos años, muestra también el del otro (AC-06).
  Los años los resuelve `ImputacionPorAnio.AniosAbarcados`, **una función pura sin reloj y sin
  datos**, exactamente como `FormularioDeAlta.razor:134` ya llama a `CalculadorDeDiasCorridos.Contar`.
  El componente no sabe en qué año está: el año viaja dentro de `SaldoDelAnio`.
- `FormularioDeAlta` expone `OnPeriodoCambiado`, que **solo comunica las dos fechas**. No decide
  nada: la regla la sigue decidiendo el servidor.

**El botón de enviar no se toca.** Su condición sigue siendo «están las dos fechas» y no «el período
es válido» (`FormularioDeAlta.razor:96-103`, mitigación R-10), y
`FormularioDeAltaTests.cs:119-122` lo fija con un aserto. Mostrar el saldo al lado del formulario es
la tentación directa de bloquear desde el cliente; la interfaz muestra el resultado de una regla,
no la decide.

**El cuarto estado.** El saldo tiene su propio `data-estado` y su propio `data-testid`, distintos de
los tres que ya publica `MisSolicitudes.razor:61-79`. Si el cálculo falla, se ve «no pudimos calcular
tu saldo» **sin ninguna cantidad de días**, y el listado y el formulario siguen utilizables (AC-07).
Su consulta va en su propio `try`, no dentro del `try` de `RecargarAsync` (`:107`), cuyo `catch`
(`:133`) pinta `EstadoError` para la pantalla entera.

Los `data-testid` se publican como `const string` públicas del componente, según la convención de
`ListadoDeSolicitudes.razor:41-62`, y no se comparten con otro componente de la misma pantalla
(`:53-56`).

**Input validation**

El componente recibe las dos fechas del formulario como `DateOnly?`: mientras alguna sea `null` no
pide ningún saldo salvo el del año en curso, que no depende del período.

**Con las dos fechas presentes, solo llama a `ImputacionPorAnio.AniosAbarcados` si el período está en
orden.** Esa función lanza con un período invertido, igual que su hermana
(`CalculadorDeDiasCorridos.cs:37-41`), y comprobar el orden antes de contar es el mismo deber que ya
tiene `FormularioDeAlta.razor:134` al mostrar los días corridos. **No es la interfaz validando la
regla:** el rechazo del período invertido lo sigue emitiendo el servidor con el literal de AC-03; acá
solo se evita pedir un cálculo cuya precondición no se cumple.

**Error handling**
- Fallo del cálculo → cuarto estado, sin número. **Nunca «0 días disponibles»**: le diría al empleado
  que se quedó sin días mientras la base está caída.
- Sin empleado seleccionado → el saldo no se renderiza; esa rama ya la resuelve la pantalla antes
  (`:28-33`).
- Período invertido en el formulario → no se pide el saldo del segundo año; se sigue mostrando el del
  año en curso.

**Required tests**
- [ ] `El_saldo_del_anio_en_curso_se_muestra_al_entrar` — valida AC-05.
- [ ] `Se_muestran_utilizados_y_disponibles_por_separado` — valida AC-05.
- [ ] `Un_periodo_que_cruza_el_anio_muestra_los_dos_saldos` — valida AC-06.
- [ ] `Un_periodo_dentro_del_anio_muestra_un_solo_saldo` — valida AC-06 por su contracara.
- [ ] `Si_el_calculo_falla_no_se_muestra_ninguna_cantidad` — valida AC-07.
- [ ] `Si_el_calculo_falla_el_listado_y_el_alta_siguen_usables` — valida AC-07.
- [ ] `El_estado_del_saldo_se_distingue_de_los_otros_tres` — marcadores distintos. Valida AC-07.
- [ ] `El_boton_de_enviar_sigue_habilitado_sin_saldo_suficiente` — camino triste: la interfaz **no**
  decide la regla.
- [ ] `Ningun_razor_nombra_TimeProvider` — se extiende `ComponentesSinAccesoADatosTests` al
  componente nuevo.
- [ ] `Sin_empleado_seleccionado_el_saldo_no_se_renderiza` — camino triste: la pantalla está en
  `sin-empleado` y el marcador del saldo no aparece.
- [ ] `Un_periodo_invertido_no_pide_el_saldo_del_segundo_anio` — camino triste: se sigue viendo el
  saldo del año en curso y no se lanza.

**Completion criterion**
Los 11 tests pasan; el componente nuevo está registrado en `ComposicionDeLaPantalla.cs`; y
`ComponentesSinAccesoADatosTests` sigue verde con el archivo nuevo dentro de su escaneo.

---

## Block 5 — Índice que sostiene la consulta del saldo

**Files**
- `src/GestionVacaciones.Data/VacacionesDbContext.cs` (modificado) — declaración del índice, junto a
  `:116-118`.
- `src/GestionVacaciones.Data/Migrations/` (nuevo) — migración generada con
  `dotnet ef migrations add`.
- `tests/GestionVacaciones.Tests/Persistencia/EsquemaDeSolicitudTests.cs` (modificado) — `:150-171`,
  la lista de índices esperados.

**Data model**

Índice nuevo sobre `Solicitud`: `IX_Solicitud_EmpleadoId_Estado_FechaInicio`, sobre
`(EmpleadoId ASC, Estado ASC, FechaInicio ASC)`.

El único índice de hoy es `(EmpleadoId, FechaCreacion DESC)` (`:116-118`), creado para el listado de
FEAT-001a. **No sirve a la consulta del saldo**, que filtra por empleado, estado y rango de fechas:
sin este índice, cada cálculo de saldo escanea las solicitudes del empleado enteras, y NFR-03 no
tiene nada que lo sostenga.

**No se agrega ninguna check constraint.** La imputación por año es un dato **derivado** de
`FechaInicio`/`FechaFin` y no se persiste; persistirla crearía exactamente la divergencia que
`CK_Solicitud_DiasCoincidenConPeriodo` existe para impedir (`VacacionesDbContext.cs:142-146`), y para
un período a caballo no sería expresable como check de fila. Por eso
`EsquemaDeSolicitudTests.cs:174-199` —que afirma la lista exacta de 4 constraints— **no cambia**.

Las columnas del índice no cambian de tipo ni de nulabilidad: `EmpleadoId` (`int`, no nulo),
`Estado` (`int`, no nulo, acotado por `CK_Solicitud_EstadoValido`) y `FechaInicio` (`date`, no nulo),
tal como los declaró la migración inicial (`20260802014814_InicialV2.cs:47-49`). Este bloque **solo
agrega un índice**: no crea columnas, no cambia tipos y no toca claves.

**Error handling**
- **La migración falla al aplicarse** → no se da por buena: se revierte con
  `dotnet ef migrations remove` o `dotnet ef database update <MigracionAnterior>`, se corrige y se
  vuelve a aplicar. Lo exige `AGENTS.md` → *What NOT to do*, y es la razón de que el criterio de
  cierre de este bloque incluya la vuelta atrás y no solo la ida.
- **El índice ya existe con otro nombre o con otras columnas** → la migración falla al crearlo, y se
  trata igual que el caso anterior: revertir, corregir, reaplicar. No se intenta un `IF NOT EXISTS`:
  un índice distinto del declarado es una divergencia entre el esquema y el modelo, y taparla es lo
  que vuelve inútil el historial de migraciones.

> **Nota, no camino de error del bloque:** si la instancia SQL2022 no está disponible, los tests de
> `Persistencia/` se saltean con `SaltearSiNoEstaDisponible()`, que es la convención vigente de esa
> carpeta y no una conducta que este bloque introduzca. **No se sustituye por el proveedor en
> memoria**, que ignora índices y check constraints y dejaría el bloque verde sin haber verificado
> nada.

**Required tests**
- [ ] `El_indice_del_saldo_existe_con_sus_columnas_y_su_orden` — contra la instancia real, en la
  línea de los demás tests de `Persistencia/`.
- [ ] `Las_check_constraints_siguen_siendo_las_mismas_cuatro` — `:174-199` sin cambios, verde:
  confirma que este bloque no tocó las invariantes de fila.
- [ ] `La_migracion_revierte_dejando_el_esquema_anterior` — camino triste: aplicada y revertida, el
  índice no queda y las 4 constraints siguen.

**Completion criterion**
La migración aplica y revierte limpiamente (`dotnet ef database update` al anterior y de vuelta), los
tres tests pasan, y `EsquemaDeSolicitudTests.cs:174-199` sigue verde sin editarlo.

---

## Final verification

Cuando los cinco bloques estén hechos:

| AC | Verificado por |
|---|---|
| AC-01 | Block 1 — `Un_periodo_a_caballo_de_dos_anios_imputa_a_cada_uno_lo_suyo` (+3) · Block 2 — `Una_solicitud_a_caballo_descuenta_de_cada_anio_lo_suyo` |
| AC-02 | Block 3 — `Una_solicitud_que_supera_el_tope_se_rechaza_con_el_mensaje_de_AC_02` · Block 2 — `Los_literales_coinciden_con_prd_FEAT_001b` |
| AC-03 | Block 3 — `Una_solicitud_a_caballo_que_supera_en_un_anio_se_rechaza_con_el_desglose_de_AC_03` · Block 2 — `Los_literales_coinciden_con_prd_FEAT_001b` |
| AC-04 | Block 2 — `El_saldo_descuenta_los_dias_aprobados_y_los_pendientes` (+2) |
| AC-05 | Block 4 — `El_saldo_del_anio_en_curso_se_muestra_al_entrar`, `Se_muestran_utilizados_y_disponibles_por_separado` |
| AC-06 | Block 4 — `Un_periodo_que_cruza_el_anio_muestra_los_dos_saldos`, `Un_periodo_dentro_del_anio_muestra_un_solo_saldo` |
| AC-07 | Block 4 — `Si_el_calculo_falla_no_se_muestra_ninguna_cantidad` (+2) · Block 2 — `Sin_identidad_el_saldo_no_se_degrada_a_cero` |

Y además:

- **`dotnet test src/GestionVacaciones.slnx` en verde**, con los 198 tests de FEAT-001a más los 50 de
  este ticket, y **0 advertencias** (`TreatWarningsAsErrors` está activo).
- Cobertura ≥ 80 % en líneas, ramas y funciones sobre el código nuevo (NFR-02).
- `TopeAnual.Dias` es la única aparición de `14` en `src/`, y cambiarlo mueve todos los saldos sin
  tocar otra línea (NFR-01).
- `SaldoService` es el único consumidor de `ImputacionPorAnio` en `src/` (NFR-04).
- Los nueve guardarraíles estructurales que FEAT-001a dejó vigentes siguen verdes:
  `IDbContextFactory` y nunca `AddDbContext`, `TimeProvider` inyectado y ausente de todo `.razor`,
  sin `MarkupString`, la UI sin reglas propias, `PermisosService` como sede única, los estados
  distinguibles en pantalla, los literales atados al PRD y los diagnósticos sin PII.
- `AGENTS.md` → *Architecture conventions* enumera los servicios nuevos.
- SAST PASSED sobre el delta.
