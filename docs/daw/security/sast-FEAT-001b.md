# SAST FEAT-001b — Análisis estático de seguridad

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001b |
| Tier | FEATURE |
| Rama | `feat/FEAT-001b-imputacion-tope-saldo` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-001b.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §4 |
| Fecha | 2026-08-06 |
| Ejecuciones | 1 · cierre de CODE |
| Resultado | **PASSED** |

## Alcance

El delta completo del ticket, los 5 bloques de la spec, diff `8209de0..HEAD` (31 archivos, 3424
inserciones): `ImputacionPorAnio.cs`, `TopeAnual.cs`, `SaldoService.cs`, los cambios en
`SolicitudesService.cs` y `ErroresDeSolicitud.cs`, la migración `20260806034345_IndiceDelSaldo`, el
componente `SaldoDelEmpleado.razor` y los cambios en `FormularioDeAlta.razor`/`MisSolicitudes.razor`,
más los archivos de test nuevos y modificados. Incluye auditoría de dependencias sobre los tres
proyectos, con transitivas. Ningún `.csproj` fue tocado: no hay dependencias nuevas ni superficie
nueva de cadena de suministro.

---

## Resultado por categoría

| ID | Categoría | Severidad | Estado |
|---|---|---|---|
| F-SAST-01 | Secretos embebidos | Critical | ✅ Limpio |
| F-SAST-02 | Inyección SQL | Critical | ✅ Limpio |
| F-SAST-03 | Inyección de comandos | Critical | ✅ Sin superficie |
| F-SAST-04 | Deserialización insegura | Critical | ✅ Sin superficie |
| F-SAST-05 | Path traversal | High | ✅ Sin superficie |
| F-SAST-06 | XSS | High | ✅ Limpio |
| F-SAST-07 | SSRF | High | ✅ Sin superficie |
| F-SAST-08 | Criptografía rota | High | ✅ Sin superficie |
| F-SAST-09 | Modo debug en producción | High | ✅ Sin cambios — `appsettings*`/`launchSettings.json` no tocados |
| F-SAST-10 | Log de datos sensibles | High | ✅ Limpio |
| F-SAST-11 | Upload sin restricción | High | ✅ Sin superficie |
| F-SAST-12 | Falta de protección CSRF | High | ✅ Sin cambios — `Program.cs` solo agrega un registro DI |
| F-SAST-13/16 | CVEs en dependencias | Critical/High/Medium | ✅ Limpio |
| F-SAST-14 | Validación de entrada incompleta | Medium | ✅ Limpio |
| F-SAST-15 | Manejo de errores que filtra internals | Medium | ✅ Limpio |
| F-SAST-17 | Funciones inseguras | Medium | ✅ Sin superficie |

**Total: 16 categorías limpias · 0 vulnerabilidades abiertas · 0 supresiones.**

---

## Verificaciones puntuales

| Control | Evidencia |
|---|---|
| Secretos (F-SAST-01) | `grep` sobre el diff completo por patrones `password=`, `secret=`, `api[_-]?key=`, `connectionstring=`, `Server=`, `Data Source=`, `User Id=`, `Pwd=`: 0 apariciones. `GuardarrailDeSecretosTests` (barrido existente sobre todo lo versionado) sigue verde con los archivos nuevos dentro de su escaneo — confirmado por la corrida completa de la suite (269/269). |
| SQL crudo (F-SAST-02) | 0 apariciones de `FromSqlRaw`/`ExecuteSqlRaw`. Las únicas consultas SQL literales del delta son `Database.SqlQuery<string>($"""...""")` en `EsquemaDeSolicitudTests.cs` (código de test, no de producción), con `{TablaDeSolicitudes}` y `{nombreIndice}` como huecos interpolados — EF Core los parametriza automáticamente (no es concatenación de cadenas), y además `nombreIndice` es una constante del propio test, nunca una entrada externa. El resto del acceso a datos nuevo (`SaldoService`, `SolicitudesService`) es LINQ parametrizado por EF Core. |
| Comandos / deserialización (F-SAST-03/04) | 0 apariciones de `Process.Start`, `eval`, `BinaryFormatter`, `XmlSerializer` en el diff. |
| Path traversal (F-SAST-05) | Los 4 `File.ReadAllText`/`ReadAllLines` del diff están todos en código de test (`FuenteSinComentarios.cs`, `SaldoEnPantallaTests.cs`, escaneos de `PuntoUnicoDelTopeTests.cs`) y leen rutas fijas dentro del propio repositorio (archivos `.cs`/`.razor`/el PRD), nunca una ruta construida con entrada de usuario. |
| `MarkupString` (F-SAST-06) | 0 apariciones en el proyecto Web. `SaldoEnPantallaTests.Ningun_razor_nombra_TimeProvider` incluye `MarkupString` en su lista de tokens prohibidos y pasa contra el componente nuevo. |
| Criptografía (F-SAST-08) | 0 usos de `MD5`/`SHA1`/`DES`/`ECB` en el delta. |
| Logs sin PII (F-SAST-10, R-12/R-14/R-18) | Las 2 llamadas nuevas a `_registro.LogInformation` (`SolicitudesService.cs:337,382`) usan plantilla estructurada — nunca interpolación directa del mensaje compuesto — y solo llevan `EmpleadoId`, años y cantidades de días; nunca `MensajeDeError` ni el saldo real. Los 2 `catch` nuevos de `SaldoDelEmpleado.razor` (`:124,185`) llaman `Registro.LogError(excepcion, "...")` con un literal fijo, sin `excepcion.Message` en ningún texto expuesto a la UI. `DiagnosticoSinPiiTests` y `MensajesLiteralesDeFeat001bTests` fijan por reflexión que ningún `ToString()` nuevo (`SaldoDelAnio`, el `ResultadoDelAlta` con motivo de saldo) expone cantidades de días ni identificadores de persona. |
| Validación en servidor (F-SAST-14) | `SaldoService.DeLosAniosAsync` valida año fuera de `[1, 9999]` y más de 2 años por llamada con `ArgumentOutOfRangeException`, antes de tocar la base (R-15). `SolicitudesService.CrearAsync` valida el tope después de las dos validaciones de fecha existentes y antes de persistir. El botón de envío del formulario no incorporó ninguna validación nueva del lado del cliente que reemplace al servidor (R-10, sin tocar). |
| Errores sin internals (F-SAST-15) | Los 2 `catch` nuevos de `SaldoDelEmpleado.razor` renderizan un estado genérico (`_fallo = true`, sin número ni mensaje de excepción) — la UI nunca muestra `excepcion.Message`. `SolicitudesService.CrearAsync` no agrega ningún `catch`: los fallos de persistencia y los conflictos de serialización se propagan tal cual (R-16), sin envolver ni exponer una traza distinta de la que ya maneja `Program.ConfigurarCanalizacion` (sin tocar en este ticket). |
| CSRF (F-SAST-12) | `Program.cs` solo agrega `servicios.AddScoped<SaldoService>()`; el pipeline de middleware (`UseAntiforgery`, orden canónico) no fue tocado. |
| Dependencias (F-SAST-13/16) | `dotnet list src/GestionVacaciones.slnx package --vulnerable --include-transitive`: 0 paquetes vulnerables en los tres proyectos. Ningún `.csproj` modificado — 0 dependencias nuevas. |

---

## Hallazgos que no bloquean

Ninguno. Las dos auditorías de arquitectura por bloque (Bloques 1-5) y esta pasada de SAST no
encontraron ningún patrón que requiera seguimiento fuera de lo ya documentado en
`docs/daw/security/threat-FEAT-001b.md`.

---

## Veredicto

```
┌─────────────────────────────────────────────────────────────┐
│  /daw-security-sast FEAT-001b — PASSED                       │
├─────────────────────────────────────────────────────────────┤
│  Total: 16 categorías limpias, 0 vulnerabilidades abiertas   │
│         (0 críticas, 0 altas, 0 medias)                      │
│  Supresiones: 0                                               │
│  No bloqueantes: 0                                            │
│  Next: gate desbloqueado — CODE puede cerrar                  │
└─────────────────────────────────────────────────────────────┘
```
