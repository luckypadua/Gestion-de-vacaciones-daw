# SAST FEAT-001a — Análisis estático de seguridad

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001a |
| Tier | FEATURE |
| Rama | `feat/FEAT-001a-andamiaje-alta-solicitud` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-001a.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §4 |
| Fecha | 2026-08-04 |
| Intentos | 2 (el primero quedó BLOCKED por un hallazgo alto) |
| Resultado | **PASSED** |

## Alcance

Los 6 bloques del ticket: `src/GestionVacaciones.Data/`, `src/GestionVacaciones.Web/`,
`tests/GestionVacaciones.Tests/` y la configuración versionada. Incluye la auditoría de dependencias
sobre los tres proyectos, con transitivas.

---

## Resultado por categoría

| ID | Categoría | Severidad | Estado |
|---|---|---|---|
| F-SAST-01 | Secretos embebidos | Critical | ✅ Limpio — 1 falso positivo documentado |
| F-SAST-02 | Inyección SQL | Critical | ✅ Limpio |
| F-SAST-03 | Inyección de comandos | Critical | ✅ Sin superficie |
| F-SAST-04 | Deserialización insegura | Critical | ✅ Sin superficie |
| F-SAST-05 | Path traversal | High | ✅ Sin superficie |
| F-SAST-06 | XSS | High | ✅ Limpio |
| F-SAST-07 | SSRF | High | ✅ Sin superficie |
| F-SAST-08 | Criptografía rota | High | ✅ Sin superficie |
| F-SAST-09 | Modo debug en producción | High | ✅ **Corregido en el intento 2** |
| F-SAST-10 | Log de datos sensibles | High | ✅ Limpio |
| F-SAST-11 | Upload sin restricción | High | ✅ Sin superficie |
| F-SAST-12 | Falta de protección CSRF | High | ✅ Limpio |
| F-SAST-13/16 | CVEs en dependencias | Critical/High/Medium | ✅ Limpio |
| F-SAST-14 | Validación de entrada incompleta | Medium | ✅ Limpio |
| F-SAST-15 | Manejo de errores que filtra internals | Medium | ✅ Limpio |
| F-SAST-17 | Funciones inseguras | Medium | ✅ Sin superficie |

**Total: 16 categorías limpias · 0 vulnerabilidades abiertas · 1 supresión documentada.**

---

## Hallazgo corregido — F-SAST-09 · HIGH · R-01

### Qué se encontró

El modelo de amenazas clasifica **R-01 como CRITICAL** y lo mitiga con «dos condiciones
**independientes**» para activar el sustituto de identidad de desarrollo: entorno `Development` **y**
la clave `Vacaciones:PermitirIdentidadDeDesarrollo`.

**Las dos condiciones no eran independientes.** La clave vivía en
`src/GestionVacaciones.Web/appsettings.Development.json`, un archivo que:

1. Solo se carga cuando ya se cumple la primera condición, y
2. **viajaba en el artefacto de `dotnet publish -c Release`**, con la clave en `true`.

Comprobado con un `publish` real, no por lectura del código.

**Camino de explotación.** Un host productivo con `ASPNETCORE_ENVIRONMENT=Development` mal puesta:
se carga el archivo → la clave llega sola → se registran `EmpleadoActualDesarrollo` y
`EmpleadosService` → `VerificacionDeIdentidad` consulta **esa misma variable** y no lanza → la
aplicación arranca con un desplegable de identidades sin credencial, la nómina completa (PII) a la
vista, `DetailedErrors` activo, sin `UseExceptionHandler` ni `UseHsts`, y `CadenaDeConexion.Resolver`
salteando la exigencia de `Encrypt=True`.

**Agravante.** Un test —`ComposicionDeIdentidadTests.El_appsettings_versionado_habilita_la_identidad_de_desarrollo_de_punta_a_punta`—
**fijaba la conducta vulnerable como esperada**. El acoplamiento se había detectado en el andamiaje
de los tests, pero la conclusión no se trasladó al artefacto de despliegue.

### Por qué las revisiones anteriores no lo vieron

Las dos auditorías de arquitectura del Bloque 4 dieron PASS, y con razón: **el código implementaba
exactamente la mitigación que el modelo de amenazas había aprobado**. El defecto no estaba en la
implementación sino en la premisa —que las dos condiciones fueran independientes—, y esa premisa solo
se cae al mirar de dónde sale cada una y qué archivos entran en el artefacto publicado. Hizo falta un
`publish` real para verlo.

### Corrección aplicada — dos capas

**Capa 1 · La clave sale del artefacto.**

- Se elimina de `appsettings.Development.json` (versionado y publicable).
- Pasa a `Properties/launchSettings.json`, bajo `environmentVariables`, como
  `Vacaciones__PermitirIdentidadDeDesarrollo`. Se eligió sobre user-secrets porque está versionado
  —un clon nuevo conserva la experiencia de desarrollo sin pasos manuales— y **no se publica**.
- `<Content Update="appsettings.Development.json" CopyToPublishDirectory="Never" />` en el `.csproj`
  del Web, como segunda barrera de la misma capa.

Con esto las dos condiciones del modelo de amenazas se vuelven de verdad independientes, que es lo
que el modelo ya afirmaba.

**Capa 2 · Guardarraíl de compilación.**

`CompilacionDelArtefacto.EsDeDepuracion` es la **tercera** condición, y la única que no sale de la
configuración: se decide al compilar, y lo que se despliega se compila en `Release`. Un artefacto de
Release no registra el proveedor de desarrollo con **ninguna** combinación de variables de entorno.

Detalles de diseño que importan:

- Es una **propiedad con inicializador y no una `const`**: el valor de una constante se incrusta en
  cada ensamblado que la lee al compilarlo, así que un consumidor compilado aparte podría afirmar lo
  contrario de lo que dice el artefacto que de verdad se despliega.
- `PermiteIdentidadDeDesarrollo` y `Verificar` tienen sobrecarga que recibe la condición, de modo que
  un test puede ejercitar el comportamiento de Release sin compilar en Release. Que esa costura no se
  pueda esquivar lo sostienen dos asertos más: el `DebuggableAttribute` del ensamblado contra la
  propiedad, y el escaneo de que **`#if` existe en un único archivo** de todo el proyecto Web.
- **Contrapartida intencional y documentada:** correr la aplicación localmente en Release no ofrece
  el selector de empleado, ni con el entorno y la clave puestos.

**Corrección del comentario falso.** El XML-doc de `VerificacionDeIdentidad` afirmaba que un entorno
mal puesto «no alcanza, porque además haría falta la clave». Era falso con el artefacto publicado.
Ahora dice que lo era, por qué, y qué lo hace cierto hoy.

### Verificación del cierre

| Comprobación | Resultado |
|---|---|
| `dotnet publish -c Release` → archivos de configuración en el artefacto | solo `appsettings.json` |
| `grep -rl "PermitirIdentidadDeDesarrollo"` sobre el artefacto | **0 apariciones** |
| `Properties/` en el artefacto | no viaja |
| Suite en Debug | 188 passed, 0 failed, 0 skipped |
| Suite en **Release** | 177 passed, 0 failed, **11 skipped** |
| `#if` en el proyecto Web | 1 solo archivo (`CompilacionDelArtefacto.cs`) |

Los **11 salteos en Release son la evidencia de punta a punta**: son exactamente los tests que
necesitan el proveedor de identidad de desarrollo, y el artefacto de Release se lo niega. Se saltean
con motivo explícito y nunca en rojo, con la misma disciplina que el repositorio ya aplica a los
tests que dependen de la instancia SQL Server.

---

## Supresiones

### Supresión: F-SAST-01 — cadenas de conexión con `Password=` en el proyecto de tests

| Campo | Valor |
|---|---|
| Archivo | `tests/GestionVacaciones.Tests/Andamiaje/ArranqueDelHostTests.cs:22,28,31,34-35,39` · `tests/GestionVacaciones.Tests/Andamiaje/CadenaDeConexionTests.cs:17,21,28-29,35,41,47,53,59,66,144` · `tests/GestionVacaciones.Tests/Persistencia/GuardarrailDeBaseDeTestTests.cs:37,168,175,191,230,248,264,293` · `tests/GestionVacaciones.Tests/Persistencia/BaseInalcanzableTests.cs:24-25` · `tests/GestionVacaciones.Tests/Persistencia/InvocacionDeLaSemillaTests.cs:35-36,44-45` · `tests/GestionVacaciones.Tests/Identidad/HostConIdentidad.cs:37` · `tests/GestionVacaciones.Tests/Persistencia/ComposicionDeAccesoADatosTests.cs:32` |
| Categoría | Secretos embebidos en el código (CWE-798, F-SAST-01) |
| Disposición | FALSE_POSITIVE |
| Revisor | aescudero@bas.com.ar |
| Fecha | 2026-08-04 |
| Justificación | Se revisaron las 31 apariciones una por una, no una muestra. Todo valor de `Password=` es el literal `valor-ficticio-de-test` y todo `User ID=` es `usuario-ficticio`; no existe ningún otro valor en el repositorio. Los hosts son etiquetas no resolubles (`host-de-prueba`, `host-de-la-variable`, `host-de-la-configuracion`, `host-de-los-secretos`), `localhost` o `127.0.0.1` con el puerto muerto `14330`; ninguno alcanza la instancia `NTKLUCIANOE\SQL2022`, que desde WSL no vive en el loopback y que nunca apareció en el árbol ni en el historial (`git log --all -S "NTKLUCIANOE"` no devuelve nada). Los catálogos reales `GestionVacacionesV2` y `GestionVacaciones` aparecen únicamente como entrada de los tests de la denylist de B2-T11, cuyo aserto es que el fixture aborta **antes** de abrir la conexión. Estas constantes no son credenciales filtradas: son el sujeto bajo prueba de los guardarraíles de R-02, y el propio `valor-ficticio-de-test` existe para comprobar que nunca aparece en un mensaje de error. |
| Control compensatorio | `GuardarrailDeSecretosTests` rompe el build si aparece una credencial en archivo versionado. **Su barrido se extendió en este mismo intento** de `appsettings*.json` a `.cs` y `.razor`, con lista blanca **por valor** —no por archivo, que dejaría archivos sin auditar para siempre—: sin esa extensión, la familia de archivos donde SAST encontró estas cadenas no la auditaba nadie y una cadena real pegada en una constante de test habría pasado. La cadena real se lee de `VACACIONES_CONNECTION_TEST` o de user-secrets y nunca se versiona. `CadenaDeConexionResuelta` lleva `ToString()` sobrescrito y `[JsonIgnore]` sobre `Valor`, con ocho asertos que verifican que el valor no llega a un mensaje de excepción, a un `ToString()` ni a un JSON. |
| Revisar antes de | 2027-02-04 |

> **Sobre la forma.** §4.1 marca F-SAST-01 como no suprimible, pero esa columna gobierna los
> *verdaderos positivos*. §4.4 abre con «Every finding, including false positives, must be
> documented», y la sección «What can NEVER be a WARNING» incluye «An undocumented security finding
> (not even false positives)». Un falso positivo en categoría Critical se documenta con este bloque,
> que es el único formato definido. La supresión no oculta una credencial: certifica que no la hay.

---

## Hallazgos que no bloquean

**A2 · INFO — `ConfigurarCanalizacion` no tiene ninguna aserción encima.** El manejador de
excepciones, HSTS, la redirección HTTPS y `UseAntiforgery` son correctos por inspección, pero nada
los fija: **borrar `UseAntiforgery()` o `UseHsts()` deja la suite en verde.** El propio código lo
declara como deuda. Corresponde a VERIFY (F-VER-04, F-VER-06) más que a este gate.

**Patrón sistémico — recomendación de ADR.** `ASPNETCORE_ENVIRONMENT` gobierna hoy **seis** controles
de seguridad independientes: identidad de desarrollo, servicio de nómina (R-04), `DetailedErrors`
(F-SAST-09), manejador de excepciones + HSTS, exigencia de cifrado en tránsito (F-TM-07) y ejecución
de `SeedDatos` (R-03). Es el mismo diagnóstico que R-01 hace del diseño original —«confía en una
única condición booleana»—, y la capa 2 lo corrige **solo para la identidad**. Vale un ADR que decida
entre extender la señal de compilación a los demás controles o aceptar y documentar el riesgo con
fecha de revisión.

---

## Verificaciones puntuales

| Control | Evidencia |
|---|---|
| `UseAntiforgery` (F-SAST-12) | `Program.cs:286`, en el orden canónico: después de `UseStaticFiles`, antes de `MapRazorComponents` |
| HSTS + redirección HTTPS | `UseHsts()` fuera de `Development` (`:281`), `UseHttpsRedirection()` siempre (`:284`) |
| SQL crudo (F-SAST-02) | 0 apariciones de `FromSqlRaw`, `ExecuteSqlRaw`, `SqlQueryRaw`. Todo LINQ parametrizado por EF Core |
| `MarkupString` (F-SAST-06) | 0 apariciones en el proyecto Web, auditado por `ComponentesSinAccesoADatosTests` |
| Logs sin PII (F-SAST-10, R-12) | Las 6 llamadas de log llevan `EmpleadoId`, nombre de catálogo o cantidades. `DiagnosticoSinPiiTests` fija los `ToString()` de los cinco tipos que podrían filtrar por interpolación |
| Validación en servidor (F-SAST-14, R-10) | `SolicitudesService.CrearAsync` valida las dos fechas antes de persistir; `EmpleadosService.ExisteEnLaNominaAsync` rechaza `Id <= 0` sin consultar y verifica existencia. `PuedeEnviarse` del formulario comprueba «están las dos fechas», nunca «el período es válido» |
| Errores sin internals (F-SAST-15) | `UseExceptionHandler` escribe un literal fijo, `500`, `text/plain`. `DetailedErrors = esDesarrollo`. Los tres `catch` de la UI renderizan constantes genéricas y nunca `excepcion.Message` |
| Dependencias (F-SAST-13/16) | `dotnet list package --vulnerable --include-transitive`: 0 paquetes vulnerables en los tres proyectos |

---

## Veredicto

```
┌─────────────────────────────────────────────────────────────┐
│  /daw-security-sast FEAT-001a — PASSED                       │
├─────────────────────────────────────────────────────────────┤
│  Total: 16 categorías limpias, 0 vulnerabilidades abiertas   │
│         (0 críticas, 0 altas, 0 medias)                      │
│  Corregidas en este ticket: 1 alta (F-SAST-09 / R-01)        │
│  Supresiones: 1, con los 7 campos (F-SAST-01)                │
│  No bloqueantes: 1 INFO + 1 recomendación de ADR             │
│  Next: gate desbloqueado — CODE puede cerrar                 │
└─────────────────────────────────────────────────────────────┘
```
