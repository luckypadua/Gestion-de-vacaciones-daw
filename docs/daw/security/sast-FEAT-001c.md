# SAST FEAT-001c — Análisis estático de seguridad

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001c |
| Tier | FEATURE |
| Rama | `feat/FEAT-001c-no-superposicion-periodos` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-001c.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §4 |
| Fecha | 2026-08-06 |
| Ejecuciones | 1 · cierre de CODE |
| Resultado | **PASSED** |

## Alcance

El delta completo del ticket (único bloque), diff `adbb0b7..HEAD`: `EstadosDeSolicitud.cs` (nuevo),
`SolicitudesService.cs`, `SaldoService.cs`, `ErroresDeSolicitud.cs` y `VacacionesDbContext.cs`
(comentario) en `src/`, más los dos archivos de test nuevos. Ningún `.csproj` fue tocado: no hay
dependencias nuevas.

---

## Resultado por categoría

| ID | Categoría | Severidad | Estado |
|---|---|---|---|
| F-SAST-01 | Secretos embebidos | Critical | ✅ Limpio |
| F-SAST-02 | Inyección SQL | Critical | ✅ Limpio |
| F-SAST-03 | Inyección de comandos | Critical | ✅ Sin superficie |
| F-SAST-04 | Deserialización insegura | Critical | ✅ Sin superficie |
| F-SAST-05 | Path traversal | High | ✅ Sin superficie |
| F-SAST-06 | XSS | High | ✅ Sin superficie — sin cambios en el proyecto Web |
| F-SAST-07 | SSRF | High | ✅ Sin superficie |
| F-SAST-08 | Criptografía rota | High | ✅ Sin superficie |
| F-SAST-09 | Modo debug en producción | High | ✅ Sin cambios — ningún `appsettings*`/config tocado |
| F-SAST-10 | Log de datos sensibles | High | ✅ Limpio |
| F-SAST-11 | Upload sin restricción | High | ✅ Sin superficie |
| F-SAST-12 | Falta de protección CSRF | High | ✅ Sin cambios — ningún endpoint ni middleware tocado |
| F-SAST-13/16 | CVEs en dependencias | Critical/High/Medium | ✅ Limpio |
| F-SAST-14 | Validación de entrada incompleta | Medium | ✅ Limpio |
| F-SAST-15 | Manejo de errores que filtra internals | Medium | ✅ Limpio |
| F-SAST-17 | Funciones inseguras | Medium | ✅ Sin superficie |

**Total: 16 categorías limpias · 0 vulnerabilidades abiertas · 0 supresiones.**

---

## Verificaciones puntuales

| Control | Evidencia |
|---|---|
| Secretos (F-SAST-01) | `grep` sobre el diff completo por patrones de credencial/cadena de conexión: 0 apariciones. |
| SQL crudo (F-SAST-02) | 0 apariciones de `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw`. La consulta de solapamiento nueva (`HaySuperposicionAsync`) es LINQ parametrizado por EF Core, igual que el resto del dominio. |
| Manejo de errores (F-SAST-15) | El único `catch` nuevo (`SolicitudesService.cs:271`, `catch (Exception excepcion) when (EsConflictoDeSerializacion(excepcion))`) está acotado a un código de error específico (SQL Server 1205), nunca `catch (Exception)` desnudo. Termina siempre en un rechazo explícito (si confirma la superposición) o en `throw;` que repropaga la excepción original intacta — ningún camino la traga ni expone una traza distinta a la que ya maneja el pipeline de errores existente. Confirmado por `daw-arch-auditor` durante la revisión del bloque. |
| Logs sin PII (F-SAST-10, R-20) | La llamada nueva a `_registro.LogInformation` (`SolicitudesService.cs:536-538`) usa plantilla estructurada con solo `EmpleadoId` y un indicador de camino ("directo"/"reintento") — nunca fechas, nunca el identificador de la otra solicitud. Verificado por `El_rechazo_por_superposicion_queda_registrado`, que afirma que el único dígito del mensaje es el `EmpleadoId`. |
| Validación en servidor (F-SAST-14) | La regla de superposición corre enteramente en `SolicitudesService.CrearAsync`, sin ningún atajo del lado del cliente — `FormularioDeAlta.razor` no fue tocado y su botón de enviar sigue condicionado solo a "están las dos fechas" (R-10, sin cambios). |
| Dependencias (F-SAST-13/16) | `dotnet list src/GestionVacaciones.slnx package --vulnerable --include-transitive`: 0 paquetes vulnerables en los tres proyectos. Ningún `.csproj` modificado — 0 dependencias nuevas. |
| Reintento sin amplificación (relacionado a F-SAST-14, ver R-21 del modelo de amenazas) | El mecanismo de reintento ante conflicto de serialización es estructuralmente incapaz de repetirse más de una vez (sin `while`, sin recursión — confirmado por `daw-arch-auditor`) y queda acotado a `EmpleadoId == autor`, sin poder amplificar carga sobre otro empleado. |

---

## Hallazgos que no bloquean

Ninguno. La auditoría de arquitectura del bloque encontró y corrigió, antes de este SAST, un `cref`
roto en un XML-doc (cosmético, sin impacto de seguridad) y una entrada faltante en la enumeración de
`AGENTS.md` — ambos ya resueltos en el commit `2ab8ae1`.

---

## Veredicto

```
┌─────────────────────────────────────────────────────────────┐
│  /daw-security-sast FEAT-001c — PASSED                       │
├─────────────────────────────────────────────────────────────┤
│  Total: 16 categorías limpias, 0 vulnerabilidades abiertas   │
│         (0 críticas, 0 altas, 0 medias)                      │
│  Supresiones: 0                                               │
│  No bloqueantes: 0                                            │
│  Next: gate desbloqueado — CODE puede cerrar                  │
└─────────────────────────────────────────────────────────────┘
```
