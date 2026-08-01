# PRD-001: Sistema de Gestión de Vacaciones — Permite gestionar las vacaciones del personal.

> Versión 2 — endurecida contra el template y el checklist de calidad del curso. Reemplaza a `PRD.md`.

## Contexto y Problema
Actualmente las solicitudes de vacaciones se gestionan de manera manual, lo que genera demoras, falta de control sobre los días disponibles y riesgo de superposición de períodos. No existe un sistema centralizado para registrar licencias, el control del tope anual de días es difícil y los procesos de aprobación son poco claros o informales.

**Personas:**
- **Empleado**: solicita vacaciones, consulta su saldo de días del año en curso y el estado e historial de sus solicitudes, sin fricción y sin depender de RRHH.
- **Manager**: aprueba o rechaza las solicitudes de los empleados a su cargo, y necesita ver el contexto (fechas y saldo) para decidir.
- **Designado**: persona en la que el manager delega la aprobación/rechazo cuando no está disponible; sobre ese equipo tiene las mismas capacidades que el manager.

## Objetivos
- Transparencia en el uso de días de vacaciones.
- Reducción de errores administrativos.
- Flujo claro de aprobación y trazabilidad.
- Mejora en la planificación de recursos humanos.

## Requerimientos Funcionales
- RF-01: El sistema debe autenticar a los usuarios pertenecientes a la empresa mediante OAuth.
- RF-02: El sistema debe permitir registrar solicitudes con su período (fecha de inicio y fecha de fin) y la cantidad de días corridos.
- RF-03: El sistema debe validar automáticamente el tope anual de 14 días, contando los días tomados, aprobados y pendientes del año en curso.
- RF-04: El sistema debe validar que una nueva solicitud no se superponga con otra solicitud del mismo empleado en estado Pendiente o Aprobada.
- RF-05: El sistema debe permitir que el manager (o el designado por él) apruebe solicitudes pendientes.
- RF-06: El sistema debe permitir que el manager (o el designado por él) rechace solicitudes pendientes, indicando obligatoriamente un motivo de rechazo.
- RF-07.1: El sistema debe notificar al manager (o su designado) cuando una solicitud se crea y queda en estado "Pendiente".
- RF-07.2: El sistema debe notificar al empleado cuando su solicitud pasa a estado "Aprobada".
- RF-07.3: El sistema debe notificar al empleado cuando su solicitud pasa a estado "Rechazada", incluyendo el motivo del rechazo.
- RF-07.4: El sistema debe entregar toda notificación de forma obligatoria por dos canales: in-app y correo electrónico.
- RF-08: El sistema debe registrar el historial de solicitudes por usuario.
- RF-09: El sistema debe restringir el acceso a las solicitudes, el historial y el saldo de días de un empleado únicamente a ese empleado y al manager (o designado) autorizado sobre él.
- RF-10: El sistema debe calcular el saldo de días de vacaciones disponibles del empleado en el año en curso, como 14 menos los días tomados, aprobados y pendientes.
- RF-11: El sistema debe mostrar al empleado su saldo de días de vacaciones disponibles del año en curso.
- RF-12: El sistema debe reiniciar el saldo de cada empleado a 14 días al comenzar cada año calendario (1 de enero), sin prorrateo por fecha de ingreso.

## Requerimientos No Funcionales
- RNF-01: Seguridad de autenticación y autorización, verificable con:
    - Todo el tráfico servido sobre HTTPS (TLS 1.2 o superior).
    - Expiración de la sesión tras 30 minutos de inactividad, exigiendo nueva autenticación.
    - HTTP 401 en el 100% de las peticiones sin sesión válida a endpoints protegidos.
    - HTTP 403 ante cualquier intento de acceder a datos de otro usuario sobre el que el autenticado no tiene un rol autorizado (por ejemplo, un empleado que intenta ver datos de otro empleado del que no es manager ni designado).
- RNF-02: Usabilidad medible: un usuario sin capacitación previa debe poder completar y enviar una solicitud de vacaciones en ≤ 3 minutos y ≤ 5 clics, con una tasa de éxito ≥ 90% en pruebas de usabilidad con al menos 5 usuarios.
- RNF-03: Compatibilidad garantizada con las 2 últimas versiones estables de Chrome, Edge y Firefox en escritorio, sin errores de renderizado ni de funcionalidad.
- RNF-04: Rendimiento: las operaciones interactivas principales (autenticación, listado de solicitudes, y creación, aprobación o rechazo de una solicitud) deben responder en menos de 3 segundos en el percentil 95 (p95), bajo una carga concurrente de al menos 50 usuarios.

## Criterios de Aceptación
- AC-01.1 (RF-01):
    Dado: un usuario cuyo correo corporativo está registrado en la empresa,
    Cuando: se autentica mediante OAuth,
    Entonces: el sistema le concede el acceso.

- AC-01.2 (RF-01):
    Dado: un usuario cuyo correo no está registrado en la empresa,
    Cuando: intenta autenticarse mediante OAuth,
    Entonces: el sistema le deniega el acceso.

- AC-02 (RF-01):
    Dado: un usuario no autenticado,
    Cuando: intenta acceder a cualquier sección protegida del sistema,
    Entonces: es redirigido automáticamente a la pantalla de login.

- AC-03 (RF-02):
    Dado: un empleado creando una solicitud,
    Cuando: ingresa una fecha de inicio y una fecha de fin en el calendario interactivo,
    Entonces: el sistema calcula y muestra automáticamente la cantidad de días corridos antes de permitir el envío.

- AC-04 (RF-02):
    Dado: un empleado creando una solicitud,
    Cuando: ingresa un rango de fechas inválido (una fecha de inicio anterior a la fecha actual, o una fecha de fin anterior a la fecha de inicio),
    Entonces: el sistema impide el envío y muestra el mensaje "La fecha de inicio no puede ser anterior a hoy" o "La fecha de fin no puede ser anterior a la fecha de inicio", según el caso.

- AC-05 (RF-03):
    Dado: un empleado con una determinada cantidad de días ya tomados, aprobados o pendientes en el año en curso,
    Cuando: intenta enviar una solicitud cuyos días, sumados a los ya utilizados o reservados, superan el tope anual de 14 días,
    Entonces: el sistema bloquea el botón "Enviar" y muestra el mensaje "No dispones de días suficientes. Tu saldo actual es de X días".

- AC-06 (RF-04):
    Dado: un empleado con una solicitud propia ya en estado "Pendiente" o "Aprobada",
    Cuando: intenta crear una nueva solicitud cuyas fechas coinciden total o parcialmente con la existente,
    Entonces: el sistema impide la creación y muestra el mensaje "Ya tenés una solicitud que se superpone con estas fechas".

- AC-07 (RF-07.1, RF-07.4):
    Dado: un empleado que acaba de enviar una solicitud,
    Cuando: la solicitud se crea y queda en estado "Pendiente",
    Entonces: el manager (o su designado) recibe una notificación con los detalles de la solicitud, tanto in-app como por correo electrónico (ambos obligatorios).

- AC-08.1 (RF-05):
    Dado: una solicitud en estado "Pendiente" y un manager (o designado) autenticado,
    Cuando: el manager la aprueba desde el formulario de autorizaciones,
    Entonces: el sistema cambia el estado de la solicitud a "Aprobada".

- AC-08.2 (RF-07.2, RF-07.4):
    Dado: una solicitud que acaba de pasar a estado "Aprobada",
    Cuando: se registra el cambio de estado,
    Entonces: el empleado recibe la notificación de aprobación entregada por ambos canales (in-app y correo electrónico).

- AC-08.3 (RF-08):
    Dado: una solicitud que acaba de pasar a estado "Aprobada",
    Cuando: se registra el cambio de estado,
    Entonces: el sistema registra la acción en el historial con la fecha y el manager (o designado) que autorizó.

- AC-09.1 (RF-06):
    Dado: una solicitud en estado "Pendiente" y un manager (o designado) autenticado,
    Cuando: el manager la rechaza desde el formulario de autorizaciones indicando un motivo,
    Entonces: el sistema cambia el estado de la solicitud a "Rechazada".

- AC-09.1a (RF-06):
    Dado: una solicitud en estado "Pendiente" y un manager (o designado) autenticado,
    Cuando: el manager intenta rechazarla sin indicar un motivo,
    Entonces: el sistema impide el rechazo y solicita que ingrese el motivo.

- AC-09.2 (RF-07.3, RF-07.4):
    Dado: una solicitud que acaba de pasar a estado "Rechazada",
    Cuando: se registra el cambio de estado,
    Entonces: el empleado recibe la notificación de rechazo, incluyendo el motivo, entregada por ambos canales (in-app y correo electrónico).

- AC-09.3 (RF-08):
    Dado: una solicitud que acaba de pasar a estado "Rechazada",
    Cuando: se registra el cambio de estado,
    Entonces: el sistema registra la acción en el historial con la fecha, el manager (o designado) que rechazó y el motivo del rechazo.

- AC-10.1 (RF-08):
    Dado: un empleado autenticado con solicitudes históricas,
    Cuando: entra a la página para visualizar sus solicitudes,
    Entonces: el sistema muestra todas sus solicitudes ordenadas cronológicamente de forma descendente por fecha de creación (la más reciente primero).

- AC-10.2 (RF-08):
    Dado: un empleado autenticado que visualiza sus solicitudes,
    Cuando: se muestra cada solicitud del listado,
    Entonces: cada solicitud muestra su estado actual (Pendiente, Aprobada o Rechazada).

- AC-10.3 (RF-11):
    Dado: un empleado autenticado que visualiza sus solicitudes,
    Cuando: se muestra el listado,
    Entonces: el sistema muestra los días de vacaciones utilizados o reservados (tomados, aprobados y pendientes) y los días disponibles del año en curso.

- AC-10.4 (RF-08):
    Dado: un empleado autenticado que visualiza una solicitud ya resuelta (Aprobada o Rechazada),
    Cuando: se muestra esa solicitud,
    Entonces: el sistema muestra los datos del manager (o designado) que la autorizó o rechazó.

- AC-11 (RF-09):
    Dado: un empleado A autenticado y una solicitud, historial o saldo que pertenece a un empleado B, sin que A sea su manager ni designado,
    Cuando: A intenta acceder a esos datos (por ejemplo, mediante el identificador o la URL directa del recurso),
    Entonces: el sistema deniega el acceso (HTTP 403) y no muestra ningún dato de B.

- AC-12 (RF-12):
    Dado: un empleado con días consumidos o reservados en un año calendario,
    Cuando: comienza un nuevo año calendario (1 de enero),
    Entonces: su saldo disponible vuelve a ser de 14 días.

## Fuera de Alcance
- No hay posibilidad de ampliar el tope anual ni reglas según convenios.
- No se consideran otro tipo de solicitudes de licencias (por ejemplo: Enfermedad, Fallecimiento, Matrimonio, Examen, etc.).
- No se consideran dispositivos móviles.
- La creación y administración de usuarios y la asignación de quién es manager o designado de cada empleado NO forman parte del sistema: se administran externamente (usuarios y relaciones pre-cargados o gestionados por el proveedor de identidad).
- No se puede cancelar ni editar una solicitud una vez enviada.
- No hay prorrateo del cupo anual según la fecha de ingreso.
- No hay arrastre (carryover) de días no utilizados al año siguiente: el saldo se reinicia a 14 cada 1 de enero.

## Riesgos y Dependencias

**Riesgos (riesgo → mitigación):**
- Resistencia inicial al cambio por parte de los empleados → mitigación: interfaz simple y de bajo esfuerzo (respaldada por RNF-02) y una capacitación breve de onboarding.
- Ajustes legales según convenios colectivos → mitigación: centralizar el tope y las reglas de cálculo en un único punto para poder adaptarlos si cambia la normativa (su ampliación queda fuera del alcance del MVP).
- Manejo de datos personales sensibles (identidades y períodos de licencia) → mitigación: control de acceso por rol (RF-09 / AC-11), HTTPS/TLS y expiración de sesión (RNF-01).
- Sobreasignación de saldo por solicitudes pendientes → mitigación: las solicitudes Pendientes reservan saldo y cuentan contra el tope (RF-03 / AC-05).

**Dependencias:**
- Proveedor de identidad OAuth (a confirmar: Google Workspace o Microsoft Entra ID) — requerido por RF-01.
- Servicio de envío de correo electrónico (a confirmar) — requerido por RF-07.4.
- SQL Server 2022, según el stack definido para el proyecto.
- Navegadores soportados: 2 últimas versiones estables de Chrome, Edge y Firefox en escritorio (RNF-03).
