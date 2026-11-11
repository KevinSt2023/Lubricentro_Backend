-- ============================================================================
-- LubriTech · scripts/03_cortesia_registrada.sql
--
-- Arreglo de datos, no de esquema.
--
-- Hasta la Fase 1, el alta de una empresa movia servicio_hasta por los dias
-- de cortesia SIN dejar ningun periodo registrado. Eso abria un agujero: al
-- anular un pago, servicio_hasta se recalcula desde los periodos pagados que
-- quedan, no encontraba ninguno y la empresa perdia los dias de prueba que se
-- le habian regalado. El arnes de la Fase 1 lo encontro.
--
-- El alta ya registra la cortesia como un periodo de monto cero. Este script
-- hace lo mismo para las empresas que nacieron antes, para que el cuadre de
-- qa/01_candados_consola.sql ("empresa con servicio_hasta sin ningun pago")
-- pueda exigir cero de verdad.
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f scripts/03_cortesia_registrada.sql
--
-- Es idempotente: solo toca empresas sin ningun periodo pagado.
-- ============================================================================

BEGIN;

INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado, referencia)
SELECT e.id,
       e.plan_id,
       -- Desde el dia en que se dio de alta la empresa, sin pasarse del fin
       -- de la cortesia (una empresa creada hoy con cortesia de hoy es un
       -- periodo de un solo dia, no un periodo invertido).
       least((e.creado_en AT TIME ZONE 'America/Lima')::date, e.servicio_hasta),
       e.servicio_hasta,
       0,
       'PAGADA',
       'CORTESIA'
  FROM empresa e
 WHERE e.servicio_hasta >= (now() AT TIME ZONE 'America/Lima')::date
   AND NOT EXISTS (SELECT 1 FROM suscripcion s
                    WHERE s.empresa_id = e.id AND s.estado = 'PAGADA');

COMMIT;

-- Comprobacion: la columna "filas" debe ser 0.
SELECT 'empresa con servicio vigente y sin ningun periodo pagado' AS control,
       count(*) AS filas
  FROM empresa e
 WHERE e.servicio_hasta >= (now() AT TIME ZONE 'America/Lima')::date
   AND NOT EXISTS (SELECT 1 FROM suscripcion s
                    WHERE s.empresa_id = e.id AND s.estado = 'PAGADA');
