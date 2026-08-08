# Verificación FEAT-001b

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001b |
| Tier | FEATURE |
| PRD | `docs/daw/prd/prd-FEAT-001b.md` |
| Spec | `docs/daw/specs/spec-FEAT-001b.md` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-001b.md` |
| SAST | `docs/daw/security/sast-FEAT-001b.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §5 |
| Rondas | 1 (PASSED en la primera) |
| Fecha | 2026-08-06 |
| HEAD verificado | `c809710` |

---

## Ronda 1 — 2026-08-06 · HEAD `c809710` · **PASSED**

### Comandos ejecutados por el verificador, no delegados

| Comando | Resultado |
|---|---|
| `dotnet build src/GestionVacaciones.slnx` | 0 advertencias, 0 errores |
| `dotnet test src/GestionVacaciones.slnx --collect:"XPlat Code Coverage"` | 269 passed, 0 failed, 0 skipped, contra la instancia SQL Server 2022 real |
| `dotnet format --severity info` | solo sugerencias de estilo (CA1822, CA1861, primary constructors) en 3 archivos de test — informativas, no forman parte del linter configurado |
| Cobertura del código nuevo/modificado (desglosada por archivo) | ver F-VER-03 |

### Resultado por regla

| Regla | Resultado | Detalle |
|---|---|---|
| F-VER-01 | ✅ PASS | 7/7 AC con test que valida comportamiento real y pasa |
| F-VER-02 | ✅ PASS | 5/5 bloques implementados y completos |
| F-VER-03 | ✅ PASS | 95,5% líneas · 92,8% ramas globales; dominio nuevo (`ImputacionPorAnio`, `TopeAnual`, `SaldoService`, deltas de `ErroresDeSolicitud`/`SolicitudesService`) en 100% línea/rama/función |
| F-VER-04 | ✅ PASS | Caminos tristes cubiertos en las 4 unidades de entrada nuevas; 2 casos borde de bajo riesgo señalados como WARN (ver abajo) |
| F-VER-05 | ✅ PASS | 0 advertencias, 0 errores con `TreatWarningsAsErrors` |
| F-VER-06 | ✅ PASS | 50/50 tests exigidos por la spec (7+16+13+11+3) — faltan 0 |
| W-VER-01 | ✅ Sin hallazgos | Sin código muerto ni imports sin usar |
| W-VER-02 | ⚠️ 1 hallazgo | `SaldoDelEmpleado.razor`: 86,2% línea — dentro de la franja 80-90%, podría subir |
| W-VER-03 | ✅ Sin hallazgos | Sin tests frágiles: `TiempoFijo` y empleados descartables en vez de estado global o IDs hardcodeados |

### F-VER-01 — Trazabilidad de los 7 criterios de aceptación

| AC | Implementación | Test |
|---|---|---|
| AC-01 | `ImputacionPorAnio.cs:DiasEnElAnio/AniosAbarcados` | `ImputacionPorAnioTests.Un_periodo_a_caballo_de_dos_anios_imputa_a_cada_uno_lo_suyo` (+3) · `SaldoDelAnioTests.Una_solicitud_a_caballo_descuenta_de_cada_anio_lo_suyo` |
| AC-02 | `SolicitudesService.cs:CrearAsync` (líneas 361-393) | `TopeAnualEnElAltaTests.Una_solicitud_que_supera_el_tope_se_rechaza_con_el_mensaje_de_AC_02` · `SaldoDelAnioTests.Los_literales_coinciden_con_prd_FEAT_001b` |
| AC-03 | `SolicitudesService.cs:CrearAsync` (desglose de dos años) | `TopeAnualEnElAltaTests.Una_solicitud_a_caballo_que_supera_en_un_anio_se_rechaza_con_el_desglose_de_AC_03` |
| AC-04 | `SaldoService.cs:DeLosAniosAsync` + `record SaldoDelAnio` | `SaldoDelAnioTests.El_saldo_descuenta_los_dias_aprobados_y_los_pendientes` (+2: ignora rechazadas, tope completo) |
| AC-05 | `SaldoDelEmpleado.razor:CargarSaldoDelAnioEnCursoAsync` | `SaldoEnPantallaConBaseDeDatosTests.El_saldo_del_anio_en_curso_se_muestra_al_entrar`, `Se_muestran_utilizados_y_disponibles_por_separado` |
| AC-06 | `SaldoDelEmpleado.razor:ActualizarSaldoDelOtroAnioAsync` | `Un_periodo_que_cruza_el_anio_muestra_los_dos_saldos`, `Un_periodo_dentro_del_anio_muestra_un_solo_saldo` |
| AC-07 | `SaldoDelEmpleado.razor` (cuarto estado, `EstadoFallo`, sin dígitos) | `Si_el_calculo_falla_no_se_muestra_ninguna_cantidad` (+2: listado/alta usables, distinción de 4 estados) |

Los tests se evaluaron por sus asertos (valores concretos: 4/2026, 5/2027, 3 utilizados/11 disponibles),
no por sus nombres. Ninguno resultó tautológico.

### F-VER-02 / F-VER-06 — Los 5 bloques y sus 50 tests exigidos

| Bloque | Tests exigidos | Estado |
|---|---|---|
| 1 — Imputación por año calendario | 7 | ✅ 9/7 (2 extra) |
| 2 — Saldo del año y tope anual | 16 | ✅ 16/16 por contenido (2 renombrados, 1 sustituido con justificación — ver WARN) |
| 3 — El alta hace cumplir el tope | 13 | ✅ 13/13, nombres exactos |
| 4 — El saldo en pantalla | 11 | ✅ 11/11, nombres exactos |
| 5 — Índice que sostiene la consulta del saldo | 3 | ✅ 3/3, nombres exactos |

### F-VER-03 — Cobertura

- Global: **95,5% líneas · 92,8% ramas**, 269/269 tests, 0 skipped.
- Dominio nuevo (100% línea/rama/función): `ImputacionPorAnio.cs`, `TopeAnual.cs`, `SaldoService.cs`,
  el delta de `ErroresDeSolicitud.cs`, el delta de `SolicitudesService.cs`.
- `FormularioDeAlta.razor`/`MisSolicitudes.razor`: el código que este ticket agregó o modificó está
  100% cubierto; los huecos globales de esos archivos (78,5%/88,2%) son preexistentes de FEAT-001a
  (`EnviarAsync`, `catch(SinEmpleadoSeleccionadoException)`), confirmado por diff — ninguna línea
  tocada por FEAT-001b quedó sin cubrir.
- `SaldoDelEmpleado.razor`: 86,2% línea — por encima del piso del 80%, dentro de la franja 80-90% de
  W-VER-02 (ver hallazgo W-3 abajo).

### F-VER-04 — Caminos tristes

Cubiertos con test dedicado: período invertido, año fuera de `[1,9999]`, año ajeno al período,
sin identidad, fallo de persistencia, más de 2 años, años no consecutivos, tope superado (1 y 2 años),
más de 2 años sin tocar la base, fallo de guardado con rollback, excepción de motor no convertida en
rechazo, cálculo de saldo fallido, sin empleado seleccionado, período invertido no pide el segundo año.

Dos casos borde de bajo riesgo señalados como WARN (no FAIL — no son unidades de entrada nuevas sin
cubrir, son ramas secundarias de unidades ya cubiertas):

- **W-1.** `Permisos_negados_no_se_degradan_a_cero` (nombre literal de la spec) no existe. En su lugar,
  `SaldoService_le_pregunta_a_PermisosService_antes_de_consultar` fija por orden de código que la
  comprobación de permisos ocurre antes de abrir contexto. Verificado por el verificador: no hay
  `catch` entre esa llamada y el `return`, así que una eventual denegación se propagaría igual que
  `Sin_identidad_el_saldo_no_se_degrada_a_cero`. Sustitución razonada (`PermisosService` no puede negar
  hoy en este camino, es `sealed` sin interfaz), pero es una desviación del literal de la spec sin nota
  formal en ella.
- **W-3.** `SaldoDelEmpleado.razor:185-194` (catch de `ActualizarSaldoDelOtroAnioAsync`, el segundo
  año) no se ejecuta en ningún test: los tests de fallo fuerzan el error en el primer año, que corta
  antes de llegar al segundo `try/catch`. Misma lógica ya probada para el primer año, aplicada al
  segundo — riesgo bajo.

### Calidad

- Build: 0 advertencias, 0 errores (`TreatWarningsAsErrors` activo).
- Imports limpios, sin código muerto (W-VER-01: sin hallazgos).
- Sin tests frágiles (W-VER-03: sin hallazgos).

### Verificaciones estructurales (exigidas por "Final verification" de la spec, no son AC)

| Verificación | Resultado |
|---|---|
| NFR-01 — `14` aparece una sola vez en `src/` | ✅ Único hit real: `TopeAnual.cs`. El resto son menciones en comentarios XML |
| NFR-04 — único consumidor de la imputación | ✅ `SaldoService` es el único consumidor de `DiasEnElAnio` (la función que calcula). `AniosAbarcados` (enumeración de calendario sin regla de negocio) sí se usa también desde `SolicitudesService` y `SaldoDelEmpleado.razor`, por diseño explícito de los Bloques 3 y 4 — ver W-4 |
| Los 9 guardarraíles de FEAT-001a | ✅ Sin `AddDbContext`, sin `TimeProvider`/`DateTime.*` en ningún `.razor`, sin `MarkupString` (greps propios, 0 resultados) |
| `AGENTS.md` enumera los servicios nuevos | ✅ `SaldoService`, `ImputacionPorAnio`, `TopeAnual` listados |
| SAST | ✅ PASSED — 16 categorías limpias, 0 vulnerabilidades, 0 supresiones |

**W-4 — Imprecisión de redacción en la spec, no del código.** "Final verification" dice "`SaldoService`
es el único consumidor de `ImputacionPorAnio` en `src/`", pero los Bloques 3 y 4 llaman a
`ImputacionPorAnio.AniosAbarcados` por diseño explícito de esos mismos bloques. El test
`Solo_SaldoService_consume_DiasEnElAnio` documenta con precisión que la restricción real es sobre
`DiasEnElAnio` (la función que efectivamente imputa), no sobre el tipo entero. NFR-04 se cumple bajo
esta lectura más precisa; la ambigüedad es de la tabla resumen de la spec.

### W-2 — Block 2: test de PII renombrado/dividido

`El_mensaje_compuesto_no_lleva_identificadores_de_persona` (nombre de la spec) se implementó como dos
tests (`El_mensaje_compuesto_no_lleva_nada_que_el_formato_no_diga` y
`Los_mensajes_compuestos_pasan_por_el_criterio_del_guardarrail_de_diagnosticos`), que verifican por
dígitos-centinela que el mensaje no lleva nada fuera del saldo y los años. Cubre la intención con más
rigor que el test tal como estaba descrito literalmente (que habría sido trivialmente vacío, porque el
compositor no puede recibir nombre ni legajo por firma).

---

## Veredicto

```
┌─────────────────────────────────────────────────────────┐
│  /daw-verify-module FEAT-001b — PASSED                   │
├─────────────────────────────────────────────────────────┤
│  Total: 22 passed, 0 failed, 4 warnings                  │
│  Result: PASSED                                          │
│  Next: gate desbloqueado — CODE puede cerrar (avanzar a  │
│         RELEASE)                                          │
└─────────────────────────────────────────────────────────┘
```

Los 4 WARN son mejoras de calidad sobre casos borde de bajo riesgo y una imprecisión de redacción en
la propia spec — ninguno representa un AC sin verificar, una tarea de bloque sin implementar, ni una
brecha de cobertura por debajo del mínimo.
