-- ============================================================================
-- LubriTech · 00_nucleo.sql
-- Nucleo de la plataforma: planes, empresas, sedes, roles, permisos,
-- identidad del personal, identidad del cliente, bitacora e idempotencia.
--
-- Aplicar SIEMPRE asi (el PGCLIENTENCODING no es opcional):
--   PGCLIENTENCODING=UTF8 "C:/Program Files/PostgreSQL/18/bin/psql.exe" \
--     -h localhost -U postgres -d LubriTech -v ON_ERROR_STOP=1 -P pager=off \
--     -f scripts/00_nucleo.sql
--
-- Convenciones:
--   * snake_case, timestamptz SIEMPRE en UTC real.
--   * Enumerados como text + CHECK, nunca varchar(n) corto: un literal mas largo
--     que la columna es un bug que solo aparece el dia que alguien lo usa.
--   * Idempotente: se puede volver a correr sin romper nada.
-- ============================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- 1. PLANES (el plan es dato, no codigo)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS plan (
    id              serial PRIMARY KEY,
    codigo          text        NOT NULL UNIQUE,
    nombre          text        NOT NULL,
    precio_mensual  numeric(10,2) NOT NULL DEFAULT 0 CHECK (precio_mensual >= 0),
    max_sedes       int         NOT NULL DEFAULT 1 CHECK (max_sedes >= 0),   -- 0 = sin tope
    orden           int         NOT NULL DEFAULT 0,
    activo          boolean     NOT NULL DEFAULT true,
    creado_en       timestamptz NOT NULL DEFAULT now()
);

-- Que funciones habilita cada plan. El permiso efectivo es la interseccion
-- entre lo que el rol puede y lo que el plan habilita.
CREATE TABLE IF NOT EXISTS plan_funcion (
    plan_id   int  NOT NULL REFERENCES plan(id) ON DELETE CASCADE,
    funcion   text NOT NULL,
    PRIMARY KEY (plan_id, funcion)
);

-- Cupo de usuarios por rol dentro del plan. 0 = sin tope.
CREATE TABLE IF NOT EXISTS plan_rol_cupo (
    plan_id int NOT NULL REFERENCES plan(id) ON DELETE CASCADE,
    rol_id  int NOT NULL,
    cupo    int NOT NULL DEFAULT 0 CHECK (cupo >= 0),
    PRIMARY KEY (plan_id, rol_id)
);

-- ---------------------------------------------------------------------------
-- 2. EMPRESAS (el tenant) Y SUSCRIPCION
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS empresa (
    id                serial PRIMARY KEY,
    ruc               text        NOT NULL UNIQUE CHECK (ruc ~ '^[0-9]{11}$'),
    razon_social      text        NOT NULL,
    nombre_comercial  text        NOT NULL,
    direccion         text,
    telefono          text,
    email             text,
    logo_ruta         text,
    plan_id           int         NOT NULL REFERENCES plan(id),
    activo            boolean     NOT NULL DEFAULT true,
    -- Hasta cuando esta pagado el servicio. Vencido = corte de acceso.
    servicio_hasta    date        NOT NULL,
    creado_en         timestamptz NOT NULL DEFAULT now(),
    actualizado_en    timestamptz
);

COMMENT ON COLUMN empresa.servicio_hasta IS
    'Ningun dia de servicio se regala ni se cobra dos veces: el corte se calcula contra esta fecha.';

CREATE TABLE IF NOT EXISTS suscripcion (
    id            bigserial PRIMARY KEY,
    empresa_id    int         NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    plan_id       int         NOT NULL REFERENCES plan(id),
    desde         date        NOT NULL,
    hasta         date        NOT NULL,
    monto         numeric(10,2) NOT NULL CHECK (monto >= 0),
    estado        text        NOT NULL DEFAULT 'PAGADA'
                              CHECK (estado IN ('PENDIENTE','PAGADA','ANULADA')),
    referencia    text,
    registrado_en timestamptz NOT NULL DEFAULT now(),
    CHECK (hasta >= desde)
);

CREATE INDEX IF NOT EXISTS ix_suscripcion_empresa ON suscripcion(empresa_id, desde DESC);

-- ---------------------------------------------------------------------------
-- 3. SEDES
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS sede (
    id          serial PRIMARY KEY,
    empresa_id  int         NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    nombre      text        NOT NULL,
    direccion   text,
    telefono    text,
    es_principal boolean    NOT NULL DEFAULT false,
    activo      boolean     NOT NULL DEFAULT true,
    creado_en   timestamptz NOT NULL DEFAULT now(),
    UNIQUE (empresa_id, nombre)
);

CREATE INDEX IF NOT EXISTS ix_sede_empresa ON sede(empresa_id);

-- Una sola sede principal por empresa.
CREATE UNIQUE INDEX IF NOT EXISTS ux_sede_principal
    ON sede(empresa_id) WHERE es_principal;

-- ---------------------------------------------------------------------------
-- 4. ROLES Y PERMISOS
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS rol (
    id          int PRIMARY KEY,
    nombre      text NOT NULL UNIQUE,
    descripcion text,
    -- Un rol de taller no puede recibir permisos de dinero.
    maneja_dinero boolean NOT NULL DEFAULT false
);

CREATE TABLE IF NOT EXISTS permiso (
    codigo      text PRIMARY KEY,
    nombre      text NOT NULL,
    modulo      text NOT NULL,
    es_dinero   boolean NOT NULL DEFAULT false,
    -- Funcion del plan que ademas debe estar habilitada. NULL = no depende del plan.
    funcion_requerida text
);

CREATE TABLE IF NOT EXISTS rol_permiso (
    rol_id        int  NOT NULL REFERENCES rol(id) ON DELETE CASCADE,
    permiso_codigo text NOT NULL REFERENCES permiso(codigo) ON DELETE CASCADE,
    PRIMARY KEY (rol_id, permiso_codigo)
);

-- Candado: no se le puede dar un permiso de dinero a un rol que no maneja dinero.
CREATE OR REPLACE FUNCTION fn_rol_permiso_dinero() RETURNS trigger AS $$
DECLARE
    v_es_dinero     boolean;
    v_maneja_dinero boolean;
BEGIN
    SELECT es_dinero INTO v_es_dinero FROM permiso WHERE codigo = NEW.permiso_codigo;
    SELECT maneja_dinero INTO v_maneja_dinero FROM rol WHERE id = NEW.rol_id;

    IF v_es_dinero AND NOT v_maneja_dinero THEN
        RAISE EXCEPTION 'El rol % no maneja dinero: no puede recibir el permiso %',
            NEW.rol_id, NEW.permiso_codigo
            USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_rol_permiso_dinero ON rol_permiso;
CREATE TRIGGER tg_rol_permiso_dinero
    BEFORE INSERT OR UPDATE ON rol_permiso
    FOR EACH ROW EXECUTE FUNCTION fn_rol_permiso_dinero();

-- ---------------------------------------------------------------------------
-- 5. IDENTIDAD DEL PERSONAL
--    El SuperAdmin NO tiene empresa. Todo lo demas si, obligatoriamente.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS usuario (
    id                 serial PRIMARY KEY,
    empresa_id         int         REFERENCES empresa(id) ON DELETE RESTRICT,
    sede_id            int         REFERENCES sede(id)    ON DELETE RESTRICT,
    rol_id             int         NOT NULL REFERENCES rol(id),
    dni                text        CHECK (dni IS NULL OR dni ~ '^[0-9]{8}$'),
    nombres            text        NOT NULL,
    apellidos          text        NOT NULL,
    email              text        NOT NULL,
    telefono           text,
    password_hash      text        NOT NULL,
    -- Sube de a uno para invalidar al instante todos los tokens del usuario.
    sesion_version     int         NOT NULL DEFAULT 1,
    intentos_fallidos  int         NOT NULL DEFAULT 0,
    bloqueado_hasta    timestamptz,
    debe_cambiar_clave boolean     NOT NULL DEFAULT false,
    biometria_activa   boolean     NOT NULL DEFAULT false,
    activo             boolean     NOT NULL DEFAULT true,
    creado_en          timestamptz NOT NULL DEFAULT now(),
    ultimo_acceso      timestamptz,

    -- El SuperAdmin es el unico sin empresa. Nadie mas.
    CONSTRAINT ck_usuario_superadmin_sin_empresa CHECK (
        (rol_id = 0 AND empresa_id IS NULL AND sede_id IS NULL)
        OR (rol_id > 0 AND empresa_id IS NOT NULL)
    )
);

-- Email del personal: UNICO GLOBAL, no por empresa.
-- Por que: el login es por email y nada mas. Si dos empresas pudieran tener
-- el mismo email, el login seria ambiguo y habria que pedir la empresa en la
-- pantalla de entrada. La misma persona si puede ser cliente del portal con
-- ese email: son dos mundos distintos (ver cliente_acceso).
DROP INDEX IF EXISTS ux_usuario_email_empresa;
DROP INDEX IF EXISTS ux_usuario_email_sa;
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuario_email ON usuario(lower(email));

-- Un solo Admin (propietario) por empresa.
CREATE UNIQUE INDEX IF NOT EXISTS ux_usuario_admin_unico
    ON usuario(empresa_id) WHERE rol_id = 1 AND activo;

CREATE INDEX IF NOT EXISTS ix_usuario_empresa ON usuario(empresa_id);
CREATE INDEX IF NOT EXISTS ix_usuario_sede    ON usuario(sede_id);

-- La sede del usuario tiene que ser de su propia empresa.
CREATE OR REPLACE FUNCTION fn_usuario_sede_coherente() RETURNS trigger AS $$
DECLARE v_empresa_sede int;
BEGIN
    IF NEW.sede_id IS NULL THEN RETURN NEW; END IF;

    SELECT empresa_id INTO v_empresa_sede FROM sede WHERE id = NEW.sede_id;
    IF v_empresa_sede IS DISTINCT FROM NEW.empresa_id THEN
        RAISE EXCEPTION 'La sede % no pertenece a la empresa %', NEW.sede_id, NEW.empresa_id
            USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_usuario_sede_coherente ON usuario;
CREATE TRIGGER tg_usuario_sede_coherente
    BEFORE INSERT OR UPDATE OF sede_id, empresa_id ON usuario
    FOR EACH ROW EXECUTE FUNCTION fn_usuario_sede_coherente();

-- Candado de cupo por plan, con advisory lock para que dos altas simultaneas
-- no pasen el tope entre las dos.
CREATE OR REPLACE FUNCTION fn_limite_plan_usuario() RETURNS trigger AS $$
DECLARE
    v_plan_id int;
    v_cupo    int;
    v_usados  int;
BEGIN
    -- El SuperAdmin no pertenece a ninguna empresa: el cupo del plan no le aplica.
    -- Si viene con empresa, de eso se queja el CHECK, que da un mensaje claro.
    IF NEW.rol_id = 0 THEN RETURN NEW; END IF;

    IF NEW.empresa_id IS NULL OR NOT NEW.activo THEN RETURN NEW; END IF;

    PERFORM pg_advisory_xact_lock(hashtext('cupo_usuario:' || NEW.empresa_id || ':' || NEW.rol_id));

    SELECT plan_id INTO v_plan_id FROM empresa WHERE id = NEW.empresa_id;
    SELECT cupo INTO v_cupo FROM plan_rol_cupo WHERE plan_id = v_plan_id AND rol_id = NEW.rol_id;

    -- Sin fila de cupo = el plan no habilita ese rol.
    IF v_cupo IS NULL THEN
        RAISE EXCEPTION 'El plan contratado no habilita el rol %', NEW.rol_id
            USING ERRCODE = 'P0001';
    END IF;

    IF v_cupo = 0 THEN RETURN NEW; END IF;   -- sin tope

    SELECT count(*) INTO v_usados
      FROM usuario
     WHERE empresa_id = NEW.empresa_id AND rol_id = NEW.rol_id AND activo
       AND id IS DISTINCT FROM NEW.id;

    IF v_usados >= v_cupo THEN
        RAISE EXCEPTION 'El plan permite % usuario(s) con el rol %, y ya hay %',
            v_cupo, NEW.rol_id, v_usados
            USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_limite_plan_usuario ON usuario;
CREATE TRIGGER tg_limite_plan_usuario
    BEFORE INSERT OR UPDATE OF rol_id, activo, empresa_id ON usuario
    FOR EACH ROW EXECUTE FUNCTION fn_limite_plan_usuario();

-- Mismo candado para sedes.
CREATE OR REPLACE FUNCTION fn_limite_plan_sede() RETURNS trigger AS $$
DECLARE
    v_max    int;
    v_usados int;
BEGIN
    IF NOT NEW.activo THEN RETURN NEW; END IF;

    PERFORM pg_advisory_xact_lock(hashtext('cupo_sede:' || NEW.empresa_id));

    SELECT p.max_sedes INTO v_max
      FROM empresa e JOIN plan p ON p.id = e.plan_id
     WHERE e.id = NEW.empresa_id;

    IF v_max = 0 THEN RETURN NEW; END IF;

    SELECT count(*) INTO v_usados
      FROM sede WHERE empresa_id = NEW.empresa_id AND activo
        AND id IS DISTINCT FROM NEW.id;

    IF v_usados >= v_max THEN
        RAISE EXCEPTION 'El plan permite % sede(s) y ya hay %', v_max, v_usados
            USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_limite_plan_sede ON sede;
CREATE TRIGGER tg_limite_plan_sede
    BEFORE INSERT OR UPDATE OF activo, empresa_id ON sede
    FOR EACH ROW EXECUTE FUNCTION fn_limite_plan_sede();

-- Refresh rotativo del personal. Reuso de un token ya rotado = robo: se revoca todo.
CREATE TABLE IF NOT EXISTS usuario_refresh (
    id            bigserial PRIMARY KEY,
    usuario_id    int         NOT NULL REFERENCES usuario(id) ON DELETE CASCADE,
    token_hash    text        NOT NULL UNIQUE,
    emitido_en    timestamptz NOT NULL DEFAULT now(),
    expira_en     timestamptz NOT NULL,
    usado_en      timestamptz,
    revocado_en   timestamptz,
    reemplazado_por bigint    REFERENCES usuario_refresh(id),
    dispositivo_id bigint,
    ip            text,
    agente        text
);

CREATE INDEX IF NOT EXISTS ix_usuario_refresh_usuario ON usuario_refresh(usuario_id);

-- ---------------------------------------------------------------------------
-- 6. IDENTIDAD DEL CLIENTE
--    Mundo aparte. Un cliente NO es un usuario y no puede tener rol del ERP.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS cliente (
    id            bigserial PRIMARY KEY,
    empresa_id    int         NOT NULL REFERENCES empresa(id) ON DELETE RESTRICT,
    tipo          text        NOT NULL DEFAULT 'PARTICULAR'
                              CHECK (tipo IN ('PARTICULAR','EMPRESA')),
    tipo_documento text       NOT NULL DEFAULT 'DNI'
                              CHECK (tipo_documento IN ('DNI','RUC','CE','PASAPORTE','SIN_DOCUMENTO')),
    documento     text,
    nombres       text,
    apellidos     text,
    razon_social  text,
    telefono      text,
    email         text,
    direccion     text,
    activo        boolean     NOT NULL DEFAULT true,
    creado_en     timestamptz NOT NULL DEFAULT now(),
    actualizado_en timestamptz,

    -- Un particular tiene nombre; una empresa tiene razon social.
    CONSTRAINT ck_cliente_nombre CHECK (
        (tipo = 'PARTICULAR' AND nombres IS NOT NULL)
        OR (tipo = 'EMPRESA' AND razon_social IS NOT NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cliente_documento
    ON cliente(empresa_id, tipo_documento, documento)
    WHERE documento IS NOT NULL AND tipo_documento <> 'SIN_DOCUMENTO';

CREATE INDEX IF NOT EXISTS ix_cliente_empresa ON cliente(empresa_id);

CREATE TABLE IF NOT EXISTS cliente_acceso (
    id                 bigserial PRIMARY KEY,
    cliente_id         bigint      NOT NULL REFERENCES cliente(id) ON DELETE CASCADE,
    email              text        NOT NULL,
    password_hash      text        NOT NULL,
    sesion_version     int         NOT NULL DEFAULT 1,
    intentos_fallidos  int         NOT NULL DEFAULT 0,
    bloqueado_hasta    timestamptz,
    email_verificado   boolean     NOT NULL DEFAULT false,
    biometria_activa   boolean     NOT NULL DEFAULT false,
    activo             boolean     NOT NULL DEFAULT true,
    creado_en          timestamptz NOT NULL DEFAULT now(),
    ultimo_acceso      timestamptz
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_cliente_acceso_email ON cliente_acceso(lower(email));
CREATE UNIQUE INDEX IF NOT EXISTS ux_cliente_acceso_cliente ON cliente_acceso(cliente_id);

CREATE TABLE IF NOT EXISTS cliente_refresh (
    id            bigserial PRIMARY KEY,
    acceso_id     bigint      NOT NULL REFERENCES cliente_acceso(id) ON DELETE CASCADE,
    token_hash    text        NOT NULL UNIQUE,
    emitido_en    timestamptz NOT NULL DEFAULT now(),
    expira_en     timestamptz NOT NULL,
    usado_en      timestamptz,
    revocado_en   timestamptz,
    reemplazado_por bigint    REFERENCES cliente_refresh(id),
    dispositivo_id bigint,
    ip            text,
    agente        text
);

CREATE INDEX IF NOT EXISTS ix_cliente_refresh_acceso ON cliente_refresh(acceso_id);

-- Recuperacion de contrasena del cliente (token de un solo uso).
CREATE TABLE IF NOT EXISTS cliente_recuperacion (
    id          bigserial PRIMARY KEY,
    acceso_id   bigint      NOT NULL REFERENCES cliente_acceso(id) ON DELETE CASCADE,
    token_hash  text        NOT NULL UNIQUE,
    emitido_en  timestamptz NOT NULL DEFAULT now(),
    expira_en   timestamptz NOT NULL,
    usado_en    timestamptz
);

-- ---------------------------------------------------------------------------
-- 7. DISPOSITIVOS REGISTRADOS (push y cierre remoto de sesiones)
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS dispositivo (
    id              bigserial PRIMARY KEY,
    -- Exactamente uno de los dos: personal o cliente.
    usuario_id      int         REFERENCES usuario(id) ON DELETE CASCADE,
    acceso_id       bigint      REFERENCES cliente_acceso(id) ON DELETE CASCADE,
    aplicacion      text        NOT NULL
                                CHECK (aplicacion IN ('OPERACIONES','CLIENTES','ERP_WEB')),
    plataforma      text        NOT NULL
                                CHECK (plataforma IN ('ANDROID','IOS','WEB')),
    token_push      text,
    proveedor_push  text        CHECK (proveedor_push IN ('FCM','APNS')),
    modelo          text,
    version_app     text,
    version_os      text,
    identificador   text        NOT NULL,   -- id estable del dispositivo
    activo          boolean     NOT NULL DEFAULT true,
    registrado_en   timestamptz NOT NULL DEFAULT now(),
    ultimo_visto    timestamptz,
    cerrado_en      timestamptz,

    CONSTRAINT ck_dispositivo_dueno CHECK (
        (usuario_id IS NOT NULL AND acceso_id IS NULL)
        OR (usuario_id IS NULL AND acceso_id IS NOT NULL)
    )
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_dispositivo_usuario
    ON dispositivo(usuario_id, identificador) WHERE usuario_id IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_dispositivo_cliente
    ON dispositivo(acceso_id, identificador) WHERE acceso_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS ix_dispositivo_push
    ON dispositivo(token_push) WHERE token_push IS NOT NULL AND activo;

-- ---------------------------------------------------------------------------
-- 8. BITACORA — rastro de quien hizo que. INMUTABLE.
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS bitacora (
    id            bigserial PRIMARY KEY,
    empresa_id    int,
    usuario_id    int,
    acceso_id     bigint,
    sede_id       int,
    accion        text        NOT NULL,
    entidad       text        NOT NULL,
    entidad_id    text,
    detalle       jsonb,
    ip            text,
    agente        text,
    origen        text        NOT NULL DEFAULT 'ERP_WEB'
                              CHECK (origen IN ('ERP_WEB','OPERACIONES','CLIENTES','SISTEMA')),
    ocurrido_en   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_bitacora_empresa  ON bitacora(empresa_id, ocurrido_en DESC);
CREATE INDEX IF NOT EXISTS ix_bitacora_entidad  ON bitacora(entidad, entidad_id);
CREATE INDEX IF NOT EXISTS ix_bitacora_usuario  ON bitacora(usuario_id, ocurrido_en DESC);

CREATE OR REPLACE FUNCTION fn_solo_insercion() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'La tabla % es inmutable: solo admite INSERT (intento de %)',
        TG_TABLE_NAME, TG_OP
        USING ERRCODE = 'P0001';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_bitacora_inmutable ON bitacora;
CREATE TRIGGER tg_bitacora_inmutable
    BEFORE UPDATE OR DELETE ON bitacora
    FOR EACH ROW EXECUTE FUNCTION fn_solo_insercion();

DROP TRIGGER IF EXISTS tg_suscripcion_inmutable ON suscripcion;
CREATE TRIGGER tg_suscripcion_inmutable
    BEFORE DELETE ON suscripcion
    FOR EACH ROW EXECUTE FUNCTION fn_solo_insercion();

-- ---------------------------------------------------------------------------
-- 9. IDEMPOTENCIA — lo que llega del celular puede llegar dos veces
-- ---------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS operacion_idempotente (
    clave          text        PRIMARY KEY,
    empresa_id     int,
    usuario_id     int,
    acceso_id      bigint,
    operacion      text        NOT NULL,
    huella_cuerpo  text        NOT NULL,   -- sha256 del cuerpo de la peticion
    estado_http    int         NOT NULL,
    respuesta      jsonb,
    creado_en      timestamptz NOT NULL DEFAULT now(),
    expira_en      timestamptz NOT NULL DEFAULT now() + interval '7 days'
);

CREATE INDEX IF NOT EXISTS ix_operacion_idempotente_expira
    ON operacion_idempotente(expira_en);

COMMENT ON TABLE operacion_idempotente IS
    'Misma clave + misma huella = se devuelve la respuesta guardada. Misma clave + huella distinta = 409.';

COMMIT;
