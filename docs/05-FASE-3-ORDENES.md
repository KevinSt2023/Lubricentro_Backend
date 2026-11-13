# Fase 3 · Órdenes de trabajo (backend)

Cerrada el 2026-11-11. El corazón del sistema: toda la vida de un trabajo, de la
cita a la entrega, con su historial, su inspección, sus repuestos y sus fotos.

Alcance de lo cerrado: **API y base de datos**. Las pantallas Angular van con el ERP web.

## Los 11 estados (confirmados con el cliente)

```
1 Cita registrada → 2 Recibido → 3 En diagnóstico → 4 Cotización enviada →
5 Aprobada → 6 En ejecución → 7 En control de calidad → 8 Lista →
9 Facturada → 10 Entregada        ·        11 Anulada (fuera de línea)
```

Regla del flujo, **amarrada en la base** (trigger `fn_orden_transicion`):
- La orden **avanza de a un estado**: no se salta ni se retrocede.
- **Anulada** se puede activar desde cualquier estado no final (exige motivo).
- Una orden **Entregada o Anulada es final**: no cambia más.

## Quién mueve cada cosa (permisos)

| Acción | Permiso | Rol típico |
|--------|---------|-----------|
| Ver todas las órdenes | `ORDENES.VER` | Admin, Gerente, Asesor, Supervisor, Cajero |
| Ver solo las asignadas | `ORDENES.VER_ASIGNADAS` | Técnico |
| Abrir orden | `ORDENES.CREAR` | Asesor, Admin |
| Mover estado / editar / servicios | `ORDENES.ESTADO` | según el paso |
| Asignar técnico | `ORDENES.ASIGNAR` | Supervisor, Admin |
| Llenar checklist | `ORDENES.CHECKLIST` | Técnico |
| Subir evidencias | `ORDENES.EVIDENCIA` | Técnico |
| Consumir repuesto | `ALMACEN.CONSUMIR` | Técnico |
| Anular | `ORDENES.ANULAR` | Admin, Gerente |
| Ver costo y margen | `ORDENES.COSTOS` | Admin, Gerente |

El Admin y el Gerente, por su matriz, pueden mover cualquier estado.

## Reglas que quedaron amarradas EN LA BASE (`scripts/05_ordenes.sql`)

1. **Número de orden correlativo por empresa** — lo asigna la base con un lock por
   empresa, así dos altas simultáneas nunca chocan.
2. **El historial (`orden_historial`) es inmutable** — cada cambio de estado deja su
   renglón: de qué estado a cuál, quién y cuándo. Es la prueba del criterio de auditoría.
3. **El consumo (`orden_consumo`) es inmutable** — ya movió el stock. Deshacerlo es un
   ingreso o un ajuste, no un borrado.
4. **Consumir un repuesto escribe su SALIDA de kardex en la misma transacción** (reusa
   el motor de la Fase 2). Sin stock, la base lo rechaza y no queda nada a medias.

Reglas de la capa de servicio:
- **Consumo solo con la orden En ejecución** (la regla que cerramos); fuera de ese
  estado se rechaza.
- **El checklist nace de una plantilla editable por empresa** (se sembró una base de
  10 puntos de lubricentro); el nombre del punto se copia en la respuesta, así editar
  la plantilla no reescribe inspecciones ya hechas.
- **Las evidencias se validan por bytes**: no se confía en la extensión ni en el mime
  declarado; una foto tiene que empezar con la firma real de un JPG o un PNG. El
  archivo vive en disco, la metadata (mime, tamaño, hash) en la base.
- **Costos y margen solo para quien tiene `ORDENES.COSTOS`**: al asesor nunca se le
  asoma el costo de adquisición.

## QA

```
psql … -f qa/03_candados_ordenes.sql    13 candados   (Fase 3)
python qa/qa_ordenes.py                 57 comprobaciones (Fase 3)
```

Barrido final de las cuatro fases: **55 candados + 328 comprobaciones, todo en
verde**, cero 500 en el log, todos los cuadres en cero. `qa_ordenes.py` recorre el
ciclo entero: alta, avance de a uno, rechazo de saltos, checklist, consumo que mueve
kardex, servicios y total, costos por permiso, evidencia validada por bytes y
descargada, asignación y vista del técnico, cierre a Entregada y anulación.

## Defecto real que encontró el arnés (y se corrigió)

**Un candado de la base que saltaba a través de EF daba 500 en vez de 409.** Cuando
el error ocurre dentro de `SaveChanges` (como la salida de kardex sin stock), la
`PostgresException` viene **envuelta en una `DbUpdateException`**, y el middleware
solo miraba la excepción directa. Se corrigió `ManejoDeErroresMiddleware` para
recorrer la cadena de `InnerException` y encontrar la `PostgresException`. El arreglo
**endurece todos los candados**, no solo el consumo: cualquier regla de la base que
salte vía EF ahora sale como el 409 que corresponde, con su mensaje. (Hasta aquí,
los duplicados se atrapaban antes con un pre-chequeo en el servicio, así que este
camino nunca se había ejercitado de verdad.)

## Pendiente dentro de la Fase 3

- Pantallas Angular del tablero de órdenes y del detalle.
- La **cotización versionada y la aprobación digital formal** son la Fase 4: aquí el
  estado "Aprobada" se registra con su observación en el historial; la fila de
  aprobación inmutable del cliente llega después.
- Al **anular**, los repuestos ya consumidos no vuelven solos al stock (pueden haberse
  usado físicamente): devolverlos es un ingreso explícito, decisión de quien anula.
