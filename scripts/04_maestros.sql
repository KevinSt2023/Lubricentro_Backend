-- ============================================================================
-- LubriTech · scripts/04_maestros.sql  —  Fase 2: maestros del taller
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f scripts/04_maestros.sql
--
-- Agrega: vehiculos, catalogo de servicios, almacen (productos), stock por
-- sede y kardex con sus candados. Personal y clientes YA tienen su tabla
-- (usuario, cliente) del nucleo: esta fase solo les pone pantalla, no tabla.
--
-- REGLA DE ORO que esta fase deja amarrada EN LA BASE:
--   * el stock SOLO se mueve insertando en kardex; un trigger mantiene
--     producto_stock en sincronia. No hay forma de tocar el stock sin dejar
--     el movimiento escrito. (memoria: "TODO movimiento afecta kardex+inventario")
--   * el kardex es inmutable: lo que se movio, se movio.
--   * el kilometraje de un vehiculo nunca baja.
--
-- Idempotente: CREATE TABLE IF NOT EXISTS, y los triggers se recrean.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ---------------------------------------------------------------------------
-- 1. VEHICULOS
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS vehiculo (
    id            bigserial PRIMARY KEY,
    empresa_id    int         NOT NULL REFERENCES empresa(id)  ON DELETE RESTRICT,
    cliente_id    bigint      NOT NULL REFERENCES cliente(id)  ON DELETE RESTRICT,
    placa         text        NOT NULL,
    marca         text        NOT NULL,
    modelo        text        NOT NULL,
    anio          int         CHECK (anio IS NULL OR anio BETWEEN 1900 AND 2100),
    tipo          text        NOT NULL DEFAULT 'AUTO'
                              CHECK (tipo IN ('AUTO','CAMIONETA','SUV','CAMION','BUS','MOTO','OTRO')),
    color         text,
    vin           text,
    motor         text,
    combustible   text        CHECK (combustible IS NULL OR combustible IN
                              ('GASOLINA','DIESEL','GLP','GNV','ELECTRICO','HIBRIDO')),
    transmision   text        CHECK (transmision IS NULL OR transmision IN ('MANUAL','AUTOMATICA','CVT')),
    kilometraje   int         NOT NULL DEFAULT 0 CHECK (kilometraje >= 0),
    notas         text,
    activo        boolean     NOT NULL DEFAULT true,
    creado_en     timestamptz NOT NULL DEFAULT now(),
    actualizado_en timestamptz
);

-- La placa identifica al vehiculo dentro de la empresa. Puede repetirse entre
-- empresas distintas (son talleres distintos), pero no dentro de una.
CREATE UNIQUE INDEX IF NOT EXISTS ux_vehiculo_placa
    ON vehiculo (empresa_id, upper(placa));

CREATE INDEX IF NOT EXISTS ix_vehiculo_cliente ON vehiculo (cliente_id);
CREATE INDEX IF NOT EXISTS ix_vehiculo_empresa ON vehiculo (empresa_id) WHERE activo;

-- El kilometraje no retrocede: un auto no desanda lo andado. Corregir a la
-- baja un error de tecleo es trabajo de un ajuste explicito, no de una
-- edicion comun, asi que aqui se bloquea de plano.
CREATE OR REPLACE FUNCTION fn_vehiculo_km_no_baja() RETURNS trigger AS $$
BEGIN
    IF NEW.kilometraje < OLD.kilometraje THEN
        RAISE EXCEPTION 'El kilometraje no puede bajar: estaba en % y se intento poner %',
            OLD.kilometraje, NEW.kilometraje
            USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_vehiculo_km_no_baja ON vehiculo;
CREATE TRIGGER tg_vehiculo_km_no_baja
    BEFORE UPDATE ON vehiculo
    FOR EACH ROW EXECUTE FUNCTION fn_vehiculo_km_no_baja();

-- ---------------------------------------------------------------------------
-- 2. CATALOGO DE SERVICIOS
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS servicio (
    id            bigserial PRIMARY KEY,
    empresa_id    int         NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    codigo        text        NOT NULL,
    nombre        text        NOT NULL,
    categoria     text,
    descripcion   text,
    precio        numeric(10,2) NOT NULL DEFAULT 0 CHECK (precio >= 0),
    duracion_min  int         CHECK (duracion_min IS NULL OR duracion_min >= 0),
    activo        boolean     NOT NULL DEFAULT true,
    creado_en     timestamptz NOT NULL DEFAULT now(),
    actualizado_en timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_servicio_codigo
    ON servicio (empresa_id, upper(codigo));

CREATE INDEX IF NOT EXISTS ix_servicio_empresa ON servicio (empresa_id) WHERE activo;

-- ---------------------------------------------------------------------------
-- 3. ALMACEN: PRODUCTOS (repuestos e insumos)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS producto (
    id            bigserial PRIMARY KEY,
    empresa_id    int         NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    codigo        text        NOT NULL,
    codigo_barras text,
    nombre        text        NOT NULL,
    categoria     text,
    tipo          text        NOT NULL DEFAULT 'REPUESTO'
                              CHECK (tipo IN ('REPUESTO','INSUMO','ACEITE','FILTRO','LLANTA','OTRO')),
    unidad        text        NOT NULL DEFAULT 'UND'
                              CHECK (unidad IN ('UND','LT','GAL','KG','JGO','MT','CAJA')),
    marca         text,
    precio_venta  numeric(10,2) NOT NULL DEFAULT 0 CHECK (precio_venta >= 0),
    stock_minimo  numeric(12,3) NOT NULL DEFAULT 0 CHECK (stock_minimo >= 0),
    -- Un servicio consumible (p.ej. "mano de obra" vendida como insumo) puede
    -- no controlar stock: entonces su salida nunca se frena por falta.
    controla_stock boolean    NOT NULL DEFAULT true,
    activo        boolean     NOT NULL DEFAULT true,
    creado_en     timestamptz NOT NULL DEFAULT now(),
    actualizado_en timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_producto_codigo
    ON producto (empresa_id, upper(codigo));

-- El codigo de barras, si lo tiene, es unico dentro de la empresa: el lector
-- debe llegar a un solo producto.
CREATE UNIQUE INDEX IF NOT EXISTS ux_producto_barras
    ON producto (empresa_id, codigo_barras) WHERE codigo_barras IS NOT NULL;

CREATE INDEX IF NOT EXISTS ix_producto_empresa ON producto (empresa_id) WHERE activo;

-- ---------------------------------------------------------------------------
-- 4. STOCK POR SEDE  (foto actual; la pelicula es el kardex)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS producto_stock (
    producto_id   bigint        NOT NULL REFERENCES producto(id) ON DELETE RESTRICT,
    sede_id       int           NOT NULL REFERENCES sede(id)     ON DELETE RESTRICT,
    cantidad      numeric(12,3) NOT NULL DEFAULT 0 CHECK (cantidad >= 0),
    -- Costo promedio ponderado: con cada ingreso se recalcula; en la salida
    -- es el costo con el que se descarga la mercaderia.
    costo_promedio numeric(12,4) NOT NULL DEFAULT 0 CHECK (costo_promedio >= 0),
    actualizado_en timestamptz  NOT NULL DEFAULT now(),
    PRIMARY KEY (producto_id, sede_id)
);

CREATE INDEX IF NOT EXISTS ix_stock_sede ON producto_stock (sede_id);

-- producto_stock NO se edita a mano desde la aplicacion: su unica fuente
-- legitima es el trigger del kardex. Una app que intente un UPDATE directo se
-- lo encuentra cerrado; el trigger del kardex pasa poniendo una marca de
-- sesion que este candado reconoce.
CREATE OR REPLACE FUNCTION fn_stock_solo_por_kardex() RETURNS trigger AS $$
BEGIN
    IF current_setting('lubritech.aplicando_kardex', true) = '1' THEN
        RETURN NEW;    -- viene del trigger del kardex: permitido
    END IF;
    RAISE EXCEPTION 'producto_stock no se edita directo: todo movimiento pasa por el kardex'
        USING ERRCODE = 'P0001';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_stock_solo_por_kardex ON producto_stock;
CREATE TRIGGER tg_stock_solo_por_kardex
    BEFORE INSERT OR UPDATE OR DELETE ON producto_stock
    FOR EACH ROW EXECUTE FUNCTION fn_stock_solo_por_kardex();

-- ---------------------------------------------------------------------------
-- 5. KARDEX  (el libro mayor del almacen; inmutable)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS kardex (
    id            bigserial PRIMARY KEY,
    empresa_id    int         NOT NULL REFERENCES empresa(id)  ON DELETE RESTRICT,
    producto_id   bigint      NOT NULL REFERENCES producto(id) ON DELETE RESTRICT,
    sede_id       int         NOT NULL REFERENCES sede(id)     ON DELETE RESTRICT,
    tipo          text        NOT NULL
                              CHECK (tipo IN ('INGRESO','SALIDA','AJUSTE_POS','AJUSTE_NEG')),
    -- Siempre positiva: el signo lo pone el tipo. Asi el kardex se lee sin
    -- tener que interpretar un negativo.
    cantidad      numeric(12,3) NOT NULL CHECK (cantidad > 0),
    costo_unitario numeric(12,4) NOT NULL DEFAULT 0 CHECK (costo_unitario >= 0),
    -- Saldo del producto EN ESA SEDE despues de aplicar este movimiento. Lo
    -- calcula el trigger; no se confia en lo que mande la aplicacion.
    saldo         numeric(12,3) NOT NULL DEFAULT 0,
    referencia    text,                 -- documento o motivo corto
    motivo        text,
    usuario_id    int           REFERENCES usuario(id),
    creado_en     timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_kardex_producto_sede ON kardex (producto_id, sede_id, id);
CREATE INDEX IF NOT EXISTS ix_kardex_empresa_fecha ON kardex (empresa_id, creado_en DESC);

-- El corazon de la fase: un movimiento de kardex ES el movimiento de stock.
-- Al insertar una fila, este trigger:
--   1. lee (o crea) el stock de ese producto en esa sede,
--   2. valida que una salida no deje el stock en negativo (si controla_stock),
--   3. calcula el saldo nuevo y lo graba en NEW.saldo,
--   4. en un ingreso, recalcula el costo promedio ponderado,
--   5. upsertea producto_stock con la marca de sesion que lo deja pasar.
-- No hay camino alterno: sin kardex no hay stock, y el stock nunca miente
-- respecto del kardex porque es el kardex quien lo escribe.
CREATE OR REPLACE FUNCTION fn_kardex_aplica() RETURNS trigger AS $$
DECLARE
    v_stock   numeric(12,3);
    v_costo   numeric(12,4);
    v_controla boolean;
    v_signo   int;
    v_nuevo   numeric(12,3);
    v_ingreso boolean;
BEGIN
    SELECT controla_stock INTO v_controla FROM producto WHERE id = NEW.producto_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto % no existe', NEW.producto_id USING ERRCODE = 'P0001';
    END IF;

    v_ingreso := NEW.tipo IN ('INGRESO','AJUSTE_POS');
    v_signo   := CASE WHEN v_ingreso THEN 1 ELSE -1 END;

    -- Toma el stock actual de la sede; si no existe, arranca en cero.
    SELECT cantidad, costo_promedio INTO v_stock, v_costo
      FROM producto_stock
     WHERE producto_id = NEW.producto_id AND sede_id = NEW.sede_id
     FOR UPDATE;

    IF NOT FOUND THEN
        v_stock := 0; v_costo := 0;
    END IF;

    v_nuevo := v_stock + v_signo * NEW.cantidad;

    IF v_nuevo < 0 AND v_controla THEN
        RAISE EXCEPTION 'Stock insuficiente: hay % y se intenta sacar %',
            v_stock, NEW.cantidad USING ERRCODE = 'P0001';
    END IF;

    -- Un producto que no controla stock no baja de cero: queda en cero.
    IF v_nuevo < 0 THEN v_nuevo := 0; END IF;

    -- Costo promedio ponderado: solo los ingresos lo mueven. En la salida se
    -- conserva, y es el costo con el que sale la mercaderia.
    IF v_ingreso AND NEW.costo_unitario > 0 THEN
        v_costo := CASE WHEN v_nuevo = 0 THEN NEW.costo_unitario
                        ELSE ((v_stock * v_costo) + (NEW.cantidad * NEW.costo_unitario)) / v_nuevo END;
    END IF;

    NEW.saldo := v_nuevo;

    -- Marca de sesion: el candado de producto_stock la reconoce y deja pasar.
    PERFORM set_config('lubritech.aplicando_kardex', '1', true);

    INSERT INTO producto_stock (producto_id, sede_id, cantidad, costo_promedio, actualizado_en)
    VALUES (NEW.producto_id, NEW.sede_id, v_nuevo, v_costo, now())
    ON CONFLICT (producto_id, sede_id) DO UPDATE
        SET cantidad = EXCLUDED.cantidad,
            costo_promedio = EXCLUDED.costo_promedio,
            actualizado_en = now();

    PERFORM set_config('lubritech.aplicando_kardex', '0', true);

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_kardex_aplica ON kardex;
CREATE TRIGGER tg_kardex_aplica
    BEFORE INSERT ON kardex
    FOR EACH ROW EXECUTE FUNCTION fn_kardex_aplica();

-- El kardex es historia: ni se edita ni se borra.
CREATE OR REPLACE FUNCTION fn_tabla_inmutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'La tabla % es inmutable: solo admite INSERT (intento de %)',
        TG_TABLE_NAME, TG_OP USING ERRCODE = 'P0001';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_kardex_inmutable ON kardex;
CREATE TRIGGER tg_kardex_inmutable
    BEFORE UPDATE OR DELETE ON kardex
    FOR EACH ROW EXECUTE FUNCTION fn_tabla_inmutable();

COMMIT;

-- Comprobacion de lo aplicado.
SELECT 'vehiculo'        AS objeto, to_regclass('vehiculo')::text        AS existe
UNION ALL SELECT 'servicio',        to_regclass('servicio')::text
UNION ALL SELECT 'producto',        to_regclass('producto')::text
UNION ALL SELECT 'producto_stock',  to_regclass('producto_stock')::text
UNION ALL SELECT 'kardex',          to_regclass('kardex')::text
UNION ALL SELECT 'trigger kardex aplica',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_kardex_aplica')
UNION ALL SELECT 'trigger kardex inmutable',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_kardex_inmutable')
UNION ALL SELECT 'trigger km no baja',
       (SELECT tgname FROM pg_trigger WHERE tgname = 'tg_vehiculo_km_no_baja');
