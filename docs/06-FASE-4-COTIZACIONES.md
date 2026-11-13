# Fase 4 · Cotizaciones y aprobaciones digitales (backend)

Cerrada el 2026-11-12. El flujo formal del trabajo adicional: lo que el técnico
encuentra se cotiza, el cliente lo aprueba o lo rechaza, y esa decisión queda
grabada para siempre. **Sin esa fila de decisión, el renglón adicional no pasa a
ejecución.**

Alcance de lo cerrado: **API y base de datos**. Las pantallas Angular (ERP) y la
app del cliente (Flutter) consumen esto después.

## El flujo

```
Hallazgo del técnico  →  Cotización versionada del asesor  →  Decisión del cliente
 (COTIZACION.HALLAZGO)      (COTIZACION.CREAR / ENVIAR)          (portal, inmutable)
                                                                      │
                                       Aprobada ──► los servicios pasan a la orden
                                       Rechazada ─► no pasa nada
```

## Quién mueve cada cosa (permisos — ya existían en `01_seed.sql`)

| Acción | Permiso | Rol típico |
|--------|---------|-----------|
| Registrar / quitar hallazgo | `COTIZACION.HALLAZGO` | Técnico, Supervisor |
| Ver cotizaciones | `COTIZACION.VER` | Asesor, Supervisor, Admin |
| Armar / editar / aplicar cotización | `COTIZACION.CREAR` | Asesor |
| Enviar cotización al cliente | `COTIZACION.ENVIAR` | Asesor |
| Decidir (aprobar / rechazar) | *(portal del cliente)* | el dueño del vehículo |

El lado del taller vive en `CotizacionesController`; el del cliente en
`PortalCotizacionesController`, marcado `[SoloCliente]`: un token del ERP no entra
al portal y uno de cliente no entra al ERP (candado del criterio 11, por audiencia).

## Reglas que quedaron amarradas EN LA BASE (`scripts/06_cotizaciones.sql`)

1. **La versión de la cotización es correlativa por orden** — la asigna la base con
   un lock por orden (igual que el número de orden). Dos borradores simultáneos no chocan.
2. **Una cotización solo se edita mientras es BORRADOR** — una vez ENVIADA queda
   congelada; cambiarla es armar una versión nueva (trigger `fn_cotizacion_renglon_bloqueo`).
3. **Solo la fila `cotizacion_decision` aprueba o rechaza** — un trigger impide forzar
   el estado a APROBADA/RECHAZADA a mano; la decisión del cliente es la única puerta, y
   su `AFTER INSERT` pone el estado. Una cotización ya decidida no cambia más.
4. **La decisión del cliente es INMUTABLE y única por cotización** (trigger
   `fn_tabla_inmutable` + `UNIQUE`). Guarda decisión, cliente, comentario, canal, IP y
   fecha. Es la prueba del criterio 5.
5. **Una sola cotización ENVIADA (pendiente) por orden a la vez** — índice único parcial.
   Al enviar una versión nueva, el servicio marca la anterior como REEMPLAZADA.
6. **El renglón cotizado no pasa a ejecución sin aprobación** — `orden_servicio` y
   `orden_consumo` llevan un `cotizacion_renglon_id` opcional; si viene, la base exige
   que la cotización esté APROBADA (trigger `fn_renglon_necesita_aprobacion`). Es la
   traducción literal de la regla de oro de la fase.
7. **El subtotal del renglón lo calcula la base** — columna generada
   (`round(cantidad * precio_unitario, 2)`): no se confía en el total que mande el cliente.

## El ciclo, en endpoints

```
POST   api/ordenes/{id}/hallazgos            registra un hallazgo (técnico)
GET    api/ordenes/{id}/hallazgos            los ve el asesor
POST   api/ordenes/{id}/cotizaciones         arma el borrador con renglones (asesor)
PUT    api/cotizaciones/{id}                 edita el borrador
POST   api/cotizaciones/{id}/enviar          la envía (reemplaza la versión anterior)
GET    api/portal/cotizaciones               el cliente ve SOLO las suyas
POST   api/portal/cotizaciones/{id}/decision el cliente aprueba o rechaza (inmutable)
POST   api/cotizaciones/{id}/aplicar         los servicios aprobados entran a la orden
```

Aplicar pasa los renglones de **servicio** aprobados a `orden_servicio` (vinculados por
`cotizacion_renglon_id`) y es idempotente. Los repuestos quedan autorizados y se consumen
por el flujo de kardex existente durante la ejecución.

## Cómo se cerró (QA)

- **Candados de base** `qa/04_candados_cotizaciones.sql`: 13 comprobaciones, **0 fallas**,
  cuadre de integridad en cero (versión correlativa, renglón congelado, no se fuerza el
  estado, decisión inmutable, una sola enviada, renglón sin aprobación rechazado y con
  aprobación admitido).
- **Arnés contra la API viva** `qa/qa_cotizaciones.py`: 45 comprobaciones, **0 fallas**
  (hallazgo, armado, edición de borrador, envío, versionado, portal del cliente,
  aislamiento, decisión, aplicar a la orden, rechazo, bitácora, cuadre).
- **Re-barrido de las fases 0-3** otra vez en verde: candados núcleo/consola/maestros/
  órdenes (0 fallas) + `qa_nucleo` (62), `qa_consola` (135), `qa_maestros` (74),
  `qa_ordenes` (57), todo **0 fallas, cero 500, cuadres en cero**.

Total acumulado: **72 candados de base + 373 comprobaciones contra la API**, todo verde.

## Lo que falta de esta fase

Las pantallas: la del asesor (armar y enviar la cotización, aplicarla) en el ERP web, y
la del cliente (ver y aprobar) en la app Flutter de clientes (Fase 10). El motor ya existe
y está probado de punta a punta.
