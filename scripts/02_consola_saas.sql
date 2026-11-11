-- ============================================================================
-- LubriTech · 02_consola_saas.sql
-- Fase 1 — Consola del SuperAdmin.
--
-- No crea modulos nuevos: le pone candados y catalogo a lo que la consola va
-- a manejar (planes, funciones, suscripciones), para que las reglas
-- comerciales no dependan de que el codigo se acuerde de ellas.
--
--   PGCLIENTENCODING=UTF8 "C:/Program Files/PostgreSQL/18/bin/psql.exe" \
--     -h localhost -U postgres -d LubriTech -v ON_ERROR_STOP=1 -P pager=off \
--     -f scripts/02_consola_saas.sql
--
-- Idempotente.
-- ============================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- 1. CATALOGO DE FUNCIONES
--    Hasta ahora "la funcion que el plan habilita" era texto libre: un
--    MULTISEDE mal escrito en plan_funcion no habilitaba nada y no avisaba
--    a nadie. Con catalogo y clave ajena, el error sale en el momento.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS funcion (
    codigo      text PRIMARY KEY,
    nombre      text NOT NULL,
    descripcion text,
    -- Para agrupar la pantalla de armado de planes.
    grupo       text NOT NULL DEFAULT 'GENERAL',
    orden       int  NOT NULL DEFAULT 0
);

INSERT INTO funcion (codigo, nombre, descripcion, grupo, orden) VALUES
    ('APP_OPERACIONES',        'App de Operaciones',
     'La app movil del personal del taller: ordenes, checklist, evidencias.', 'APPS', 1),
    ('APP_CLIENTES',           'App de Clientes',
     'La app movil del dueno del vehiculo: seguimiento, citas, aprobaciones.', 'APPS', 2),
    ('MULTISEDE',              'Multisede',
     'Mas de un local, con stock y personal por sede.', 'OPERACION', 3),
    ('OFFLINE',                'Modo sin conexion',
     'La app guarda y reintenta cuando el taller se queda sin senal.', 'OPERACION', 4),
    ('FLOTAS',                 'Flotas de empresa',
     'Clientes empresa con varios vehiculos a su nombre.', 'OPERACION', 5),
    ('VISOR_3D',               'Visor 3D',
     'Vista 3D del vehiculo con despiece y consulta de piezas.', 'EXTRAS', 6),
    ('REPORTES_AVANZADOS',     'Reportes avanzados',
     'Reportes cruzados y exportables, mas alla de los basicos.', 'EXTRAS', 7),
    ('FACTURACION_ELECTRONICA','Facturacion electronica',
     'Emision de comprobantes electronicos.', 'EXTRAS', 8)
ON CONFLICT (codigo) DO UPDATE
   SET nombre = EXCLUDED.nombre,
       descripcion = EXCLUDED.descripcion,
       grupo = EXCLUDED.grupo,
       orden = EXCLUDED.orden;

-- Clave ajena desde lo que ya existe. Si alguna fila no calza, el script
-- falla aqui a proposito: es exactamente el error que se quiere encontrar.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_plan_funcion_funcion') THEN
        ALTER TABLE plan_funcion
            ADD CONSTRAINT fk_plan_funcion_funcion
            FOREIGN KEY (funcion) REFERENCES funcion(codigo) ON UPDATE CASCADE;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_permiso_funcion') THEN
        ALTER TABLE permiso
            ADD CONSTRAINT fk_permiso_funcion
            FOREIGN KEY (funcion_requerida) REFERENCES funcion(codigo) ON UPDATE CASCADE;
    END IF;

    -- Al nucleo le faltaba esta: plan_rol_cupo.rol_id no apuntaba a rol.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_plan_rol_cupo_rol') THEN
        ALTER TABLE plan_rol_cupo
            ADD CONSTRAINT fk_plan_rol_cupo_rol
            FOREIGN KEY (rol_id) REFERENCES rol(id);
    END IF;
END $$;

-- El rol 0 (SuperAdmin) no pertenece a ninguna empresa: no tiene cupo que dar.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_plan_rol_cupo_sin_sa') THEN
        ALTER TABLE plan_rol_cupo
            ADD CONSTRAINT ck_plan_rol_cupo_sin_sa CHECK (rol_id > 0);
    END IF;
END $$;

-- ---------------------------------------------------------------------------
-- 2. SUSCRIPCIONES: NI UN DIA REGALADO, NI UN DIA COBRADO DOS VECES
--    Dos periodos pagados de la misma empresa no pueden pisarse. El candado
--    va en la base porque es plata: un reintento de la app, dos pestanas
--    abiertas o un doble clic no pueden terminar en un dia cobrado dos veces.
-- ---------------------------------------------------------------------------

CREATE EXTENSION IF NOT EXISTS btree_gist;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ex_suscripcion_sin_solape') THEN
        ALTER TABLE suscripcion
            ADD CONSTRAINT ex_suscripcion_sin_solape
            EXCLUDE USING gist (
                empresa_id WITH =,
                daterange(desde, hasta, '[]') WITH &&
            ) WHERE (estado = 'PAGADA');
    END IF;
END $$;

COMMENT ON CONSTRAINT ex_suscripcion_sin_solape ON suscripcion IS
    'Dos periodos PAGADOS de la misma empresa no pueden solaparse: ningun dia se cobra dos veces.';

-- Una suscripcion anulada no vuelve a PAGADA: se registra otra. Y una
-- pagada no se puede "despagar" sin dejar el rastro de la anulacion.
CREATE OR REPLACE FUNCTION fn_suscripcion_estado() RETURNS trigger AS $$
BEGIN
    IF OLD.estado = 'ANULADA' AND NEW.estado <> 'ANULADA' THEN
        RAISE EXCEPTION 'Una suscripcion anulada no se puede reactivar: registra una nueva'
            USING ERRCODE = 'P0001';
    END IF;

    IF OLD.estado = 'PAGADA' AND NEW.estado = 'PENDIENTE' THEN
        RAISE EXCEPTION 'Una suscripcion pagada no vuelve a pendiente: anulala y registra otra'
            USING ERRCODE = 'P0001';
    END IF;

    -- El periodo y el monto de una suscripcion pagada no se retocan.
    IF OLD.estado = 'PAGADA' AND NEW.estado = 'PAGADA'
       AND (OLD.desde, OLD.hasta, OLD.monto, OLD.empresa_id, OLD.plan_id)
        IS DISTINCT FROM (NEW.desde, NEW.hasta, NEW.monto, NEW.empresa_id, NEW.plan_id) THEN
        RAISE EXCEPTION 'No se edita una suscripcion pagada: anulala y registra la correcta'
            USING ERRCODE = 'P0001';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_suscripcion_estado ON suscripcion;
CREATE TRIGGER tg_suscripcion_estado
    BEFORE UPDATE ON suscripcion
    FOR EACH ROW EXECUTE FUNCTION fn_suscripcion_estado();

-- ---------------------------------------------------------------------------
-- 3. INDICES PARA EL TABLERO Y LOS LISTADOS
-- ---------------------------------------------------------------------------

-- El tablero pregunta siempre por estado del servicio.
CREATE INDEX IF NOT EXISTS ix_empresa_servicio ON empresa(activo, servicio_hasta);

-- Busqueda de empresas por nombre y RUC (el listado del SuperAdmin).
CREATE INDEX IF NOT EXISTS ix_empresa_busqueda
    ON empresa (lower(nombre_comercial), lower(razon_social), ruc);

-- Conteo de usuarios por empresa y rol: lo usa el control de cupo del plan
-- y la pantalla de salud.
CREATE INDEX IF NOT EXISTS ix_usuario_empresa_rol
    ON usuario(empresa_id, rol_id) WHERE activo;

COMMIT;

-- Comprobacion de lo aplicado.
SELECT 'funciones'                AS objeto, count(*)::text AS valor FROM funcion
UNION ALL
SELECT 'plan_funcion con FK',
       (SELECT count(*)::text FROM pg_constraint WHERE conname = 'fk_plan_funcion_funcion')
UNION ALL
SELECT 'suscripcion sin solape',
       (SELECT count(*)::text FROM pg_constraint WHERE conname = 'ex_suscripcion_sin_solape')
UNION ALL
SELECT 'btree_gist',
       (SELECT count(*)::text FROM pg_extension WHERE extname = 'btree_gist');
