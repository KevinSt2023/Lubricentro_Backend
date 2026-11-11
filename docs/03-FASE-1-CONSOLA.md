# Fase 1 · Consola del SuperAdmin (backend)

Cerrada el 2026-11-10. Es la parte del sistema con la que se **vende y se cobra** el
SaaS: dar de alta un lubricentro, ponerle un plan, cobrarle, cortarle y reactivarlo.

Alcance de lo cerrado: **API y base de datos**. Las pantallas Angular de la consola
todavía no existen; se construyen con el esqueleto del ERP web.

## Qué se puede hacer ya

| Para qué | Endpoint | Quién |
|---|---|---|
| Listar empresas (paginado, filtro por estado y plan, búsqueda) | `GET /api/sa/empresas` | SuperAdmin |
| Alta completa: empresa + sede principal + Admin | `POST /api/sa/empresas` | SuperAdmin |
| Ver / editar datos de una empresa | `GET` / `PUT /api/sa/empresas/{id}` | SuperAdmin |
| Cambiar de plan | `PUT /api/sa/empresas/{id}/plan` | SuperAdmin |
| Dar de baja / reactivar | `PUT /api/sa/empresas/{id}/estado` | SuperAdmin |
| Salud y cumplimiento | `GET /api/sa/empresas/{id}/salud` | SuperAdmin |
| Clave temporal nueva para el Admin | `POST /api/sa/empresas/{id}/clave-admin` | SuperAdmin |
| Sedes de cualquier empresa | `/api/sa/empresas/{id}/sedes…` | SuperAdmin |
| Registrar un pago | `POST /api/sa/empresas/{id}/suscripciones` | SuperAdmin |
| Historial de pagos y anulación | `GET /api/sa/suscripciones`, `POST …/{id}/anular` | SuperAdmin |
| Planes, funciones y cupos | `/api/sa/planes…` | SuperAdmin |
| Tablero del negocio | `GET /api/sa/tablero` | SuperAdmin |
| Sedes de **mi** empresa | `/api/sedes…` | Admin de la empresa |

## Reglas que quedaron amarradas

1. **La consola es solo del rol 0.** `[SoloSuperAdmin]` va en la clase, no en cada
   acción: ahí dentro se apaga el filtro de empresa, así que una acción que se
   olvide del atributo sería una fuga.
2. **El Admin nunca escribe el id de su empresa.** En `/api/sedes` la empresa sale
   del token. Una sede de otra empresa responde 404, no 403: para él no existe.
3. **El permiso efectivo es rol ∩ plan.** `SEDES.GESTIONAR` depende de la función
   `MULTISEDE`. Al bajar una empresa a Básico, el mismo Admin pierde ese permiso al
   instante, sin tocarle el rol. *Tener un local se configura; tener varios se paga.*
4. **Un cambio de plan nunca deja a la empresa excedida.** Si no cabe (sedes o cupo
   por rol), responde 409 `PLAN_NO_ALCANZA` diciendo qué sobra. Lo mismo al editar un
   plan: 409 `EMPRESAS_EXCEDIDAS`.
5. **El pago encadena.** Si la empresa está al día, el periodo nuevo arranca el día
   siguiente al último pagado: no se pierde lo ya pagado. Si está vencida, arranca
   hoy: no se cobra lo que no se usó.
6. **Un pago no se edita ni se borra: se anula con motivo.** Al anular,
   `servicio_hasta` se recalcula desde los periodos que quedan.
7. **La cortesía es un periodo registrado** (monto 0, referencia `CORTESIA`). Todo
   día de servicio tiene un periodo que lo respalda.
8. **La baja corta al instante**: el token que el Admin ya tenía deja de servir en
   la siguiente petición, y exige motivo. Reactivar no lo exige.
9. **Todo queda en la bitácora con autor**: alta, edición, plan, baja,
   reactivación, clave del Admin, sedes, pagos y anulaciones.

Candados nuevos en la base (`scripts/02_consola_saas.sql`): catálogo `funcion` con
FK desde `plan_funcion` y `permiso`; ningún cupo para el rol 0; exclusión
`ex_suscripcion_sin_solape` (dos periodos pagados de una empresa no se cruzan);
`tg_suscripcion_estado` (anulado no revive, pagado no vuelve a pendiente, periodo y
monto de un pago no se retocan).

## Scripts de esta fase (orden de aplicación)

```
scripts/02_consola_saas.sql        esquema: catálogo de funciones y candados de suscripción
scripts/03_cortesia_registrada.sql datos: periodo de cortesía para empresas anteriores
scripts/01_seed.sql                (reaplicar) ahora siembra el periodo pagado de las demos
```

## QA

```
psql … -f qa/00_candados.sql             13 candados   (Fase 0)
psql … -f qa/01_candados_consola.sql     16 candados   (Fase 1)
python qa/qa_nucleo.py                   62 comprobaciones (Fase 0)
python qa/qa_consola.py                  135 comprobaciones (Fase 1)
```

Resultado del barrido final: todo en verde, cero 500 en el log, todos los cuadres
en cero. `qa_consola.py` es repetible: se corrió varias veces sobre la misma base
y deja la empresa de pruebas como la encontró.

## Defectos reales que encontró el arnés (y se corrigieron)

1. **Toda transacción manual daba 500.** La conexión usa `EnableRetryOnFailure`, y
   esa estrategia de EF prohíbe `BeginTransactionAsync` a mano. Reventaba el alta de
   empresa, el pago, la anulación y el cambio de sede principal. Arreglo:
   `Datos/Transacciones.cs` (`bd.EnTransaccionAsync`), que entrega el bloque entero a
   la estrategia. **Regla para adelante: ningún servicio llama a
   `BeginTransactionAsync` directamente.**
2. **El tablero daba 500.** Se comparaba `registrado_en` contra un instante con
   desfase −05:00 y Npgsql solo acepta UTC en `timestamptz`. Arreglo:
   `Tiempo.InicioDelDiaLima(dia)`, que devuelve el inicio del día de Lima ya en UTC.
3. **Anular el primer pago borraba la cortesía.** El alta movía `servicio_hasta` sin
   registrar ningún periodo; al anular, el recálculo no encontraba nada y dejaba a la
   empresa vencida. De ahí sale la regla 7 y el script 03.
4. **Las demos tenían servicio hasta 2027 sin un solo pago.** Incoherencia de la
   siembra que por la API no se puede producir; el seed ahora siembra su periodo.

## Datos de prueba que se conservan

| RUC | Empresa | Estado | Para qué |
|---|---|---|---|
| 20777000001 | LubriTech QA | Profesional, activa, 2 sedes | La usa `qa_consola.py` en cada barrido. Admin `admin@lubritechqa.pe`, clave de dev de siempre. |
| 20777000009 | LubriTech QA Cortesía | Básico, **dada de baja** | Alta de un solo uso que verificó en vivo que la cortesía sobrevive a una anulación. |

No se pueden borrar (una empresa con pagos no se borra, por diseño), así que quedan
documentadas aquí. Solo existen en la base de desarrollo.

## Pendiente dentro de la Fase 1

- Pantallas Angular de la consola.
- Corte **automático** por vencimiento ya funciona en el acceso (login y sesión
  rechazan a una empresa vencida); lo que no existe todavía es el aviso previo al
  cliente (correo / push a los 7 días), que depende de la fase de notificaciones.
- Los precios de los planes (99 / 189 / 329) siguen siendo **provisionales**.
