# Verificación FEAT-001a

| Campo | Valor |
|-------|-------|
| Ticket | FEAT-001a |
| Tier | FEATURE |
| PRD | `docs/daw/prd/prd-FEAT-001a.md` |
| Spec | `docs/daw/specs/spec-FEAT-001a.md` |
| Modelo de amenazas | `docs/daw/security/threat-FEAT-001a.md` |
| SAST | `docs/daw/security/sast-FEAT-001a.md` |
| Catálogo de reglas | `.daw/rules/validation-rules.instructions.md` §5 |
| Rondas | 2 (la primera BLOCKED, la segunda PASSED) |

---

## Ronda 1 — 2026-08-04 · HEAD `81c37af` · **BLOCKED**

### Comandos ejecutados por el verificador, no delegados

| Comando | Resultado |
|---|---|
| `dotnet build src/GestionVacaciones.slnx` | 0 advertencias, 0 errores |
| `dotnet test src/GestionVacaciones.slnx` | 188 passed, 0 failed, 0 skipped |
| `dotnet test --filter "Category=Integracion"` | 29 passed, 0 skipped (motor SQL Server 2022 real) |
| `dotnet format --verify-no-changes --severity info` | 0 × IDE0005, IDE0051/52, CS0169/CS0414 |

### Resultado por regla

| Regla | Resultado | Detalle |
|---|---|---|
| F-VER-01 | ✅ PASS | 7/7 AC con test que valida el comportamiento y pasa |
| F-VER-02 | ✅ PASS | 6/6 bloques implementados (1 desvío aprobado) |
| F-VER-03 | ✅ PASS | 92,82% líneas · 89,23% ramas · **95,52% funciones** |
| F-VER-04 | ❌ **FAIL** | `ConfigurarCanalizacion` sin ningún test |
| F-VER-05 | ✅ PASS | 0 advertencias, 0 errores con `TreatWarningsAsErrors` |
| F-VER-06 | ✅ PASS | 49/49 tests exigidos por la spec — faltan 0 |
| W-VER-01 | ⚠️ 5 hallazgos | Código inalcanzable y constantes sin aserto |
| W-VER-02 | ✅ No aplica | Dominio en 97,15% L / 94,11% R, por encima del 90% recomendado |
| W-VER-03 | ⚠️ 1 hallazgo | Asertos sobre la tabla entera en base compartida |

### F-VER-01 — Trazabilidad de los 7 criterios de aceptación

| AC | Implementación | Test |
|---|---|---|
| AC-01 | `FormularioDeAlta.razor:122-136` | `FormularioDeAltaTests.cs:56` (B6-T1) |
| AC-02 | `SolicitudesService.cs:233-236` | `ValidacionDelAltaTests.cs:38` (B5-T3) · `FormularioDeAltaTests.cs:86` (B6-T2) |
| AC-03 | `SolicitudesService.cs:239-242` | `ValidacionDelAltaTests.cs:55` (B5-T4) · `FormularioDeAltaTests.cs:107` (B6-T3) |
| AC-04 | `SolicitudesService.cs:244-268` | `AltaDeSolicitudTests.cs:36` (B5-T5), motor real |
| AC-05 | `SolicitudesService.cs:292-319` | `ListadoPropioTests.cs:34` (B5-T7) · `ListadoDeSolicitudesTests.cs:28` (B6-T4) |
| AC-06 | `EmpleadoActualNoConfigurado.cs` + `VerificacionDeIdentidad.cs:130-173` | `ComposicionDeIdentidadTests.cs:82` (B4-T1) |
| AC-07 | `EmpleadoActualDesarrollo.cs:72-86` + `SelectorDeEmpleado.razor` | `SeleccionDeEmpleadoTests.cs:44` (B4-T3) · `SeleccionDeEmpleadoEnLaInterfazTests.cs:59` (B6-T5) |

Los tests se evaluaron por sus asertos, no por sus nombres. Ninguno resultó tautológico.

### ❌ F-VER-04 — El FAIL que bloquea

**`src/GestionVacaciones.Web/Program.cs:266-291` (`ConfigurarCanalizacion`) no tiene ningún test.**
Ni de camino feliz ni de camino triste. Cobertura 0% en sus 16 líneas ejecutables y en sus 2 ramas.
`grep -rn "ConfigurarCanalizacion\|UseAntiforgery\|UseHsts\|UseExceptionHandler\|UseHttpsRedirection" tests/`
devuelve 0 resultados.

**Por qué es FAIL y no WARN**, con tres fundamentos:

1. **Es una mitigación nombrada del riesgo CRITICAL, verificada a medias.**
   `threat-FEAT-001a.md:166-167` lista como mitigación 3 de R-01: «`DetailedErrors = false` **y sin
   página de excepciones del desarrollador** fuera de `Development`». La primera mitad vive en
   `Program.cs:106` y está cubierta; la segunda vive en la rama `if (!IsDevelopment())` de
   `ConfigurarCanalizacion` (`:270-282`), con su `UseExceptionHandler` genérico y su `UseHsts()`, y esa
   rama tiene **0 ejecuciones**.

2. **La rama no ejercitada es exactamente el camino triste que F-VER-04 protege.** El fundamento
   declarado de la regla es que los peores defectos viven en los caminos tristes. Acá el camino triste
   es el entorno de producción: la única rama con consecuencia de seguridad, la que nadie ejecuta al
   desarrollar y la que nadie testea.

3. **El precedente está dentro de este mismo ticket.** `sast-FEAT-001a.md` documenta por qué las dos
   auditorías de arquitectura del Bloque 4 dieron PASS sobre código vulnerable —el defecto estaba en la
   premisa, no en la implementación— y que existía un test que fijaba la conducta vulnerable como
   esperada. El remedio adoptado fue agregar asertos. Aceptar acá «es correcto por inspección» sería
   aplicar, en el mismo ticket y sobre la misma familia de controles, el estándar que este ticket ya
   demostró insuficiente.

La evidencia única de F-SAST-12 (protección CSRF) es hoy la inspección visual de `Program.cs:286`.
**Borrar `UseAntiforgery()` o `UseHsts()` deja la suite en verde.**

**F-VER-06 no está violado:** ningún test exigido por la spec —B1-T1 a B6-T9— cubría la canalización
HTTP. La spec no se comprometió a esto; el hueco es de la regla de caminos tristes, no de un
compromiso incumplido.

### F-VER-03 — Piezas por debajo del umbral que el agregado esconde

| Archivo | L | R | F | Lectura |
|---|---|---|---|---|
| `Web/Program.cs` | 68,4 | 75,0 | 71,4 | **única pieza con lógica propia por debajo en los tres ejes**; el faltante es `ConfigurarCanalizacion` + `Main` |
| `Components/Solicitudes/FormularioDeAlta.razor` | 75,0 | 84,6 | 100 | falta el camino feliz del envío y el `catch` |
| `Components/Identidad/SelectorDeEmpleado.razor` | 78,7 | 80,0 | 100 | falta el `catch` de `ElegirAsync` |
| `Components/Pages/MisSolicitudes.razor` | 86,7 | 78,6 | 100 | falta `catch (SinEmpleadoSeleccionadoException)` |
| `Data/VacacionesDbContextFactory.cs` | 0 | 0 | 0 | andamiaje de tiempo de diseño, con una rama deliberada sin cubrir |
| `Components/Layout/MainLayout.razor` | 0 | — | 0 | andamiaje sin lógica |
| `Migrations/…_InicialV2.cs` | 92,2 | — | 50,0 | el método `Down`, fuera de alcance por W-SPEC-03 |

El proyecto `Data` está en 97,15% L / 94,11% R, y `SolicitudesService`, `PermisosService`,
`EmpleadosService` y `CalculadorDeDiasCorridos` al 100% en los tres ejes.

### Los 7 puntos de `## Final verification` de la spec

Los 7 se cumplen, más el 6b. Dos merecen registro:

- **Punto 4** (con `ASPNETCORE_ENVIRONMENT=Production` la aplicación no arranca con el proveedor de
  desarrollo): **sigue siendo cierto y hoy está mejor sostenido que cuando se escribió**, gracias a la
  tercera condición de compilación que agregó el cierre de SAST. Verificado que los tests incluyen
  **las dos contracaras**: sin ellas, «Release no habilita» lo cumpliría un guardarraíl que no
  habilitara nunca, y el entorno de desarrollo quedaría sin selector con la suite en verde.
- **Punto 6b** (ningún test de integración contra un catálogo sin sufijo `_Test`): **cerrado**, y
  verificado buscando el bypass en vez de leyendo el guardarraíl. El guardarraíl vive también en la
  primitiva `BaseDeDatosDeTest.CrearContexto`; los dos únicos lugares donde un test construye su propio
  contexto toman la cadena de un contexto del fixture, o sea después de que el guardarraíl ya corrió.

### Advertencias

**W-VER-01 — código inalcanzable y constantes sin aserto** (0 imports sin usar, 0 miembros privados
muertos, verificado con el compilador):

1. `MisSolicitudes.razor:127-132` — `catch (SinEmpleadoSeleccionadoException)`, inalcanzable con los dos
   proveedores existentes. Declarado como previsión en su propio XML-doc.
2. `FormularioDeAlta.razor:158-161` — guard de fechas nulas, inalcanzable por la UI.
3. `SelectorDeEmpleado.razor:94-97` — guard de selección nula, inalcanzable.
4. `MisSolicitudes.razor:64` y `SelectorDeEmpleado.razor:51` — constantes `public` que ningún test
   asserta. La segunda importa: el test que recorre la rama de rechazo nunca comprueba que el aviso
   aparezca en pantalla.
5. `VacacionesDbContextFactory.cs:49-64` — 0% en los tres ejes, con una rama deliberada y documentada.
   Su único test compara dos constantes y nunca invoca `CreateDbContext`.

**W-VER-03 — tests frágiles:** `SemillaDeDesarrolloTests.cs:55,135,168,207` y
`SeleccionDeEmpleadoTests.cs:135` afirman sobre la tabla **entera** de una base compartida. Hoy
funcionan solo porque las colecciones declaran `DisableParallelization = true` y la limpieza es por
`IAsyncDisposable`. Una limpieza que falle, o una colección nueva en paralelo, los pone rojos por un
motivo ajeno a lo que afirman. El resto de los tests de integración ya usa el patrón correcto
(empleado descartable con correo aleatorio y filtro por `Id`).

**Sin hallazgo:** ningún `DateTime.Now/Today/UtcNow` fuera de las denylists; los Ids constantes se usan
solo con fábricas que lanzan; el formato de fecha del listado usa `CultureInfo.InvariantCulture` con
patrón explícito, así que no es frágil por cultura.

### Evidencia TDD — estado por bloque

| Bloque | Commit | Evidencia en el registro |
|---|---|---|
| B1 | `506383e` | sin registro de rojo previo |
| B2 | `05d72d1` | conteo de tests, sin rojo previo |
| B3 | `2d25f71` | **declara la brecha explícitamente** y no la presenta como verificada |
| B4 | `fd4f89f` | **reconstruida por mutación**: 8 mutaciones de una línea, cada una mapeada a los tests que se pusieron rojos. La más fuerte del ticket |
| B5 | `ef0c6cd` | **perdida.** La sesión que lo implementó se cortó. Irreconstruible: los 32 tests en verde y dos revisiones en PASS no sustituyen el rojo previo |
| B6 | `0ec194f` | El implementador **sí reportó** dos rojos consecutivos —15 errores `CS0246`, y después `Failed: 10, Passed: 8` con los asertos citados— y el orquestador los revisó, pero **eso no quedó en el cuerpo del commit**. La evidencia existió y se verificó; el registro versionado no la conserva |

Decisión registrada del usuario para B5: dejar constancia y avanzar. B6 queda registrado acá, que es
donde el expediente del ticket lo conserva.

### NFR sin evidencia ejecutada

- **NFR-01** (p95 < 3 s con 50 concurrentes): la spec lo declara **diferido** a un ticket de
  performance propio, en dos lugares (`spec:37` y `spec:624`). Declarado, no omitido. Las mitigaciones
  de diseño existen y una está verificada —B2-T5 comprueba el índice con su dirección descendente—,
  pero **no hay ninguna medición de latencia**.
- **NFR-03** (2 últimas versiones de Chrome, Edge y Firefox): la spec lo declara de **verificación
  manual** porque bUnit no abre navegadores (`spec:39` y `spec:626`). Declarado, no omitido. **No hay
  en el repositorio constancia de que se haya realizado.** La spec la programa «al cerrar el ticket»:
  es tarea pendiente de RELEASE, y «declarada como manual» no es «realizada».

### Recomendación heredada del SAST, no bloqueante

`ASPNETCORE_ENVIRONMENT` gobierna seis controles de seguridad independientes y la capa de compilación
agregada en el cierre de CODE lo corrige **solo para la identidad**. Tres de esos seis —manejador de
excepciones, HSTS y redirección HTTPS— son precisamente los que viven en el método sin asertos del
FAIL. Vale un ADR.

### Veredicto de la ronda 1

```
┌──────────────────────────────────────────────────────────────────┐
│  /daw-verify-module FEAT-001a — BLOCKED                           │
│                                                                   │
│  FAILs: 1  |  WARNs: 6  |  Reglas en PASS: 5 de 6                 │
│  AC verificados: 7/7 · Tests de la spec: 49/49 · Final verif: 7/7 │
│                                                                   │
│  Bucle correctivo → CODE                                          │
└──────────────────────────────────────────────────────────────────┘
```

**Lo mínimo para desbloquear:** tests sobre `src/GestionVacaciones.Web/Program.cs:266-291` que fijen
las dos ramas del entorno —fuera de `Development`, manejador de excepciones con cuerpo genérico y
HSTS; en `Development`, ninguno de los dos; `UseAntiforgery` siempre—, de modo que borrar cualquiera de
esos cuatro `Use*` ponga la suite en rojo. Con eso `Program.cs` sube además por encima del 80% en los
tres ejes.

---

## Bucle correctivo en CODE — 2 rondas

**Ronda 1.** Se extrajo `ConfigurarMiddleware(IApplicationBuilder, IHostEnvironment)` —mismo cuerpo,
mismo orden, entorno por parámetro— y se escribieron 8 tests en
`tests/GestionVacaciones.Tests/Andamiaje/CanalizacionHttpTests.cs`. **Sin agregar dependencias:** el
camino canónico (`Microsoft.AspNetCore.TestHost`) habría exigido un paquete nuevo, que `AGENTS.md`
obliga a justificar en la spec y la spec está congelada en CODE; pero `ApplicationBuilder`,
`DefaultHttpContext` e `IAntiforgeryValidationFeature` están en el **framework compartido** que el
proyecto ya referencia. Los tests arman la canalización real sobre el contenedor real, le enganchan un
terminal propio y observan qué le pasa a una petición: comportamiento, no texto.

**Ronda 2 — un segundo defecto dentro de la propia corrección.** Al revisar la cobertura,
`ConfigurarCanalizacion` había quedado en **0%**: los 8 tests ejercitaban `ConfigurarMiddleware` en
aislamiento y nada verificaba que el envoltorio lo invocara. Borrar esa única línea dejaba la
aplicación sin manejador de excepciones, sin HSTS, sin redirección HTTPS y sin antiforgery **con los
196 tests en verde**. La extracción de método había **movido** la costura sin testear un piso más
arriba, no cerrado. Peor que el hueco original, porque quedaba tapado por ocho tests verdes que
parecían cubrirlo. Se cerró con 2 tests que entran por `ConfigurarCanalizacion`.

Es el cuarto defecto del ticket con la misma forma —el test mira la unidad y nadie mira la
composición— y, como los otros tres, no lo encontró una revisión leyendo código: lo encontró la
cobertura y lo confirmó una mutación.

---

## Ronda 2 — 2026-08-04 · **PASSED**

Todo lo de abajo lo ejecutó el verificador, nada delegado. Las áreas que la ronda 1 ya había
verificado con detalle (F-VER-01, F-VER-02, F-VER-06 y los 7 puntos de `Final verification`) se
confirmaron por vía barata: `git diff --stat 81c37af -- src/ tests/` devuelve **un solo archivo**
(`Program.cs`), el único no rastreado es `CanalizacionHttpTests.cs`, y la suite está en verde. El
código de esas áreas no se tocó.

### F-VER-04 — cerrado, verificado por mutación

Seis mutaciones sobre `Program.cs`, cada una con build y suite completa:

| # | Mutación | Suite | Tests en rojo |
|---|---|---|---|
| 1 | borrar `UseAntiforgery()` | 2/198 rojo | `La_validacion_antiforgery_corre_en_todos_los_entornos` (Dev y Prod), en `Assert.NotNull`: la feature solo existe si corrió el middleware |
| 2 | borrar `UseHsts()` | 2/198 rojo | `Fuera_de_Development_la_respuesta_lleva_HSTS` · `El_envoltorio_de_la_canalizacion_instala_los_middleware…` |
| 3 | borrar `UseHttpsRedirection()` | 4/198 rojo | `Una_peticion_en_claro_se_redirige_a_HTTPS` (Dev y Prod) + las dos del envoltorio |
| 4 | borrar el bloque `UseExceptionHandler(...)` | 1/198 rojo | `Fuera_de_Development_una_excepcion_no_escapa_y_la_respuesta_no_lleva_traza` |
| 5 | `if (!entorno.IsDevelopment())` → `if (true)` | 3/198 rojo | las dos contracaras de `Development` + `En_Development_la_excepcion_se_propaga` |
| 6 | borrar del envoltorio la llamada a `ConfigurarMiddleware` | 2/198 rojo | ambas por el `Assert.Equal` del 307, que es la aserción puesta para distinguir «no instaló la rama de producción» de «no instaló nada» |

**Ninguna sobrevive.** Restauración demostrada: `sha256 = fb991dec…97cb8` idéntico antes y después de
cada tanda, con `git diff --stat` sin cambios (41 inserciones / 11 supresiones).

**Los tests pasan por el motivo correcto**, no por casualidad. Las dos trampas del dominio están
sorteadas y se comprobó que lo estén: HSTS no emite cabecera en `localhost`, y los tests usan un host
público (`vacaciones.ejemplo`); `UseHttpsRedirection` no redirige sin puerto conocido, y lo declaran
con `ASPNETCORE_HTTPS_PORT`. El test de la redirección afirma además que **no se alcanzó el terminal**:
redirigir y atender igual sería atender en claro.

### La extracción de método no cambió el comportamiento

Comparado mecánicamente contra `git show 81c37af`: mismo orden de ejecución, mismo cuerpo del manejador
de excepciones salvo el nombre del receptor, guarda equivalente. Lo único agregado son dos
`ArgumentNullException.ThrowIfNull` que desde el único call site no pueden dispararse. **Extract Method
puro.**

### F-VER-03 con los números nuevos — los cuatro exactos

| Métrica | Ronda 1 | Ronda 2 |
|---|---|---|
| Líneas | 92,82% | **94,60%** (981/1037) |
| Ramas | 89,23% | **90,77%** (118/130) |
| Funciones | 95,52% | **96,30%** (130/135) |
| Clase `Program` | 5/6 métodos, 68,4% L | **100% L · 100% R · 6/6 métodos** |

Ninguna pieza bajó. Las que siguen por debajo de 80 en algún eje son **las mismas de la ronda 1 y todas
fuera del delta**.

### Hallazgo nuevo que conviene registrar

**`UseStaticFiles()` es el único `Use*` que ningún test fija.** Borrado, la suite queda 198/198 en
verde. No forma parte del FAIL —no sostiene ninguna mitigación del modelo de amenazas— pero de los
cinco `Use*` de la canalización hay cuatro con test y uno sin.

### Advertencias

**Los 6 WARN de la ronda 1 siguen intactos y ninguno se agravó** — demostrado mecánicamente por el
`git diff`, no por lectura. No se atendieron a propósito: el alcance del bucle correctivo se ciñó al
FAIL, porque cada cambio extra obliga a reejecutar los gates de CODE.

**Tres WARN nuevos, todos sobre el delta y ninguno bloqueante:**

7. `CanalizacionHttpTests.cs:299` (W-VER-01) — `PasarPorAsync` construye `RespuestaObservada` con
   `LlegoAlTerminal: false` **fijo**, sin medir nada. Hoy no engaña a ninguna aserción porque los dos
   tests que lo usan no leen el campo, pero un test futuro a nivel envoltorio que lo afirme pasaría en
   verde vacuamente.
8. `ConstruirPorElEnvoltorio` (W-VER-03, latente) — el `using var puertoHttps` se libera antes de que
   corra la petición, a diferencia de `EjecutarAsync`. Funciona porque el puerto se resuelve al
   construir la canalización, y si eso dejara de ser cierto el test se pondría **rojo** (307→404), no
   verde: falla ruidosamente. Asimetría a corregir cuando toque.
9. Cosmético — un `ASP0015` informativo en `CanalizacionHttpTests.cs:145` (usar `Response.Headers.Location`)
   y un typo «middieware» en el comentario de la línea 205. No rompen el build.

**Sin hallazgo de fondo en la calidad de los tests nuevos:** la clase está en
`ColeccionDeEntornoDeProceso` con `DisableParallelization = true`, usa `VariableDeEntornoTemporal` que
restaura el valor previo, y cada test construye su propio host. Sin dependencia de orden, sin estado
compartido, sin Ids ni timestamps fijos.

### Veredicto de la ronda 2

```
┌──────────────────────────────────────────────────────────────────┐
│  /daw-verify-module FEAT-001a — ronda 2 — PASSED                  │
│                                                                   │
│  F-VER-01  ✅ 7/7 AC (heredado; área no tocada, suite verde)      │
│  F-VER-02  ✅ 6/6 bloques (heredado)                              │
│  F-VER-03  ✅ 94,60% L · 90,77% R · 96,30% F — los 4 exactos      │
│  F-VER-04  ✅ CERRADO — las 6 mutaciones matan tests              │
│  F-VER-05  ✅ 0 advertencias, 0 errores                           │
│  F-VER-06  ✅ 49/49 tests de la spec (heredado)                   │
│                                                                   │
│  FAILs: 0  |  WARNs: 9 (6 heredados + 3 nuevos)                   │
│  Suite: 198/198, 0 salteados · restauración verificada por sha256 │
└──────────────────────────────────────────────────────────────────┘
```

---

## Pendientes que este ticket deja registrados

1. **Evidencia TDD:** B5 perdida e irreconstruible (decisión del usuario: dejar constancia y avanzar);
   B6 existió y fue revisada, pero no quedó en el cuerpo del commit.
2. **NFR-03** — verificación manual sobre las 2 últimas versiones de Chrome, Edge y Firefox: declarada
   en la spec, **no realizada**. Tarea de RELEASE.
3. **NFR-01** — medición de carga con 50 concurrentes: diferida a un ticket de performance propio.
4. **ADR recomendado** — `ASPNETCORE_ENVIRONMENT` gobierna seis controles de seguridad independientes;
   la capa de compilación agregada en el cierre de CODE lo corrige solo para la identidad.
5. **`UseStaticFiles()`** sin test que lo fije.
6. Los 9 WARN de este informe.
