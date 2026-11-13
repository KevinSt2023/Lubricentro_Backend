# LubriTech — Hoja de ruta

> Abierta el 2026-11-10. Orden de construcción y definición de "listo".
> Regla que manda sobre todo lo demás: **un módulo no avanza hasta que su script esté
> aplicado, su QA pase en verde y esté commiteado.**

El encargo recibido es la **extensión** multidispositivo. La base que extiende —
clientes, vehículos, órdenes de trabajo, inventario, multisede — todavía no existe,
así que el orden no es opcional: **primero la plataforma, después las apps que la
consumen.** Un técnico no puede actualizar una orden desde el celular si la orden no
existe en la base.

---

## Fases

### Fase 0 · Núcleo — **CERRADA el 2026-11-10**
Base `LubriTech` creada, `scripts/00_nucleo.sql` y `01_seed.sql` aplicados, backend
.NET 10 de 4 capas corriendo en 9301, Serilog, FluentValidation, filtro de empresa que
falla cerrado, los **dos** flujos de login (personal y cliente) con JWT de audiencia
separada y refresh rotativo con detección de reuso, bitácora inmutable, hubs de
SignalR registrados.

**Se cerró con:** 13 candados de base + 62 comprobaciones contra la API, todo en
verde, cero 500, cuadre en cero. El detalle está en `02-FASE-0-NUCLEO.md`.

### Fase 1 · Consola SuperAdmin — **backend CERRADO el 2026-11-10**
Empresas, planes como datos (cupo por rol, funciones por plan), sedes, suscripción y
corte por vencimiento, salud y cumplimiento por empresa.

**Se cerró con:** 16 candados de base nuevos + 135 comprobaciones contra la API, más
el barrido de la Fase 0 (13 + 62) otra vez en verde, cero 500, cuadres en cero. El
detalle está en `03-FASE-1-CONSOLA.md`.

**Lo que falta de esta fase:** las pantallas. La consola hoy es API; la interfaz
Angular del SuperAdmin se construye junto con el esqueleto del ERP web.

### Fase 2 · Maestros del taller — **backend CERRADO el 2026-11-11**
Personal · clientes (particular y empresa con flota) · vehículos (placa, marca,
modelo, año, motor, kilometraje) · catálogo de servicios · almacén, repuestos e
insumos con kardex.

**Se cerró con:** 17 candados de base nuevos + 74 comprobaciones contra la API, más
el barrido de las fases 0 y 1 (29 candados + 197 comprobaciones) otra vez en verde,
cero 500, cuadres en cero. El motor del kardex (ingreso, salida, ajuste, costo
promedio ponderado, stock por sede) está probado. El detalle está en
`04-FASE-2-MAESTROS.md`.

**Lo que falta de esta fase:** las pantallas Angular. La salida de stock por consumo
real se escribe desde la orden de trabajo (Fase 3); el motor ya existe y está probado.

### Fase 3 · Órdenes de trabajo — **backend CERRADO el 2026-11-11**
El corazón. Los 11 estados como catálogo, `orden_historial` inmutable, asignación a
técnico y sede, checklist de inspección, observaciones, evidencias fotográficas con
validación por bytes, repuestos consumidos moviendo inventario y kardex en la misma
transacción.

**Se cerró con:** 13 candados de base nuevos + 57 comprobaciones contra la API, más el
barrido de las fases 0, 1 y 2 (42 candados + 271 comprobaciones) otra vez en verde,
cero 500, cuadres en cero. El flujo de los 11 estados, el consumo que mueve kardex y la
evidencia validada por bytes están probados de punta a punta. Detalle en
`05-FASE-3-ORDENES.md`.

**Lo que falta de esta fase:** las pantallas Angular. La cotización versionada y la
aprobación digital formal son la Fase 4.

### Fase 4 · Cotizaciones y aprobaciones digitales — **backend CERRADO el 2026-11-12**
Hallazgo del técnico → cotización versionada del asesor → decisión del cliente
guardada inmutable. Sin fila de aprobación, el renglón adicional no pasa a ejecución.

**Se cerró con:** 13 candados de base nuevos + 45 comprobaciones contra la API, más el
barrido de las fases 0, 1, 2 y 3 (59 candados + 328 comprobaciones) otra vez en verde,
cero 500, cuadres en cero. La versión correlativa, el borrador que se congela al enviar,
la decisión inmutable del cliente como única puerta de aprobación y el renglón que no pasa
a ejecución sin aprobación están probados de punta a punta. Detalle en
`06-FASE-4-COTIZACIONES.md`.

**Lo que falta de esta fase:** las pantallas (asesor en el ERP web; cliente en la app
Flutter de la Fase 10).

### Fase 5 · Agenda y citas — **backend CERRADO el 2026-11-13**
Agenda por sede y por técnico, cita registrada como primer estado de la orden,
próximos mantenimientos por kilometraje y por fecha.

Una cita **es** una orden en `CITA_REGISTRADA` con `fecha_cita` (no hay tabla
aparte). `scripts/07_agenda.sql` agrega el candado (una cita exige fecha) y el
índice del calendario. `AgendaController` (`/api/agenda`) se apoya en
`IOrdenServicio` para no duplicar historial ni bitácora: listar (AGENDA.VER),
agendar / reprogramar / recibir / cancelar (AGENDA.AGENDAR). Las fechas se
guardan en UTC (timestamptz solo acepta UTC).

**Se cerró con:** 1 candado de base + 25 comprobaciones contra la API, más el
barrido de las fases 0–4 (núcleo 62, consola 135, maestros 74, órdenes 67,
cotizaciones 45) otra vez en verde, cero 500.

**Iteración 2 (próximos mantenimientos) — CERRADA el 2026-11-13.**
`scripts/08_mantenimiento.sql` agrega `vehiculo.proximo_mant_fecha` y
`proximo_mant_km` (con CHECK e índice parcial). Se fijan a mano en la ficha del
vehículo o se proponen solos al entregar una orden (+5000 km / +6 meses).
`GET /api/agenda/proximos-mantenimientos` lista los vencidos y por vencer (por
fecha o por km) con teléfono para llamar al cliente. QA: 32 comprobaciones
en `qa_agenda.py` + el auto-fijado en `qa_ordenes.py`, todo en verde.

**Pantallas (CERRADAS el 2026-11-13):** calendario semanal por día (agendar,
reprogramar, recibir, cancelar) y el panel de próximos mantenimientos con
"Agendar" de un clic. Falta solo la vista del cliente en la app Flutter (Fase 10).

### Fase 6 · Caja y cobros
Cobro de la orden, métodos de pago, comprobante, cierre de turno. Esta es la frontera
del modo sin conexión: la plata no se confirma sin servidor.

### Fase 7 · Tiempo real y push
Hubs por público con proyección filtrada para el cliente. `dispositivo`,
`notificacion`, `notificacion_envio`, preferencias por usuario. FCM y APNs detrás de
una sola interfaz de envío, con historial y estado de entrega.

### Fase 8 · ERP web completo
Tablero ejecutivo con ECharts, finanzas, inventario, multisede, reportes,
configuración, personal, clientes, vehículos, órdenes, agenda, servicios. Responsive
hasta tablet **sin perder funciones**: un administrador trabaja desde tablet.

### Fase 9 · App LubriTech Operaciones (Flutter)
Login seguro con secure storage y biometría opcional, perfil, sede asignada, tablero
personal, órdenes asignadas, agenda, vehículo, historial, checklist, observaciones,
fotos desde la cámara, repuestos usados, solicitud de materiales, cambio de estado,
inicio y fin de trabajo, notificaciones internas. Táctil de verdad: botones grandes,
formularios cortos.

### Fase 10 · App LubriTech Clientes (Flutter)
Registro, login, recuperación de contraseña, perfil, vehículos propios, vinculación
con verificación del propietario, historial automotriz, citas, solicitud de servicio,
seguimiento de la orden, cotizaciones, aprobar o rechazar, evidencias, pagos,
comprobantes, notificaciones, próximos mantenimientos, contacto con el taller.

### Fase 11 · Visor 3D
Three.js en Angular y visor nativo en Flutter sobre el mismo GLB: rotación táctil,
zoom por gestos, selección y resaltado de componentes, cambio de cámara, vista
inferior, motor, despiece, consulta de piezas. LOD, carga progresiva, liberación de
recursos, calidad adaptable y **2D de respaldo**. La escena nunca bloquea la
navegación.

### Fase 12 · Sincronización y conectividad
Detección de conectividad, cola local, estados de sincronización, reintentos,
idempotencia por clave + huella, versión de registro y resolución de conflictos, con
trazabilidad de lo sincronizado.

### Fase 13 · Entrega y publicación
APK/AAB firmados, build iOS, identificadores, iconos, splash, configuración de push,
documentación de instalación, preparación de Google Play y App Store. Configurable por
contrato y por infraestructura de cada empresa cliente.

---

## Los 11 criterios de aceptación

| # | Criterio | Fases | Cómo se prueba |
|---|---|---|---|
| 1 | El técnico recibe y actualiza una orden desde su teléfono | 3, 7, 9 | QA de extremo a extremo: asignar, abrir en la app, cambiar estado, verificar `orden_historial` |
| 2 | El gerente ve el avance en el ERP | 7, 8 | El evento de SignalR llega al grupo `empresa:{id}` y la pantalla se mueve sin recargar |
| 3 | El cliente recibe la actualización autorizada | 7, 10 | Mismo cambio de estado: llega si `visible_cliente`, no llega si es interno |
| 4 | El cliente consulta el historial de su vehículo | 2, 10 | Historial del vehículo propio; vehículo ajeno devuelve 404, no 403 con datos |
| 5 | El cliente aprueba una cotización | 4, 10 | Aprobación guardada con fecha, usuario, versión y decisión; el renglón pasa a ejecución |
| 6 | Los cambios quedan en auditoría | 0, 3 | Intento de `UPDATE`/`DELETE` sobre historial y bitácora falla por trigger |
| 7 | Las apps funcionan en teléfono y tablet | 9, 10 | Recorrido en dos tamaños de pantalla por app |
| 8 | Los permisos se respetan en todos los dispositivos | 0, 1 | La matriz completa rol × endpoint, por REST, no por lo que la UI muestre |
| 9 | Las notificaciones llegan con la app cerrada | 7 | Envío real por FCM con la app terminada, y registro en `notificacion_envio` |
| 10 | La app recupera su estado tras perder conexión | 12 | Cortar red, operar, reconectar: nada duplicado, nada perdido, plata sin confirmar |
| 11 | Nadie ve datos de otra empresa ni de otro vehículo | 0, 2 | Barrido de aislamiento: cada endpoint con token de la empresa equivocada |

Ninguno se marca cumplido sin el arnés completo en verde: **cero fallas, cero 500,
ningún 400 que frene un flujo legítimo**, y cuadre de integridad en cero.

---

## Lo que hace falta de tu lado

No frenan Fase 0 a 8, pero sí las de celular, así que conviene resolverlos antes de
la Fase 9:

1. **Flutter SDK + Android Studio** en esta máquina (no están instalados). Con eso
   compilo y pruebo el APK de Android aquí mismo.
2. **Una Mac o un runner macOS en la nube** (GitHub Actions macOS, Codemagic) para
   compilar iOS. En Windows no hay forma: Xcode no existe para Windows. El código
   Flutter se escribe igual aquí y queda listo.
3. **Cuenta de Apple Developer** (99 USD/año) y **cuenta de Google Play** (25 USD una
   vez) para publicar.
4. **Proyecto de Firebase** para FCM, y el certificado APNs dentro de ese proyecto
   para iOS.
5. **Modelos 3D GLB** de los vehículos o del motor. Si no hay, arranco el visor con un
   modelo genérico de referencia y se reemplaza después sin tocar el visor.
