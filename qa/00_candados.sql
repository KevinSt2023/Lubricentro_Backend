-- ============================================================================
-- LubriTech · qa/00_candados.sql
-- Comprueba que los candados del nucleo MUERDEN de verdad.
-- Cada bloque intenta la operacion prohibida y espera la excepcion.
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f qa/00_candados.sql
--
-- Resultado esperado: todas las lineas dicen PASA, y el cuadre final en cero.
-- Todo corre dentro de una transaccion que termina en ROLLBACK: no deja
-- ni una fila de prueba, ni siquiera en las tablas inmutables.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    v_empresa   int;
    v_sede      int;
    v_sede_otra int;
    v_otra_emp  int;
    v_bit       bigint;
    v_usuario   int;
    v_fallas    int := 0;
BEGIN
    SELECT id INTO v_empresa FROM empresa WHERE ruc = '20600123456';
    SELECT id INTO v_sede FROM sede WHERE empresa_id = v_empresa AND es_principal;

    IF v_empresa IS NULL OR v_sede IS NULL THEN
        RAISE EXCEPTION 'Falta la empresa de demostracion: corre scripts/01_seed.sql primero';
    END IF;

    -- ----------------------------------------------------------------
    -- 1. La bitacora no se puede modificar ni borrar.
    -- ----------------------------------------------------------------
    INSERT INTO bitacora (empresa_id, accion, entidad, entidad_id, origen)
    VALUES (v_empresa, 'QA.PRUEBA', 'qa', '1', 'SISTEMA')
    RETURNING id INTO v_bit;

    BEGIN
        UPDATE bitacora SET accion = 'QA.CAMBIADA' WHERE id = v_bit;
        RAISE WARNING 'FALLA 1a: se pudo hacer UPDATE sobre bitacora';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 1a: UPDATE sobre bitacora rechazado';
    END;

    BEGIN
        DELETE FROM bitacora WHERE id = v_bit;
        RAISE WARNING 'FALLA 1b: se pudo hacer DELETE sobre bitacora';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 1b: DELETE sobre bitacora rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 2. Un rol que no maneja dinero no puede recibir permiso de dinero.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO rol_permiso (rol_id, permiso_codigo) VALUES (6, 'CAJA.COBRAR');
        RAISE WARNING 'FALLA 2: el Tecnico recibio CAJA.COBRAR';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 2: permiso de dinero negado al Tecnico';
    END;

    -- ----------------------------------------------------------------
    -- 3. El SuperAdmin no puede tener empresa; nadie mas puede estar sin ella.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
        VALUES (v_empresa, v_sede, 0, 'QA', 'SuperAdmin', 'qa.sa@lubritech.test', 'x');
        RAISE WARNING 'FALLA 3a: se creo un SuperAdmin con empresa';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'PASA 3a: SuperAdmin con empresa rechazado';
    END;

    BEGIN
        INSERT INTO usuario (empresa_id, rol_id, nombres, apellidos, email, password_hash)
        VALUES (NULL, 6, 'QA', 'Tecnico', 'qa.tec@lubritech.test', 'x');
        RAISE WARNING 'FALLA 3b: se creo un Tecnico sin empresa';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'PASA 3b: usuario de empresa sin empresa rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 4. La sede del usuario tiene que ser de su propia empresa.
    -- ----------------------------------------------------------------
    INSERT INTO empresa (ruc, razon_social, nombre_comercial, plan_id, servicio_hasta)
    SELECT '20700000001', 'Otro Taller S.A.C.', 'Otro Taller', p.id, DATE '2027-12-31'
      FROM plan p WHERE p.codigo = 'PREMIUM'
    RETURNING id INTO v_otra_emp;

    INSERT INTO sede (empresa_id, nombre, es_principal)
    VALUES (v_otra_emp, 'Sede Ajena', true)
    RETURNING id INTO v_sede_otra;

    BEGIN
        INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
        VALUES (v_empresa, v_sede_otra, 6, 'QA', 'Cruzado', 'qa.cruz@lubritech.test', 'x');
        RAISE WARNING 'FALLA 4: un usuario quedo en la sede de otra empresa';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 4: sede de otra empresa rechazada';
    END;

    -- ----------------------------------------------------------------
    -- 5. El cupo del plan frena el alta. El plan Basico da 1 Admin y 2 tecnicos.
    -- ----------------------------------------------------------------
    UPDATE empresa SET plan_id = (SELECT id FROM plan WHERE codigo = 'BASICO')
     WHERE id = v_otra_emp;

    INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
    VALUES (v_otra_emp, v_sede_otra, 1, 'QA', 'Admin', 'qa.admin@lubritech.test', 'x')
    RETURNING id INTO v_usuario;

    -- El rol 3 (Contador) no tiene cupo en el plan Basico: no esta habilitado.
    BEGIN
        INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
        VALUES (v_otra_emp, v_sede_otra, 3, 'QA', 'Contador', 'qa.cont@lubritech.test', 'x');
        RAISE WARNING 'FALLA 5a: el plan Basico acepto un Contador';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 5a: rol no habilitado por el plan rechazado';
    END;

    INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
    VALUES (v_otra_emp, v_sede_otra, 6, 'QA', 'Tecnico1', 'qa.t1@lubritech.test', 'x');
    INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
    VALUES (v_otra_emp, v_sede_otra, 6, 'QA', 'Tecnico2', 'qa.t2@lubritech.test', 'x');

    BEGIN
        INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
        VALUES (v_otra_emp, v_sede_otra, 6, 'QA', 'Tecnico3', 'qa.t3@lubritech.test', 'x');
        RAISE WARNING 'FALLA 5b: entro un tercer tecnico con cupo de 2';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 5b: tope de tecnicos del plan respetado';
    END;

    -- Un segundo Admin tampoco entra (cupo del plan + indice parcial).
    BEGIN
        INSERT INTO usuario (empresa_id, sede_id, rol_id, nombres, apellidos, email, password_hash)
        VALUES (v_otra_emp, v_sede_otra, 1, 'QA', 'Admin2', 'qa.admin2@lubritech.test', 'x');
        RAISE WARNING 'FALLA 5c: entro un segundo Admin';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception OR unique_violation THEN
        RAISE NOTICE 'PASA 5c: Admin unico por empresa respetado';
    END;

    -- ----------------------------------------------------------------
    -- 6. El tope de sedes del plan. Basico = 1 sede.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO sede (empresa_id, nombre) VALUES (v_otra_emp, 'Sede Extra');
        RAISE WARNING 'FALLA 6: entro una segunda sede en el plan Basico';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 6: tope de sedes del plan respetado';
    END;

    -- ----------------------------------------------------------------
    -- 7. Un dispositivo pertenece a un usuario O a un cliente, no a ninguno.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO dispositivo (usuario_id, aplicacion, plataforma, identificador)
        VALUES (v_usuario, 'OPERACIONES', 'ANDROID', 'qa-dev-1');
        RAISE NOTICE 'PASA 7a: dispositivo de personal aceptado';
    EXCEPTION WHEN others THEN
        RAISE WARNING 'FALLA 7a: no se pudo registrar un dispositivo valido (%)', SQLERRM;
        v_fallas := v_fallas + 1;
    END;

    BEGIN
        INSERT INTO dispositivo (aplicacion, plataforma, identificador)
        VALUES ('OPERACIONES', 'ANDROID', 'qa-dev-2');
        RAISE WARNING 'FALLA 7b: entro un dispositivo sin dueno';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'PASA 7b: dispositivo sin dueno rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 8. Un cliente particular sin nombre no entra.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO cliente (empresa_id, tipo, tipo_documento, documento)
        VALUES (v_empresa, 'PARTICULAR', 'DNI', '11111111');
        RAISE WARNING 'FALLA 8: entro un cliente particular sin nombre';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'PASA 8: cliente particular sin nombre rechazado';
    END;

    IF v_fallas = 0 THEN
        RAISE NOTICE '=============== CANDADOS: 0 FALLAS ===============';
    ELSE
        RAISE EXCEPTION 'CANDADOS: % falla(s)', v_fallas;
    END IF;
END $$;

ROLLBACK;

-- Cuadre de integridad sobre los datos reales: la columna "filas" debe ser 0.
SELECT 'usuario sin empresa que no sea SuperAdmin' AS control, count(*) AS filas
  FROM usuario WHERE empresa_id IS NULL AND rol_id <> 0
UNION ALL
SELECT 'usuario con sede de otra empresa', count(*)
  FROM usuario u JOIN sede s ON s.id = u.sede_id
 WHERE s.empresa_id IS DISTINCT FROM u.empresa_id
UNION ALL
SELECT 'cliente_acceso sin cliente', count(*)
  FROM cliente_acceso a LEFT JOIN cliente c ON c.id = a.cliente_id
 WHERE c.id IS NULL
UNION ALL
SELECT 'dispositivo sin dueno', count(*)
  FROM dispositivo WHERE usuario_id IS NULL AND acceso_id IS NULL
UNION ALL
SELECT 'empresa sin sede principal', count(*)
  FROM empresa e WHERE NOT EXISTS (
        SELECT 1 FROM sede s WHERE s.empresa_id = e.id AND s.es_principal)
UNION ALL
SELECT 'rastro de pruebas QA', count(*)
  FROM usuario WHERE email LIKE 'qa.%@lubritech.test'
UNION ALL
SELECT 'bitacora de pruebas QA', count(*)
  FROM bitacora WHERE accion LIKE 'QA.%';
