# SAST FEAT-002 — Análisis estático de seguridad

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-002 |
| Tier | FEATURE |
| Rama | `feat/FEAT-002-aprobacion-manager` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-002.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §3/§4 |
| Fecha | 2026-08-07 |
| Ejecuciones | 2 · cierre de CODE + recierre tras bucle correctivo de VERIFY |
| Resultado | **PASSED** |

## Alcance

**Ejecución 1** — el delta completo del ticket, diff `6d681b0..HEAD` (5 commits): esquema
(`Solicitud.cs`, `VacacionesDbContext.cs`, migración `ColumnasDeResolucion`), `PermisosService.cs`,
`SolicitudesService.cs`, `ErroresDeSolicitud.cs`, `Autorizaciones.razor`, `MainLayout.razor`,
`ListadoDeSolicitudes.razor`, más los archivos de test del ticket. Ningún `.csproj` fue tocado: 0
dependencias nuevas.

**Ejecución 2** (recierre) — el delta agregado por el bucle correctivo VERIFY→CODE
(F-VER-03/NFR-01 + 2 WARN): solo `AutorizacionesTests.cs` y `DiagnosticoSinPiiTests.cs`, 4 tests
nuevos que cierran huecos de cobertura sobre código ya escaneado en la ejecución 1 (el `catch` de
`MainLayout.razor`, el `catch` de `Autorizaciones.ResolverAsync` y los `ToString()` de
`ResultadoDeLaResolucion`/`SolicitudPendienteDelEquipo`). Ningún archivo de producción ni `.csproj`
cambió. `git diff --stat -- '*.csproj'`: vacío. Secretos, SQL crudo y `MarkupString`:
`grep -iE "password|secret|apikey|connectionstring|token"` / `grep "FromSqlRaw|ExecuteSqlRaw"` /
`grep MarkupString` sobre el delta: 0 apariciones en los tres. `dotnet list ... --vulnerable
--include-transitive`: 0 paquetes vulnerables en los 3 proyectos.

---

## Resultado por categoría

| ID | Categoría | Severidad | Estado |
|---|---|---|---|
| F-SAST-01 | Secretos embebidos | Critical | ✅ Limpio |
| F-SAST-02 | Inyección SQL | Critical | ✅ Limpio |
| F-SAST-03 | Inyección de comandos | Critical | ✅ Sin superficie |
| F-SAST-04 | Deserialización insegura | Critical | ✅ Sin superficie |
| F-SAST-05 | Path traversal | High | ✅ Sin superficie |
| F-SAST-06 | XSS | High | ✅ Limpio — `MotivoDeRechazo` interpolado con `@`, nunca `MarkupString` |
| F-SAST-07 | SSRF | High | ✅ Sin superficie |
| F-SAST-08 | Criptografía rota | High | ✅ Sin superficie |
| F-SAST-09 | Modo debug en producción | High | ✅ Sin cambios — ningún `appsettings*` tocado |
| F-SAST-10 | Log de datos sensibles | High | ✅ Limpio |
| F-SAST-11 | Upload sin restricción | High | ✅ Sin superficie |
| F-SAST-12 | Falta de protección CSRF | High | ✅ Sin cambios — ningún endpoint ni middleware tocado |
| F-SAST-13/16 | CVEs en dependencias | Critical/High/Medium | ✅ Limpio — 0 dependencias nuevas, `dotnet list package --vulnerable` sin hallazgos |
| F-SAST-14 | Validación de entrada incompleta | Medium | 🟢 1 hallazgo LOW no bloqueante (ver abajo) |
| F-SAST-15 | Manejo de errores que filtra internals | Medium | ✅ Limpio |
| F-SAST-17 | Funciones inseguras | Medium | ✅ Sin superficie |

**Total: 15 categorías limpias · 1 hallazgo LOW no bloqueante · 0 vulnerabilidades Critical/High/Medium abiertas · 0 supresiones.**

---

## Verificaciones puntuales

| Control | Evidencia |
|---|---|
| Secretos (F-SAST-01) | `grep -i "password\|secret\|apikey\|connectionstring"` sobre los 11 archivos de producción del alcance: 0 apariciones. |
| SQL crudo (F-SAST-02) | `grep "FromSqlRaw\|ExecuteSqlRaw\|FromSqlInterpolated"`: 0 apariciones. Todas las consultas nuevas (`EmpleadosBajoAutoridadDeAsync`, `ListarPendientesDelEquipoAsync`, `IntentarResolverAsync`) son LINQ traducido por EF Core. |
| XSS / `MarkupString` (F-SAST-06) | `grep "MarkupString"` sobre los 3 `.razor` tocados: 0 apariciones. `ListadoDeSolicitudes.razor:51` renderiza `@solicitud.MotivoDeRechazo` con interpolación normal de Blazor (auto-escapada), cumpliendo la prohibición absoluta de `AGENTS.md` — mitigación de R-24 verificada en código. |
| Elevación de privilegio / IDOR (R-22, HIGH del modelo de amenazas) | `PermisosService.PuedeResolverLasSolicitudesDeAsync` (línea 239) excluye `self` **antes** de tocar el organigrama (`if (sujeto == empleadoDeLasSolicitudes) return false;`, sin `await` previo). `SolicitudesService.IntentarResolverAsync` llama `ExigirPoderResolverLasSolicitudesDeAsync` **después** de leer la fila (necesita `EmpleadoId`) pero **antes** de cualquier mutación de estado; una denegación lanza `AccesoASolicitudesDenegadoException` que se propaga tal cual (403), nunca se degrada a un rechazo de negocio (AC-08). Mitigación verificada en código, no solo declarada en el modelo de amenazas. |
| Proyección mínima / disclosure (I de C15/C17) | `EmpleadosBajoAutoridadDeAsync` proyecta solo `Id` (`.Select(empleado => empleado.Id)`); `ListarPendientesDelEquipoAsync` hace `Join` explícito y proyecta solo `Nombre`, nunca `Correo`. `grep ".Include("` sobre `PermisosService.cs`/`SolicitudesService.cs`: 0 apariciones reales (el único match es una mención en un comentario XML-doc). |
| Logs sin PII (F-SAST-10, R-26) | `grep "LogInformation\|LogError"` en el alcance: ningún log nuevo interpola `MotivoDeRechazo`. Los tres `catch` de UI (`MainLayout.razor:60`, `Autorizaciones.razor:227`) loguean la excepción vía `Registro.LogError` sin el motivo ni datos de la solicitud, y muestran en pantalla solo un mensaje genérico (F-SAST-09/15). |
| Manejo de errores (F-SAST-15) | 5 `catch` en el alcance, todos no silenciosos: 2 acotados a `EsConflictoDeSerializacion` (código SQL Server específico, reintento dirigido sin bucle — R-25), 3 en la capa Web que loguean vía `Registro.LogError` y degradan a un estado UI explícito (nunca a lista vacía, ver `SinEmpleadoSeleccionadoException` manejada aparte del `catch (Exception)` genérico). |
| Constraint de coherencia (defensa en profundidad) | `CK_Solicitud_ResolucionCoherente` en la migración impide persistir `Pendiente` con campos de resolución no nulos, o `Aprobada`/`Rechazada` con campos faltantes — imposible de esquivar desde C#. |
| Dependencias (F-SAST-13/16) | `git diff --stat` sobre `*.csproj`: vacío. `dotnet list src/GestionVacaciones.slnx package --vulnerable --include-transitive`: 0 paquetes vulnerables en los 3 proyectos. |
| Validación en servidor (F-SAST-14) | AC-03 (motivo obligatorio en rechazo) se valida en `ResolverAsync` **antes** de abrir contexto, no solo en el cliente. El campo del formulario en `Autorizaciones.razor` es comodidad, no la regla (R-10). |

---

## Hallazgo no bloqueante

🟢 **LOW — sin límite de longitud explícito para `MotivoDeRechazo` antes de tocar la base**
`src/GestionVacaciones.Web/Components/Pages/Autorizaciones.razor:76-82`,
`src/GestionVacaciones.Data/Services/SolicitudesService.cs:821`

El `MudTextField` del motivo no lleva `MaxLength`, y `ResolverAsync`/`IntentarResolverAsync` no
validan la longitud del texto antes de `SaveChangesAsync`. El único límite es la columna
`nvarchar(1000)` (`HasMaxLength` en `VacacionesDbContext.cs:130`): un motivo de más de 1000
caracteres no se trunca silenciosamente ni se inyecta — SQL Server rechaza el `INSERT`/`UPDATE`, la
excepción se propaga sin `catch` específico, cae en el `catch (Exception)` genérico de
`Autorizaciones.razor:227` que loguea y muestra un mensaje genérico. **No es una vulnerabilidad**
(no hay bypass de autorización, no hay fuga de datos, no hay truncamiento silencioso que corrompa
el motivo), es una validación de negocio ausente: un manager que escribe un motivo muy largo ve un
error genérico de "algo salió mal" en vez de un mensaje claro sobre el límite. No bloquea el gate.
**Sugerencia:** agregar `MaxLength="1000"` al `MudTextField` y una validación explícita en
`ResolverAsync` con un literal propio en `ErroresDeSolicitud` — mejora de UX/robustez, no de
seguridad, para un ticket futuro o un ajuste menor si se quiere cerrar antes de RELEASE.

---

## Triage — sec-auditor

```
┌─────────────────────────────────────────────────────────┐
│  sec-auditor — Security Triage (FEAT-002)                │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  True Positives:                                         │
│    (ninguno con severidad bloqueante)                    │
│                                                          │
│  False Positives:                                        │
│    (no se generaron falsos positivos en este barrido —   │
│     todas las categorías del protocolo dieron limpias o   │
│     "sin superficie" con evidencia directa)               │
│                                                          │
│  Additional findings:                                    │
│    ⚠️ LOW: MotivoDeRechazo sin límite de longitud del     │
│       lado del cliente/servicio (solo constraint de DB)   │
│       — Autorizaciones.razor:76-82,                        │
│         SolicitudesService.cs:821. No bloquea el gate.     │
│                                                          │
│  ─────────────────────────────────────────────────────   │
│  Verdict: SECURE                                          │
│  True Positives: 0 | False Positives: 0                  │
│  Highest severity: LOW (no bloqueante)                   │
└─────────────────────────────────────────────────────────┘
```

**Riesgos del modelo de amenazas confirmados mitigados en código (no solo declarados):**
- R-22 (HIGH) — self excluido explícitamente en `PermisosService.PuedeResolverLasSolicitudesDeAsync:247`, antes de cualquier consulta al organigrama; autorización ocurre antes de mutar estado en `IntentarResolverAsync:807`.
- R-24 (MEDIUM) — sin `MarkupString`, motivo renderizado con interpolación auto-escapada en `ListadoDeSolicitudes.razor:51`.
- R-25 (LOW) — reintento dirigido único (sin bucle) ante conflicto de serialización en `SolicitudesService.cs:743-778`.
- R-26 (MEDIUM) — aceptado sin mitigación técnica adicional por diseño (documentado en el propio modelo de amenazas); no hay camino de exposición nuevo que no cubra ya el control de acceso de R-05.

**Archivos revisados:**
- `src/GestionVacaciones.Data/Entidades/Solicitud.cs`
- `src/GestionVacaciones.Data/VacacionesDbContext.cs`
- `src/GestionVacaciones.Data/Migrations/20260807030557_ColumnasDeResolucion.cs`
- `src/GestionVacaciones.Data/Services/PermisosService.cs`
- `src/GestionVacaciones.Data/Services/SolicitudesService.cs`
- `src/GestionVacaciones.Data/Services/ErroresDeSolicitud.cs`
- `src/GestionVacaciones.Web/Components/Pages/Autorizaciones.razor`
- `src/GestionVacaciones.Web/Components/Layout/MainLayout.razor`
- `src/GestionVacaciones.Web/Components/Solicitudes/ListadoDeSolicitudes.razor`
- `docs/daw/security/threat-FEAT-002.md` (referencia)

**Verdicto final ejecución 1: SECURE / PASSED — gate SAST desbloqueado, 0 Critical/High/Medium, 1 LOW no bloqueante.**

---

## Ejecución 2 — recierre tras bucle correctivo (2026-08-07)

El bucle correctivo VERIFY→CODE (F-VER-03/NFR-01: `MainLayout.razor` bajo el 80% exigido por
NFR-01, más 2 WARN de cobertura) se resolvió agregando 4 tests — sin tocar ningún archivo de
producción. Las 15 categorías del protocolo (F-SAST-01 a F-SAST-17) se re-evaluaron sobre el nuevo
delta (ver "Alcance" arriba): todas limpias o sin superficie, igual que en la ejecución 1. El único
hallazgo LOW (motivo de rechazo sin límite de longitud explícito, `Autorizaciones.razor:76-82` /
`SolicitudesService.cs:821`) sigue abierto sin cambios — no forma parte de este bucle correctivo y
sigue sin bloquear el gate.

**Verdicto final ejecución 2: SECURE / PASSED — gate SAST desbloqueado, 0 Critical/High/Medium, 1 LOW no bloqueante (sin cambios respecto a la ejecución 1).**
