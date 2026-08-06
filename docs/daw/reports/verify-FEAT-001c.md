# Verificación FEAT-001c

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001c |
| Tier | FEATURE |
| PRD | `docs/daw/prd/prd-FEAT-001c.md` |
| Spec | `docs/daw/specs/spec-FEAT-001c.md` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-001c.md` |
| SAST | `docs/daw/security/sast-FEAT-001c.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §5 |
| Rondas | 1 (PASSED en la primera) |
| Fecha | 2026-08-06 |
| HEAD verificado | `2a6c7d1` |

---

## Ronda 1 — 2026-08-06 · HEAD `2a6c7d1` · **PASSED**

### Comandos ejecutados por el verificador, no delegados

| Comando | Resultado |
|---|---|
| `dotnet build src/GestionVacaciones.slnx` | 0 advertencias, 0 errores |
| `dotnet test src/GestionVacaciones.slnx --collect:"XPlat Code Coverage"` | 284 passed, 0 failed, 0 skipped, contra la instancia SQL Server 2022 real |
| `Dos_altas_concurrentes_de_periodos_solapados_persisten_exactamente_una` | corrida 6 veces (1 en la suite completa + 5 aisladas), 0 fallos |

### Resultado por regla

| Regla | Resultado | Detalle |
|---|---|---|
| F-VER-01 | ✅ PASS | 4/4 AC con test que valida comportamiento real y pasa |
| F-VER-02 | ✅ PASS | Único bloque completo |
| F-VER-03 | ✅ PASS | Código nuevo/modificado: `EstadosDeSolicitud.cs` 100%/100%, `SaldoService.cs` 100%/100%, `ErroresDeSolicitud.cs` 100%/100%, `SolicitudesService.cs` 97,97% líneas / 100% ramas (las 2 líneas sin cubrir son llaves de cierre de `try/catch`, no lógica) |
| F-VER-04 | ✅ PASS | Los 3 caminos tristes propios del ticket cubiertos: superposición detectada, conflicto de serialización que sí es superposición, conflicto que no lo es |
| F-VER-05 | ✅ PASS | 0 advertencias, 0 errores con `TreatWarningsAsErrors` |
| F-VER-06 | ✅ PASS | 13/13 tests exigidos por la spec (15 casos de ejecución con las 2 Theory) |
| W-VER-01 | ✅ Sin hallazgos | Sin código muerto ni imports sin usar |
| W-VER-02 | ✅ No aplica | Todo el código nuevo por encima del 90% recomendado |
| W-VER-03 | ⚠️ 1 hallazgo | `SqlExceptionDePrueba` construye la excepción por reflexión sobre miembros internos de `Microsoft.Data.SqlClient` 6.1.1 — frágil ante un upgrade del paquete, pero autodocumentado (cada punto de reflexión falla con un mensaje que nombra qué firma cambió) |

### F-VER-01 — Trazabilidad de los 4 criterios de aceptación

| AC | Implementación | Test |
|---|---|---|
| AC-01 | `SolicitudesService.cs:HaySuperposicionAsync` + `CrearAsync` (líneas 393-399) | `Un_periodo_totalmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01` + `Un_periodo_parcialmente_superpuesto_se_rechaza_con_el_mensaje_de_AC_01` (Theory, 2 casos) |
| AC-02 | Misma consulta, límites `<=`/`>=` (líneas 519-522) | `Un_periodo_contiguo_el_dia_siguiente_se_acepta` + `Un_periodo_contiguo_el_dia_anterior_se_acepta` (espejo) |
| AC-03 | `EstadosDeSolicitud.Vigentes` excluye `Rechazada` | `Una_solicitud_rechazada_no_bloquea_las_mismas_fechas` |
| AC-04 | Transacción `Serializable` + reintento dirigido (líneas 376-486) | `Dos_altas_concurrentes_de_periodos_solapados_persisten_exactamente_una` (real, 6 corridas, 0 fallos) + `El_conflicto_de_serializacion_de_una_superposicion_real_se_convierte_en_rechazo` + `El_conflicto_de_serializacion_que_no_es_superposicion_se_propaga` |

Los tests se evaluaron por sus asertos (literal exacto, valores concretos, ambos sentidos de cada
borde), no por sus nombres. Ninguno resultó tautológico. AC-04, el criterio más exigente del ticket
("persistir exactamente 1 de las dos y rechazar la otra con el mensaje de superposición" bajo
concurrencia real), quedó verificado sin ningún `try/catch` que amortiguara el resultado en el propio
test — a propósito, para que una excepción sin capturar hiciera fallar el test.

### F-VER-02 / F-VER-06 — El único bloque y sus 13 tests exigidos

Los 13 tests de "Required tests" de la spec existen con los nombres exactos y pasan. Con las dos
`Theory` (solapamiento parcial en dos sentidos, pendiente/aprobada) se ejecutan 15 casos.

### Calidad

- Build: 0 advertencias, 0 errores (`TreatWarningsAsErrors` activo).
- Imports limpios, sin código muerto (W-VER-01: sin hallazgos).
- El único WARN (W-VER-03) es de mantenibilidad, no de corrección: la construcción de un
  `SqlException` real con `Number == 1205` no tiene otra vía practicable (el tipo no tiene
  constructor público), y la reflexión usada falla con un mensaje explícito si una versión futura de
  `Microsoft.Data.SqlClient` cambia la firma interna que usa — no falla en silencio.

### Verificaciones estructurales (exigidas por "Final verification" de la spec, no son AC)

| Verificación | Resultado |
|---|---|
| `EstadosDeSolicitud.Vigentes` como fuente única de `{Pendiente, Aprobada}` | ✅ Ni `SaldoService` ni `SolicitudesService` repiten el par como literal propio (grep confirmado) |
| Sin migración ni índice nuevos | ✅ `Migrations/` conserva las mismas 2 migraciones desde el cierre de FEAT-001b |
| Los guardarraíles estructurales de FEAT-001a/b | ✅ `IDbContextFactory` siempre, sin `AddDbContext`, sin `MarkupString`, sin `catch` silencioso (el único `catch` nuevo está acotado por `when` al error 1205 y siempre rechaza o repropaga), `PermisosService` como sede única sin cambios |
| `AGENTS.md` enumera `EstadosDeSolicitud` | ✅ |
| SAST | ✅ PASSED — 16 categorías limpias, 0 vulnerabilidades, 0 supresiones |

---

## Veredicto

```
┌─────────────────────────────────────────────────────────┐
│  /daw-verify-module FEAT-001c — PASSED                   │
├─────────────────────────────────────────────────────────┤
│  Total: 21 passed, 0 failed, 1 warning                   │
│  Result: PASSED                                          │
│  Next: gate desbloqueado — CODE puede cerrar (avanzar a  │
│         RELEASE)                                          │
└─────────────────────────────────────────────────────────┘
```

El único WARN es una nota de mantenibilidad sobre un helper de test que ya se autodocumenta ante su
propio punto de fragilidad — no representa un AC sin verificar, una tarea sin implementar, ni una
brecha de cobertura por debajo del mínimo.
