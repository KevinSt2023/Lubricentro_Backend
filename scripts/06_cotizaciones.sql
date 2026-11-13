-- ============================================================================
-- LubriTech · scripts/06_cotizaciones.sql  —  Fase 4: cotizaciones y aprobaciones
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f scripts/06_cotizaciones.sql
--
-- El flujo formal del trabajo adicional:
--   hallazgo del tecnico -> cotizacion versionada del asesor -> decision del
--   cliente guardada INMUTABLE. Sin esa fila de decision, el renglon adicional
--   no pasa a ejecucion.
--
-- Agrega:
--   * orden_hallazgo      : lo que el tecnico encuentra y hay que cotizar
--   * cotizacion          : la cotizacion, VERSIONADA por orden (correlativo)
--   * cotizacion_renglon  : los renglones; subtotal lo calcula la base
--   * cotizacion_decision : la decision del cliente, INMUTABLE y unica por cotizacion
--   * orden_servicio.cotizacion_renglon_id  (y orden_consumo): el vinculo que
--     amarra "sin aprobacion, el renglon no pasa a ejecucion"
--
-- REGLAS DE ORO que esta fase amarra EN LA BASE:
--   * la version de una cotizacion es correlativa y unica por orden (la pone
--     la base, no la aplicacion).
--   * una cotizacion solo se edita (sus renglones) mientras es BORRADOR.
--   * una cotizacion solo se APRUEBA o RECHAZA si existe una fila de decision
--     del cliente: el estado no se puede forzar a mano.
--   * la decision del cliente es inmutable y hay una sola por cotizacion.
--   * solo hay una cotizacion ENVIADA (pendiente) por orden a la vez.
--   * un renglon de orden que nace de una cotizacion no entra si esa cotizacion
--     no esta APROBADA.
--
-- Idempotente. Reusa fn_tabla_inmutable() de scripts/04_maestros.sql.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ---------------------------------------------------------------------------
-- 1. HALLAZGOS DEL TECNICO
-- ---------------------------------------------------------------------------

-- Lo que el tecnico detecta durante el diagnostico y hay que cotizar. Es la
-- materia prima de la cotizacion del asesor. No es inmutable: mientras no se
-- atienda, el tecnico lo corrige o lo quita.
CREATE TABLE IF NOT EXISTS orden_hallazgo (
    id          bigserial PRIMARY KEY,
    empresa_id  int    NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    orden_id    bigint NOT NULL REFERENCES orden(id)   ON DELETE RESTRICT,
    descripcion text   NOT NULL,
    severidad   text   NOT NULL DEFAULT 'MODERADO'
                       CHECK (severidad IN ('LEVE','MODERADO','CRITICO')),
    atendido    boolean NOT NULL DEFAULT false,
    creado_por  int    REFERENCES usuario(id),
    creado_en   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_orden_hallazgo_orden ON orden_hallazgo (orden_id);

-- ---------------------------------------------------------------------------
-- 2. LA COTIZACION (versionada por orden)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cotizacion (
    id             bigserial PRIMARY KEY,
    empresa_id     int    NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    orden_id       bigint NOT NULL REFERENCES orden(id)   ON DELETE RESTRICT,
    version        int    NOT NULL,
    estado         text   NOT NULL DEFAULT 'BORRADOR'
                          CHECK (estado IN ('BORRADOR','ENVIADA','APROBADA','RECHAZADA','REEMPLAZADA','VENCIDA')),
    total          numeric(12,2) NOT NULL DEFAULT 0 CHECK (total >= 0),
    moneda         text   NOT NULL DEFAULT 'PEN',
    nota           text,
    vigencia_hasta date,
    creado_por     int    REFERENCES usuario(id),
    enviada_en     timestamptz,
    decidida_en    timestamptz,
    creado_en      timestamptz NOT NULL DEFAULT now(),
    actualizado_en timestamptz,
    UNIQUE (orden_id, version)
);

CREATE INDEX IF NOT EXISTS ix_cotizacion_orden  ON cotizacion (orden_id, version);
CREATE INDEX IF NOT EXISTS ix_cotizacion_estado ON cotizacion (empresa_id, estado);

-- Una sola cotizacion ENVIADA (pendiente de decision) por orden a la vez.
-- Las versiones viejas pasan a REEMPLAZADA cuando el asesor manda una nueva.
CREATE UNIQUE INDEX IF NOT EXISTS ux_cotizacion_pendiente
    ON cotizacion (orden_id) WHERE estado = 'ENVIADA';

-- Correlativo de version por orden: lo asigna la base, como el numero de la
-- orden. El lock por orden serializa solo esa cola.
CREATE OR REPLACE FUNCTION fn_cotizacion_version() RETURNS trigger AS $$
BEGIN
    IF NEW.version IS NULL OR NEW.version = 0 THEN
        PERFORM pg_advisory_xact_lock(hashtext('cotizacion:' || NEW.orden_id));
        SELECT COALESCE(MAX(version), 0) + 1 INTO NEW.version
          FROM cotizacion WHERE orden_id = NEW.orden_id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_cotizacion_version ON cotizacion;
CREATE TRIGGER tg_cotizacion_version
    BEFORE INSERT ON cotizacion
    FOR EACH ROW EXECUTE FUNCTION fn_cotizacion_version();

-- El candado del estado: una cotizacion ya decidida no cambia; y aprobarla o
-- rechazarla exige que EXISTA la fila de decision del cliente. Asi nadie
-- aprueba una cotizacion "a mano" desde el ERP: la unica puerta es la decision.
CREATE OR REPLACE FUNCTION fn_cotizacion_estado() RETURNS trigger AS $$
BEGIN
    IF NEW.estado = OLD.estado THEN
        RETURN NEW;
    END IF;

    IF OLD.estado IN ('APROBADA','RECHAZADA') THEN
        RAISE EXCEPTION 'Una cotizacion ya decidida no cambia de estado'
            USING ERRCODE = 'P0001';
    END IF;

    IF NEW.estado IN ('APROBADA','RECHAZADA')
       AND NOT EXISTS (SELECT 1 FROM cotizacion_decision d WHERE d.cotizacion_id = NEW.id) THEN
        RAISE EXCEPTION 'La cotizacion no se aprueba ni rechaza sin la decision del cliente'
            USING ERRCODE = 'P0001';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_cotizacion_estado ON cotizacion;
CREATE TRIGGER tg_cotizacion_estado
    BEFORE UPDATE ON cotizacion
    FOR EACH ROW EXECUTE FUNCTION fn_cotizacion_estado();

-- ---------------------------------------------------------------------------
-- 3. RENGLONES DE LA COTIZACION
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cotizacion_renglon (
    id             bigserial PRIMARY KEY,
    empresa_id     int    NOT NULL REFERENCES empresa(id)    ON DELETE RESTRICT,
    cotizacion_id  bigint NOT NULL REFERENCES cotizacion(id) ON DELETE CASCADE,
    tipo           text   NOT NULL CHECK (tipo IN ('SERVICIO','REPUESTO')),
    servicio_id    bigint REFERENCES servicio(id),
    producto_id    bigint REFERENCES producto(id),
    descripcion    text   NOT NULL,
    cantidad       numeric(12,3) NOT NULL DEFAULT 1 CHECK (cantidad > 0),
    precio_unitario numeric(10,2) NOT NULL DEFAULT 0 CHECK (precio_unitario >= 0),
    -- El subtotal lo calcula la base: no se confia en que la aplicacion mande
    -- un total coherente con cantidad x precio.
    subtotal       numeric(12,2) GENERATED ALWAYS AS (round(cantidad * precio_unitario, 2)) STORED,
    creado_en      timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_cotizacion_renglon_cot ON cotizacion_renglon (cotizacion_id);

-- Los renglones solo se tocan mientras la cotizacion es BORRADOR. Una vez
-- ENVIADA queda congelada: cambiarla es armar una version nueva.
CREATE OR REPLACE FUNCTION fn_cotizacion_renglon_bloqueo() RETURNS trigger AS $$
DECLARE
    v_cot_id bigint;
    v_estado text;
BEGIN
    v_cot_id := COALESCE(NEW.cotizacion_id, OLD.cotizacion_id);
    SELECT estado INTO v_estado FROM cotizacion WHERE id = v_cot_id;

    -- Si la cotizacion ya no existe (borrado en cascada), dejar pasar el DELETE.
    IF v_estado IS NULL THEN
        RETURN COALESCE(OLD, NEW);
    END IF;

    IF v_estado <> 'BORRADOR' THEN
        RAISE EXCEPTION 'Los renglones solo se editan mientras la cotizacion es un borrador (estado %)', v_estado
            USING ERRCODE = 'P0001';
    END IF;

    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_cotizacion_renglon_bloqueo ON cotizacion_renglon;
CREATE TRIGGER tg_cotizacion_renglon_bloqueo
    BEFORE INSERT OR UPDATE OR DELETE ON cotizacion_renglon
    FOR EACH ROW EXECUTE FUNCTION fn_cotizacion_renglon_bloqueo();

-- ---------------------------------------------------------------------------
-- 4. LA DECISION DEL CLIENTE (inmutable, una por cotizacion)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cotizacion_decision (
    id            bigserial PRIMARY KEY,
    empresa_id    int    NOT NULL REFERENCES empresa(id)    ON DELETE RESTRICT,
    cotizacion_id bigint NOT NULL UNIQUE REFERENCES cotizacion(id) ON DELETE RESTRICT,
    decision      text   NOT NULL CHECK (decision IN ('APROBADA','RECHAZADA')),
    cliente_id    bigint NOT NULL REFERENCES cliente(id) ON DELETE RESTRICT,
    comentario    text,
    canal         text   NOT NULL DEFAULT 'PORTAL',
    ip            text,
    creado_en     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_cotizacion_decision_cot ON cotizacion_decision (cotizacion_id);

-- La decision solo cabe sobre una cotizacion ENVIADA. Y una vez puesta, pone
-- el estado de la cotizacion: esa es la unica forma de aprobar o rechazar.
CREATE OR REPLACE FUNCTION fn_cotizacion_decision_valida() RETURNS trigger AS $$
DECLARE
    v_estado text;
BEGIN
    SELECT estado INTO v_estado FROM cotizacion WHERE id = NEW.cotizacion_id;
    IF v_estado IS DISTINCT FROM 'ENVIADA' THEN
        RAISE EXCEPTION 'Solo se decide una cotizacion que este ENVIADA (estado actual %)', COALESCE(v_estado, 'inexistente')
            USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_cotizacion_decision_valida ON cotizacion_decision;
CREATE TRIGGER tg_cotizacion_decision_valida
    BEFORE INSERT ON cotizacion_decision
    FOR EACH ROW EXECUTE FUNCTION fn_cotizacion_decision_valida();

CREATE OR REPLACE FUNCTION fn_cotizacion_decision_aplica() RETURNS trigger AS $$
BEGIN
    UPDATE cotizacion
       SET estado = NEW.decision, decidida_en = now()
     WHERE id = NEW.cotizacion_id;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_cotizacion_decision_aplica ON cotizacion_decision;
CREATE TRIGGER tg_cotizacion_decision_aplica
    AFTER INSERT ON cotizacion_decision
    FOR EACH ROW EXECUTE FUNCTION fn_cotizacion_decision_aplica();

-- La decision no se edita ni se borra: es la prueba del criterio 5.
DROP TRIGGER IF EXISTS tg_cotizacion_decision_inmutable ON cotizacion_decision;
CREATE TRIGGER tg_cotizacion_decision_inmutable
    BEFORE UPDATE OR DELETE ON cotizacion_decision
    FOR EACH ROW EXECUTE FUNCTION fn_tabla_inmutable();

-- ---------------------------------------------------------------------------
-- 5. EL VINCULO RENGLON -> EJECUCION
-- ---------------------------------------------------------------------------

-- Un renglon de servicio o de consumo de la orden puede nacer de una
-- cotizacion. Si nace de una, esa cotizacion tiene que estar APROBADA: es la
-- traduccion literal de "sin fila de aprobacion, el renglon no pasa a
-- ejecucion".
ALTER TABLE orden_servicio
    ADD COLUMN IF NOT EXISTS cotizacion_renglon_id bigint REFERENCES cotizacion_renglon(id);
ALTER TABLE orden_consumo
    ADD COLUMN IF NOT EXISTS cotizacion_renglon_id bigint REFERENCES cotizacion_renglon(id);

CREATE OR REPLACE FUNCTION fn_renglon_necesita_aprobacion() RETURNS trigger AS $$
DECLARE
    v_estado text;
BEGIN
    IF NEW.cotizacion_renglon_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT c.estado INTO v_estado
      FROM cotizacion_renglon r
      JOIN cotizacion c ON c.id = r.cotizacion_id
     WHERE r.id = NEW.cotizacion_renglon_id;

    IF v_estado IS DISTINCT FROM 'APROBADA' THEN
        RAISE EXCEPTION 'El renglon cotizado no pasa a ejecucion sin la aprobacion del cliente (cotizacion %)', COALESCE(v_estado, 'inexistente')
            USING ERRCODE = 'P0001';
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_orden_servicio_aprobacion ON orden_servicio;
CREATE TRIGGER tg_orden_servicio_aprobacion
    BEFORE INSERT ON orden_servicio
    FOR EACH ROW EXECUTE FUNCTION fn_renglon_necesita_aprobacion();

DROP TRIGGER IF EXISTS tg_orden_consumo_aprobacion ON orden_consumo;
CREATE TRIGGER tg_orden_consumo_aprobacion
    BEFORE INSERT ON orden_consumo
    FOR EACH ROW EXECUTE FUNCTION fn_renglon_necesita_aprobacion();

COMMIT;

-- Comprobacion de lo aplicado.
SELECT 'orden_hallazgo'       AS objeto, to_regclass('orden_hallazgo')::text       AS valor
UNION ALL SELECT 'cotizacion',          to_regclass('cotizacion')::text
UNION ALL SELECT 'cotizacion_renglon',  to_regclass('cotizacion_renglon')::text
UNION ALL SELECT 'cotizacion_decision', to_regclass('cotizacion_decision')::text
UNION ALL SELECT 'trigger version',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_cotizacion_version')
UNION ALL SELECT 'trigger estado',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_cotizacion_estado')
UNION ALL SELECT 'trigger decision inmutable',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_cotizacion_decision_inmutable')
UNION ALL SELECT 'orden_servicio.cotizacion_renglon_id',
       (SELECT column_name FROM information_schema.columns
         WHERE table_name = 'orden_servicio' AND column_name = 'cotizacion_renglon_id');
