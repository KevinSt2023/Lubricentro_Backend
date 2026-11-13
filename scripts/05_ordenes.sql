-- ============================================================================
-- LubriTech · scripts/05_ordenes.sql  —  Fase 3: ordenes de trabajo
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f scripts/05_ordenes.sql
--
-- El corazon del sistema. Agrega:
--   * orden_estado    : catalogo GLOBAL de los 11 estados (cita -> entregada)
--   * orden           : la orden de trabajo, con correlativo por empresa
--   * orden_historial : inmutable; cada cambio de estado con quien y cuando
--   * checklist_item  : plantilla de inspeccion editable por empresa
--   * orden_checklist : las respuestas de la inspeccion de cada orden
--   * orden_servicio  : servicios aplicados (precio congelado al momento)
--   * orden_consumo   : repuestos consumidos (inmutable; mueve kardex)
--   * orden_evidencia : fotos y documentos (metadata; el archivo va a disco)
--
-- REGLAS DE ORO que esta fase amarra EN LA BASE:
--   * la orden avanza de a UN estado; no se salta. La unica salida lateral es
--     Anulada, posible desde cualquier estado no final.
--   * una orden Entregada o Anulada no cambia mas: es final.
--   * el historial y los consumos son inmutables.
--   * el numero de orden es correlativo y unico por empresa.
--   * consumir un repuesto escribe su SALIDA de kardex en la misma operacion
--     (reusa fn_kardex_aplica de la Fase 2): sin stock no hay consumo.
--
-- Idempotente. Reusa fn_tabla_inmutable() de scripts/04_maestros.sql.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ---------------------------------------------------------------------------
-- 1. CATALOGO DE ESTADOS (global, igual para todas las empresas)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS orden_estado (
    id           int  PRIMARY KEY,
    codigo       text NOT NULL UNIQUE,
    nombre       text NOT NULL,
    -- Secuencia del flujo. La orden sube de a 1. Anulada va fuera de linea.
    secuencia    int  NOT NULL,
    es_final     boolean NOT NULL DEFAULT false,
    es_anulacion boolean NOT NULL DEFAULT false
);

INSERT INTO orden_estado (id, codigo, nombre, secuencia, es_final, es_anulacion) VALUES
    ( 1, 'CITA_REGISTRADA',   'Cita registrada',       1, false, false),
    ( 2, 'RECIBIDO',          'Recibido',              2, false, false),
    ( 3, 'EN_DIAGNOSTICO',    'En diagnostico',        3, false, false),
    ( 4, 'COTIZACION_ENVIADA','Cotizacion enviada',    4, false, false),
    ( 5, 'APROBADA',          'Aprobada',              5, false, false),
    ( 6, 'EN_EJECUCION',      'En ejecucion',          6, false, false),
    ( 7, 'CONTROL_CALIDAD',   'En control de calidad', 7, false, false),
    ( 8, 'LISTA',             'Lista',                 8, false, false),
    ( 9, 'FACTURADA',         'Facturada',             9, false, false),
    (10, 'ENTREGADA',         'Entregada',            10, true,  false),
    (11, 'ANULADA',           'Anulada',              99, true,  true)
ON CONFLICT (id) DO UPDATE
   SET codigo = EXCLUDED.codigo, nombre = EXCLUDED.nombre,
       secuencia = EXCLUDED.secuencia, es_final = EXCLUDED.es_final,
       es_anulacion = EXCLUDED.es_anulacion;

-- ---------------------------------------------------------------------------
-- 2. LA ORDEN
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS orden (
    id                bigserial PRIMARY KEY,
    empresa_id        int    NOT NULL REFERENCES empresa(id)       ON DELETE RESTRICT,
    sede_id           int    NOT NULL REFERENCES sede(id)          ON DELETE RESTRICT,
    numero            int    NOT NULL,
    cliente_id        bigint NOT NULL REFERENCES cliente(id)       ON DELETE RESTRICT,
    vehiculo_id       bigint NOT NULL REFERENCES vehiculo(id)      ON DELETE RESTRICT,
    estado_id         int    NOT NULL REFERENCES orden_estado(id),
    asesor_id         int    REFERENCES usuario(id),
    tecnico_id        int    REFERENCES usuario(id),
    kilometraje_ingreso int  CHECK (kilometraje_ingreso IS NULL OR kilometraje_ingreso >= 0),
    diagnostico       text,
    observaciones     text,
    fecha_cita        timestamptz,
    recibido_en       timestamptz,
    entregado_en      timestamptz,
    total_servicios   numeric(12,2) NOT NULL DEFAULT 0 CHECK (total_servicios >= 0),
    total_repuestos   numeric(12,2) NOT NULL DEFAULT 0 CHECK (total_repuestos >= 0),
    total             numeric(12,2) NOT NULL DEFAULT 0 CHECK (total >= 0),
    anulada_motivo    text,
    creado_por        int    REFERENCES usuario(id),
    creado_en         timestamptz NOT NULL DEFAULT now(),
    actualizado_en    timestamptz,
    UNIQUE (empresa_id, numero)
);

CREATE INDEX IF NOT EXISTS ix_orden_empresa_estado ON orden (empresa_id, estado_id);
CREATE INDEX IF NOT EXISTS ix_orden_sede           ON orden (sede_id);
CREATE INDEX IF NOT EXISTS ix_orden_cliente        ON orden (cliente_id);
CREATE INDEX IF NOT EXISTS ix_orden_vehiculo       ON orden (vehiculo_id);
CREATE INDEX IF NOT EXISTS ix_orden_tecnico        ON orden (tecnico_id) WHERE tecnico_id IS NOT NULL;

-- Correlativo por empresa: lo asigna la base, no la aplicacion, para que dos
-- altas simultaneas no choquen. El lock por empresa serializa solo esa cola.
CREATE OR REPLACE FUNCTION fn_orden_numero() RETURNS trigger AS $$
BEGIN
    IF NEW.numero IS NULL OR NEW.numero = 0 THEN
        PERFORM pg_advisory_xact_lock(hashtext('orden:' || NEW.empresa_id));
        SELECT COALESCE(MAX(numero), 0) + 1 INTO NEW.numero
          FROM orden WHERE empresa_id = NEW.empresa_id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_orden_numero ON orden;
CREATE TRIGGER tg_orden_numero
    BEFORE INSERT ON orden
    FOR EACH ROW EXECUTE FUNCTION fn_orden_numero();

-- El candado del flujo: la orden avanza de a un estado y no retrocede. La
-- unica excepcion es Anulada, que se puede activar desde cualquier estado no
-- final. Una orden final (Entregada o Anulada) no cambia mas.
CREATE OR REPLACE FUNCTION fn_orden_transicion() RETURNS trigger AS $$
DECLARE
    v_old_seq   int;
    v_old_final boolean;
    v_new_seq   int;
    v_new_anul  boolean;
BEGIN
    IF NEW.estado_id = OLD.estado_id THEN
        RETURN NEW;    -- no cambia el estado: pasa (edicion de otros campos)
    END IF;

    SELECT secuencia, es_final INTO v_old_seq, v_old_final
      FROM orden_estado WHERE id = OLD.estado_id;
    SELECT secuencia, es_anulacion INTO v_new_seq, v_new_anul
      FROM orden_estado WHERE id = NEW.estado_id;

    IF v_old_final THEN
        RAISE EXCEPTION 'La orden ya esta en un estado final y no cambia mas'
            USING ERRCODE = 'P0001';
    END IF;

    IF v_new_anul THEN
        RETURN NEW;    -- anular desde cualquier estado no final: permitido
    END IF;

    IF v_new_seq <> v_old_seq + 1 THEN
        RAISE EXCEPTION 'La orden avanza de a un estado; no se salta (de % a %)',
            v_old_seq, v_new_seq USING ERRCODE = 'P0001';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_orden_transicion ON orden;
CREATE TRIGGER tg_orden_transicion
    BEFORE UPDATE ON orden
    FOR EACH ROW EXECUTE FUNCTION fn_orden_transicion();

-- ---------------------------------------------------------------------------
-- 3. HISTORIAL DE ESTADOS (inmutable)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS orden_historial (
    id                bigserial PRIMARY KEY,
    empresa_id        int    NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    orden_id          bigint NOT NULL REFERENCES orden(id)   ON DELETE RESTRICT,
    estado_anterior_id int   REFERENCES orden_estado(id),
    estado_nuevo_id   int    NOT NULL REFERENCES orden_estado(id),
    usuario_id        int    REFERENCES usuario(id),
    observacion       text,
    creado_en         timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_orden_historial_orden ON orden_historial (orden_id, id);

DROP TRIGGER IF EXISTS tg_orden_historial_inmutable ON orden_historial;
CREATE TRIGGER tg_orden_historial_inmutable
    BEFORE UPDATE OR DELETE ON orden_historial
    FOR EACH ROW EXECUTE FUNCTION fn_tabla_inmutable();

-- ---------------------------------------------------------------------------
-- 4. CHECKLIST DE INSPECCION
-- ---------------------------------------------------------------------------

-- Plantilla editable por empresa.
CREATE TABLE IF NOT EXISTS checklist_item (
    id         bigserial PRIMARY KEY,
    empresa_id int  NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    codigo     text NOT NULL,
    nombre     text NOT NULL,
    orden      int  NOT NULL DEFAULT 0,
    activo     boolean NOT NULL DEFAULT true,
    creado_en  timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_checklist_item_codigo
    ON checklist_item (empresa_id, upper(codigo));

-- Respuestas de la inspeccion de una orden. El nombre del punto se guarda como
-- copia: si luego editan la plantilla, la inspeccion ya hecha no cambia.
CREATE TABLE IF NOT EXISTS orden_checklist (
    id         bigserial PRIMARY KEY,
    empresa_id int    NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    orden_id   bigint NOT NULL REFERENCES orden(id)   ON DELETE RESTRICT,
    item       text   NOT NULL,
    resultado  text   NOT NULL CHECK (resultado IN ('OK','OBSERVADO','MALO','NA')),
    nota       text,
    creado_en  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_orden_checklist_orden ON orden_checklist (orden_id);

-- ---------------------------------------------------------------------------
-- 5. SERVICIOS APLICADOS A LA ORDEN
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS orden_servicio (
    id          bigserial PRIMARY KEY,
    empresa_id  int    NOT NULL REFERENCES empresa(id)   ON DELETE RESTRICT,
    orden_id    bigint NOT NULL REFERENCES orden(id)     ON DELETE RESTRICT,
    servicio_id bigint REFERENCES servicio(id),
    descripcion text   NOT NULL,
    precio      numeric(10,2) NOT NULL DEFAULT 0 CHECK (precio >= 0),
    cantidad    numeric(10,2) NOT NULL DEFAULT 1 CHECK (cantidad > 0),
    creado_en   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_orden_servicio_orden ON orden_servicio (orden_id);

-- ---------------------------------------------------------------------------
-- 6. REPUESTOS CONSUMIDOS (inmutable; cada consumo mueve kardex)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS orden_consumo (
    id           bigserial PRIMARY KEY,
    empresa_id   int    NOT NULL REFERENCES empresa(id)    ON DELETE RESTRICT,
    orden_id     bigint NOT NULL REFERENCES orden(id)      ON DELETE RESTRICT,
    producto_id  bigint NOT NULL REFERENCES producto(id)   ON DELETE RESTRICT,
    sede_id      int    NOT NULL REFERENCES sede(id)        ON DELETE RESTRICT,
    cantidad     numeric(12,3) NOT NULL CHECK (cantidad > 0),
    costo_unitario numeric(12,4) NOT NULL DEFAULT 0 CHECK (costo_unitario >= 0),
    precio_venta numeric(10,2) NOT NULL DEFAULT 0 CHECK (precio_venta >= 0),
    kardex_id    bigint REFERENCES kardex(id),
    creado_en    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_orden_consumo_orden ON orden_consumo (orden_id);

-- Un consumo no se edita ni se borra: ya movio el stock. Deshacerlo es un
-- ingreso o un ajuste, no un DELETE.
DROP TRIGGER IF EXISTS tg_orden_consumo_inmutable ON orden_consumo;
CREATE TRIGGER tg_orden_consumo_inmutable
    BEFORE UPDATE OR DELETE ON orden_consumo
    FOR EACH ROW EXECUTE FUNCTION fn_tabla_inmutable();

-- ---------------------------------------------------------------------------
-- 7. EVIDENCIAS (fotos / documentos; el archivo vive en disco)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS orden_evidencia (
    id         bigserial PRIMARY KEY,
    empresa_id int    NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    orden_id   bigint NOT NULL REFERENCES orden(id)   ON DELETE RESTRICT,
    tipo       text   NOT NULL DEFAULT 'FOTO' CHECK (tipo IN ('FOTO','DOCUMENTO')),
    archivo    text   NOT NULL,              -- ruta relativa en el almacen de archivos
    nombre     text,                         -- nombre original
    mime       text   NOT NULL,
    bytes      bigint NOT NULL CHECK (bytes > 0),
    hash       text,                         -- sha-256 del contenido
    subido_por int    REFERENCES usuario(id),
    creado_en  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_orden_evidencia_orden ON orden_evidencia (orden_id);

-- ---------------------------------------------------------------------------
-- 8. PLANTILLA DE CHECKLIST POR DEFECTO (para las empresas que ya existen)
-- ---------------------------------------------------------------------------

-- Un lubricentro arranca con esta inspeccion base. Es editable despues.
INSERT INTO checklist_item (empresa_id, codigo, nombre, orden)
SELECT e.id, c.codigo, c.nombre, c.orden
  FROM empresa e
  CROSS JOIN (VALUES
        ('NIVEL_ACEITE',  'Nivel de aceite de motor',      1),
        ('FILTRO_ACEITE', 'Filtro de aceite',              2),
        ('FILTRO_AIRE',   'Filtro de aire',                3),
        ('REFRIGERANTE',  'Nivel de refrigerante',         4),
        ('FRENOS',        'Pastillas y liquido de frenos',  5),
        ('LLANTAS',       'Estado y presion de llantas',   6),
        ('LUCES',         'Luces y senalizacion',          7),
        ('BATERIA',       'Bateria y bornes',              8),
        ('LIMPIAPARAB',   'Limpiaparabrisas y agua',       9),
        ('FUGAS',         'Fugas visibles',               10)
       ) AS c(codigo, nombre, orden)
ON CONFLICT (empresa_id, upper(codigo)) DO NOTHING;

COMMIT;

-- Comprobacion de lo aplicado.
SELECT 'orden_estado (debe ser 11)' AS objeto, count(*)::text AS valor FROM orden_estado
UNION ALL SELECT 'orden',           to_regclass('orden')::text
UNION ALL SELECT 'orden_historial', to_regclass('orden_historial')::text
UNION ALL SELECT 'orden_consumo',   to_regclass('orden_consumo')::text
UNION ALL SELECT 'orden_evidencia', to_regclass('orden_evidencia')::text
UNION ALL SELECT 'trigger transicion',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_orden_transicion')
UNION ALL SELECT 'trigger correlativo',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_orden_numero')
UNION ALL SELECT 'checklist por defecto de la demo',
       (SELECT count(*)::text FROM checklist_item ci JOIN empresa e ON e.id = ci.empresa_id
         WHERE e.ruc = '20600123456');
