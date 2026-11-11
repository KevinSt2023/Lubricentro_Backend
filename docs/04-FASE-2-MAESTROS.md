# Fase 2 · Maestros del taller (backend)

Cerrada el 2026-11-11. Es todo lo que un taller necesita tener cargado **antes**
de abrir una orden de trabajo: su gente, sus clientes, los vehiculos de esos
clientes, el catalogo de servicios y el almacen con su stock.

Alcance de lo cerrado: **API y base de datos**. Las pantallas Angular se
construyen con el ERP web.

## Qué se puede hacer ya

| Maestro | Endpoints | Permisos |
|---|---|---|
| **Personal** (sobre `usuario`) | `GET/POST/PUT /api/personal`, `PUT …/{id}/estado`, `POST …/{id}/clave` | PERSONAL.VER / CREAR / EDITAR |
| **Clientes** | `GET/POST/PUT /api/clientes`, `PUT …/{id}/estado`, `POST …/{id}/portal` | CLIENTES.VER / CREAR / EDITAR / PORTAL |
| **Vehículos** | `GET/POST/PUT /api/vehiculos`, `PUT …/{id}/estado`, `PUT …/{id}/kilometraje` | VEHICULOS.VER / CREAR / EDITAR |
| **Servicios** | `GET/POST/PUT /api/servicios`, `PUT …/{id}/estado` | SERVICIOS.VER / EDITAR |
| **Almacén** | `GET/POST/PUT /api/almacen/productos`, `…/{id}/estado`, `…/barras/{c}`, `POST …/{id}/ingresos`, `POST …/{id}/ajustes`, `GET …/{id}/kardex` | ALMACEN.VER / INGRESAR / AJUSTAR |

En todos, la empresa sale **siempre** del token (`sesion.EmpresaId`): nunca de la
ruta ni del cuerpo. Un id de otra empresa responde 404, no 403: para ese Admin no
existe.

## Reglas que quedaron amarradas EN LA BASE (`scripts/04_maestros.sql`)

1. **El stock se mueve solo por el kardex.** Insertar una fila en `kardex` ES
   mover el stock: el trigger `fn_kardex_aplica` calcula el saldo, valida que una
   salida no deje negativo (si el producto controla stock), recalcula el costo
   promedio ponderado en los ingresos y actualiza `producto_stock`. No hay otro
   camino. (memoria: *"TODO movimiento afecta kardex+inventario"*)
2. **`producto_stock` no se edita a mano.** Un trigger lo bloquea; la única mano
   que lo escribe es la del kardex, que pasa con una marca de sesión que el
   candado reconoce.
3. **El kardex es inmutable.** Ni UPDATE ni DELETE: lo que se movió, se movió.
4. **El kilometraje de un vehículo no baja.** Lo blinda un trigger; el servicio lo
   comprueba antes para dar un mensaje claro.
5. **La placa es única por empresa** (insensible a mayúsculas) y **el código de
   producto / servicio también**. La misma placa en otra empresa sí entra: son
   talleres distintos.
6. **El código de barras, si lo tiene, es único por empresa:** el lector debe
   llegar a un solo producto.

## Reglas de la capa de servicio

- **Personal:** el cupo por rol del plan se comprueba antes de crear (la base lo
  reforzaría igual), el rol 0 no existe aquí, el correo es único global, y **el
  único Admin activo no se puede desactivar** (dejaría la empresa sin quien la
  administre). Desactivar a alguien corta sus sesiones al instante.
- **Clientes:** particular necesita nombres, empresa necesita razón social; el
  documento no se repite dentro de la empresa. Habilitar el portal crea la cuenta
  del cliente con clave temporal y audiencia propia (criterio 11).
- **Almacén:** el ajuste de inventario deja el stock **en lo contado** (deriva un
  AJUSTE_POS o AJUSTE_NEG según la diferencia; si ya cuadra, no mueve nada) y
  exige motivo. El costo promedio solo lo mueven los ingresos; en la salida se
  conserva.

## Costo promedio ponderado — ejemplo verificado

Ingreso 20 @ 30, luego 20 @ 40 → 40 unidades, costo promedio 35. Salida de 5 →
35 unidades, costo 35 (la salida no mueve el promedio). Comprobado en QA y en los
candados.

## QA

```
psql … -f qa/00_candados.sql            13 candados   (Fase 0)
psql … -f qa/01_candados_consola.sql    16 candados   (Fase 1)
psql … -f qa/02_candados_maestros.sql   17 candados   (Fase 2)
python qa/qa_nucleo.py                  62 comprobaciones (Fase 0)
python qa/qa_consola.py                 135 comprobaciones (Fase 1)
python qa/qa_maestros.py                74 comprobaciones (Fase 2)
```

Barrido final: **42 candados + 271 comprobaciones, todo en verde**, cero 500 en el
log, todos los cuadres en cero. `qa_maestros.py` es repetible: genera documentos,
placas y códigos únicos por corrida y, al terminar, desactiva lo que creó (no
borra: un producto con kardex y un cliente con historia no se borran, por diseño).

## Permisos: lo que el técnico sí y no puede

El arnés dejó claro, y es correcto, que el técnico **ve** vehículos, servicios y
stock (los necesita para trabajar) pero **no** gestiona personal ni clientes, ni
**escribe** en los otros maestros (no crea vehículos, no edita el catálogo, no da
de alta productos). Ver no es editar.

## Pendiente dentro de la Fase 2

- Pantallas Angular de los cinco maestros.
- La **salida** de stock por consumo real (`ALMACEN.CONSUMIR`) se escribe desde la
  orden de trabajo, que es la Fase 3: aquí quedó el motor (insertar kardex tipo
  SALIDA ya funciona y está probado), pero el endpoint de consumo vive con la orden.
- Fotos de vehículo y adjuntos: llegan con las evidencias de la Fase 3.
