# SAST FIX-001 — Análisis estático de seguridad

| Campo | Valor |
|-------|-------|
| Ticket | FIX-001 |
| Tier | FIX |
| Rama | `fix/FIX-001-link-autorizaciones-no-aparece` |
| Modelo de amenazas | `docs/daw/security/threat-FIX-001.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §3/§4 |
| Fecha | 2026-08-07 |
| Ejecuciones | 2 · cierre de CODE + recierre tras bucle correctivo de VERIFY |
| Resultado | **PASSED** |

## Alcance

**Ejecución 1** — el delta completo del fix: `IEmpleadoActualProvider.cs` (evento nuevo en la
interfaz), `EmpleadoActualDesarrollo.cs` y `EmpleadoActualNoConfigurado.cs` (implementación del
evento), `MainLayout.razor` (reevaluación reactiva), `IdentidadDePrueba.cs` y
`AutorizacionesTests.cs` (tests). Ningún `.csproj` fue tocado: `git diff --stat -- '*.csproj'`
vacío, 0 dependencias nuevas. `grep -iE "password|secret|apikey|connectionstring|token"` y
`grep -E "FromSqlRaw|ExecuteSqlRaw|MarkupString"` sobre el delta: 0 apariciones en ambos.
`dotnet list ... --vulnerable --include-transitive` en `Data` y `Web`: 0 paquetes vulnerables.

**Ejecución 2** (recierre) — el delta agregado por el bucle correctivo VERIFY→CODE (1 FAIL de
evidencia TDD, documentado en el commit; 2 WARN no bloqueantes): solo un test nuevo en
`AutorizacionesTests.cs` que cubre `EmpleadoActualNoConfigurado.IdentidadCambiada`. Ningún archivo
de producción ni `.csproj` cambió. Mismos `grep` sobre el delta: 0 apariciones.

## Resultado por categoría

| ID | Categoría | Severidad | Estado |
|---|---|---|---|
| F-SAST-01 | Secretos embebidos | Critical | ✅ Limpio |
| F-SAST-02 | Inyección SQL | Critical | ✅ Sin superficie — ninguna query nueva |
| F-SAST-03 | Inyección de comandos | Critical | ✅ Sin superficie |
| F-SAST-04 | Deserialización insegura | Critical | ✅ Sin superficie |
| F-SAST-05 | Path traversal | High | ✅ Sin superficie |
| F-SAST-06 | XSS | High | ✅ Sin superficie — sin marcado nuevo, ningún `MarkupString` |
| F-SAST-07 | SSRF | High | ✅ Sin superficie |
| F-SAST-08 | Criptografía rota | High | ✅ Sin superficie |
| F-SAST-09 | Modo debug en producción | High | ✅ Sin cambios — ningún `appsettings*` tocado |
| F-SAST-10 | Log de datos sensibles | High | ✅ Limpio — el `LogError` existente no cambió su mensaje (sin PII); el nuevo catch de `SinEmpleadoSeleccionadoException` no loguea nada |
| F-SAST-11 | Upload sin restricción | High | ✅ Sin superficie |
| F-SAST-12 | Falta de protección CSRF | High | ✅ Sin cambios — ningún endpoint ni middleware tocado |
| F-SAST-13/16 | CVEs en dependencias | Critical/High/Medium | ✅ Limpio — 0 dependencias nuevas, `dotnet list package --vulnerable` sin hallazgos |
| F-SAST-14 | Validación de entrada incompleta | Medium | ✅ Limpio — `SeleccionarAsync(empleadoId, ...)` no cambió de firma ni de validación (sigue contra la nómina); el evento no lleva payload que validar |
| F-SAST-15 | Manejo de errores que filtra internals | Medium | ✅ Limpio — ambos catches de `EvaluarSiTieneEquipoACargoAsync` solo afectan la visibilidad de un link, ningún detalle llega al cliente |
| F-SAST-17 | Funciones inseguras | Medium | ✅ Sin superficie |

**Total: 16 categorías limpias · 0 vulnerabilidades Critical/High/Medium abiertas · 0 supresiones.**

## Nota sobre el riesgo MEDIUM del modelo de amenazas

El único riesgo no-LOW de `threat-FIX-001.md` (DoS por excepción no contenida en el handler
reactivo) es de **disponibilidad**, no de las categorías de este catálogo SAST — se verificó por
diseño y por cobertura de tests (`EvaluarSiTieneEquipoACargoAsync` compartido entre ambas rutas), no
por un patrón que un escaneo estático detecte. Ya cerrado en la implementación: el `try/catch` vive
dentro del método compartido, nunca en el borde de `InvokeAsync`.

---

┌─────────────────────────────────────────────────────────────┐
│  /daw-security-sast — PASSED                                  │
├─────────────────────────────────────────────────────────────┤
│                                                                │
│  Secrets: ✅ F-SAST-01 limpio                                  │
│  Injection: ✅ F-SAST-02/03 sin superficie                      │
│  XSS y funciones inseguras: ✅ F-SAST-04/06/17 sin superficie/limpio │
│  Dependencies: ✅ F-SAST-13/16 — 0 paquetes vulnerables           │
│                                                                │
│  Suppressions: 0                                                │
│                                                                │
│  ────────────────────────────────────────────────────────────  │
│  Total: 16 limpias, 0 vulnerabilidades (0 critical, 0 high)      │
│  Report: docs/daw/security/sast-FIX-001.md                       │
│  Next: cerrar CODE y pasar a VERIFY                               │
└─────────────────────────────────────────────────────────────┘
