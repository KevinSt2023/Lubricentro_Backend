-- ============================================================================
-- LubriTech · scripts/08_mantenimiento.sql  —  Fase 5 (it. 2): próximos mantenimientos
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f scripts/08_mantenimiento.sql
--
-- "Próximos mantenimientos por kilometraje y por fecha": cada vehículo guarda
-- cuándo le toca el próximo servicio. Lo fija el taller a mano o se propone solo
-- al entregar una orden. La agenda lee esto para avisar al cliente antes.
--
-- Agrega al vehículo:
--   * proximo_mant_fecha : date  — cuándo toca (por calendario)
--   * proximo_mant_km    : int   — a qué kilometraje toca (>= 0)
-- más un índice parcial para la consulta de "vencidos / por vencer".
--
-- Idempotente. Sin tablas ni triggers nuevos.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

ALTER TABLE vehiculo ADD COLUMN IF NOT EXISTS proximo_mant_fecha date;
ALTER TABLE vehiculo ADD COLUMN IF NOT EXISTS proximo_mant_km    int;

-- El km objetivo, si está, no es negativo.
ALTER TABLE vehiculo DROP CONSTRAINT IF EXISTS ck_vehiculo_mant_km;
ALTER TABLE vehiculo ADD  CONSTRAINT ck_vehiculo_mant_km
    CHECK (proximo_mant_km IS NULL OR proximo_mant_km >= 0);

-- Índice parcial: solo los vehículos que tienen algún objetivo de mantenimiento.
CREATE INDEX IF NOT EXISTS ix_vehiculo_mant
    ON vehiculo (empresa_id, proximo_mant_fecha)
    WHERE proximo_mant_fecha IS NOT NULL OR proximo_mant_km IS NOT NULL;

COMMIT;

-- ---------------------------------------------------------------------------
-- Verificación
-- ---------------------------------------------------------------------------
SELECT 'columnas de mantenimiento (debe ser 2)' AS objeto, count(*)::text AS valor
  FROM information_schema.columns
 WHERE table_name = 'vehiculo' AND column_name IN ('proximo_mant_fecha', 'proximo_mant_km');
