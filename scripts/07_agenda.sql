-- ============================================================================
-- LubriTech · scripts/07_agenda.sql  —  Fase 5: agenda y citas
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f scripts/07_agenda.sql
--
-- La agenda NO es una tabla aparte: una cita ES una orden en su primer estado
-- (CITA_REGISTRADA) con fecha_cita. La hoja de ruta lo dice asi: "cita
-- registrada como primer estado de la orden". Esta fase solo agrega lo que
-- faltaba para que la agenda cuadre:
--
--   * el CANDADO: una orden en CITA_REGISTRADA DEBE tener fecha_cita. Sin fecha
--     no hay cita; la base lo exige, no la aplicacion.
--   * un indice parcial para listar la agenda por sede y rango de fechas rapido.
--
-- Idempotente. No crea tablas ni columnas nuevas (reusa orden.fecha_cita).
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ---------------------------------------------------------------------------
-- 1. CANDADO: una cita registrada exige fecha
-- ---------------------------------------------------------------------------
-- Vale para alta y para edicion: si la orden esta (o queda) en CITA_REGISTRADA,
-- fecha_cita no puede ser NULL. Al avanzar a RECIBIDO la fecha ya no se exige
-- (el estado nuevo no es cita), y la fecha original se conserva.
CREATE OR REPLACE FUNCTION fn_orden_cita_fecha() RETURNS trigger AS $$
BEGIN
    IF (SELECT codigo FROM orden_estado WHERE id = NEW.estado_id) = 'CITA_REGISTRADA'
       AND NEW.fecha_cita IS NULL THEN
        RAISE EXCEPTION 'Una cita registrada necesita fecha y hora (fecha_cita)'
            USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_orden_cita_fecha ON orden;
CREATE TRIGGER tg_orden_cita_fecha
    BEFORE INSERT OR UPDATE ON orden
    FOR EACH ROW EXECUTE FUNCTION fn_orden_cita_fecha();

-- ---------------------------------------------------------------------------
-- 2. INDICE de la agenda: citas pendientes por sede y fecha
-- ---------------------------------------------------------------------------
-- Parcial: solo las ordenes que todavia son cita (estado 1 = CITA_REGISTRADA).
-- Es lo que lee el calendario; se mantiene chico porque una orden deja el
-- indice apenas se recibe.
CREATE INDEX IF NOT EXISTS ix_orden_cita
    ON orden (empresa_id, sede_id, fecha_cita)
    WHERE estado_id = 1;

COMMIT;

-- ---------------------------------------------------------------------------
-- Verificacion
-- ---------------------------------------------------------------------------
SELECT 'tg_orden_cita_fecha existe' AS objeto,
       (count(*) > 0)::text AS valor
  FROM pg_trigger WHERE tgname = 'tg_orden_cita_fecha';

SELECT 'citas sin fecha (debe ser 0)' AS objeto, count(*)::text AS valor
  FROM orden o JOIN orden_estado e ON e.id = o.estado_id
 WHERE e.codigo = 'CITA_REGISTRADA' AND o.fecha_cita IS NULL;
