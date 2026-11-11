-- ============================================================================
-- LubriTech · qa/01_candados_consola.sql
-- Candados de la Fase 1 (consola SaaS): lo que la base NO debe dejar pasar
-- por mas que se lo pida la aplicacion.
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f qa/01_candados_consola.sql
--
-- Resultado esperado: todas las lineas dicen PASA y el cuadre final en cero.
-- Todo corre dentro de una transaccion que termina en ROLLBACK: la tabla
-- suscripcion es inmutable al DELETE, asi que NADA de esto puede quedar.
-- Las fechas de prueba viven en 2090 para no cruzarse con pagos reales.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    v_empresa   int;
    v_otra      int;
    v_plan      int;
    v_premium   int;
    v_sus       bigint;
    v_fallas    int := 0;
BEGIN
    SELECT id INTO v_empresa FROM empresa WHERE ruc = '20600123456';
    SELECT id INTO v_otra    FROM empresa WHERE ruc = '20600987654';
    SELECT id INTO v_plan    FROM plan WHERE codigo = 'BASICO';
    SELECT id INTO v_premium FROM plan WHERE codigo = 'PREMIUM';

    IF v_empresa IS NULL OR v_otra IS NULL OR v_plan IS NULL THEN
        RAISE EXCEPTION 'Faltan las empresas de demostracion: corre scripts/01_seed.sql primero';
    END IF;

    -- ----------------------------------------------------------------
    -- 1. Dos periodos pagados de la misma empresa no se solapan.
    -- ----------------------------------------------------------------
    INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado)
    VALUES (v_empresa, v_plan, '2090-01-01', '2090-01-31', 99.00, 'PAGADA')
    RETURNING id INTO v_sus;

    BEGIN
        INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado)
        VALUES (v_empresa, v_plan, '2090-01-15', '2090-02-15', 99.00, 'PAGADA');
        RAISE WARNING 'FALLA 1a: entraron dos periodos pagados solapados';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN exclusion_violation THEN
        RAISE NOTICE 'PASA 1a: periodo pagado solapado rechazado';
    END;

    -- Pegado al dia siguiente SI entra: asi encadena la renovacion.
    BEGIN
        INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado)
        VALUES (v_empresa, v_plan, '2090-02-01', '2090-02-28', 99.00, 'PAGADA');
        RAISE NOTICE 'PASA 1b: el periodo siguiente encadena sin solape';
    EXCEPTION WHEN others THEN
        RAISE WARNING 'FALLA 1b: no se pudo encadenar la renovacion (%)', SQLERRM;
        v_fallas := v_fallas + 1;
    END;

    -- Un periodo ANULADO no estorba: el candado solo mira los pagados.
    BEGIN
        INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado)
        VALUES (v_empresa, v_plan, '2090-01-10', '2090-01-20', 99.00, 'ANULADA');
        RAISE NOTICE 'PASA 1c: un periodo anulado puede solaparse';
    EXCEPTION WHEN others THEN
        RAISE WARNING 'FALLA 1c: el anulado tambien fue rechazado (%)', SQLERRM;
        v_fallas := v_fallas + 1;
    END;

    -- Y el candado es POR EMPRESA: el mismo mes en otra empresa es normal.
    BEGIN
        INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado)
        VALUES (v_otra, v_plan, '2090-01-01', '2090-01-31', 99.00, 'PAGADA');
        RAISE NOTICE 'PASA 1d: el mismo periodo en otra empresa entra';
    EXCEPTION WHEN others THEN
        RAISE WARNING 'FALLA 1d: el candado cruzo empresas (%)', SQLERRM;
        v_fallas := v_fallas + 1;
    END;

    -- ----------------------------------------------------------------
    -- 2. Un pago no se reescribe: se anula y se registra el correcto.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE suscripcion SET monto = 1.00 WHERE id = v_sus;
        RAISE WARNING 'FALLA 2a: se pudo cambiar el monto de un pago';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 2a: el monto de un pago no se retoca';
    END;

    BEGIN
        UPDATE suscripcion SET hasta = '2090-12-31' WHERE id = v_sus;
        RAISE WARNING 'FALLA 2b: se pudo estirar el periodo de un pago';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 2b: el periodo de un pago no se estira';
    END;

    BEGIN
        UPDATE suscripcion SET plan_id = v_premium WHERE id = v_sus;
        RAISE WARNING 'FALLA 2c: se pudo mover un pago a otro plan';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 2c: un pago no cambia de plan';
    END;

    BEGIN
        UPDATE suscripcion SET empresa_id = v_otra WHERE id = v_sus;
        RAISE WARNING 'FALLA 2d: se pudo mover un pago a otra empresa';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 2d: un pago no cambia de empresa';
    END;

    -- La referencia SI se corrige: es el numero de operacion, no el dinero.
    BEGIN
        UPDATE suscripcion SET referencia = 'OP-CORREGIDA' WHERE id = v_sus;
        RAISE NOTICE 'PASA 2e: la referencia del pago si se puede corregir';
    EXCEPTION WHEN others THEN
        RAISE WARNING 'FALLA 2e: no se pudo corregir la referencia (%)', SQLERRM;
        v_fallas := v_fallas + 1;
    END;

    -- ----------------------------------------------------------------
    -- 3. Un pago anulado no revive, y un pagado no vuelve a pendiente.
    -- ----------------------------------------------------------------
    UPDATE suscripcion SET estado = 'ANULADA' WHERE id = v_sus;

    BEGIN
        UPDATE suscripcion SET estado = 'PAGADA' WHERE id = v_sus;
        RAISE WARNING 'FALLA 3a: un pago anulado revivio';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 3a: un pago anulado no revive';
    END;

    DECLARE
        v_otro bigint;
    BEGIN
        INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado)
        VALUES (v_empresa, v_plan, '2090-06-01', '2090-06-30', 99.00, 'PAGADA')
        RETURNING id INTO v_otro;

        BEGIN
            UPDATE suscripcion SET estado = 'PENDIENTE' WHERE id = v_otro;
            RAISE WARNING 'FALLA 3b: un pago volvio a pendiente';
            v_fallas := v_fallas + 1;
        EXCEPTION WHEN raise_exception THEN
            RAISE NOTICE 'PASA 3b: un pago no vuelve a pendiente';
        END;
    END;

    -- ----------------------------------------------------------------
    -- 4. Un pago no se borra nunca: la plata es historia.
    -- ----------------------------------------------------------------
    BEGIN
        DELETE FROM suscripcion WHERE id = v_sus;
        RAISE WARNING 'FALLA 4: se pudo borrar un pago';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN
        RAISE NOTICE 'PASA 4: un pago no se borra';
    END;

    -- ----------------------------------------------------------------
    -- 5. El SuperAdmin no lleva cupo: no pertenece a ninguna empresa.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO plan_rol_cupo (plan_id, rol_id, cupo) VALUES (v_plan, 0, 1);
        RAISE WARNING 'FALLA 5a: se pudo poner cupo al rol SuperAdmin';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'PASA 5a: cupo del rol SuperAdmin rechazado';
    END;

    BEGIN
        INSERT INTO plan_rol_cupo (plan_id, rol_id, cupo) VALUES (v_plan, 99, 1);
        RAISE WARNING 'FALLA 5b: se pudo poner cupo a un rol inexistente';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN foreign_key_violation THEN
        RAISE NOTICE 'PASA 5b: cupo de un rol inexistente rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 6. Las funciones vendibles salen del catalogo, no de un texto libre.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO plan_funcion (plan_id, funcion) VALUES (v_plan, 'INVENTADA');
        RAISE WARNING 'FALLA 6a: entro una funcion que no esta en el catalogo';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN foreign_key_violation THEN
        RAISE NOTICE 'PASA 6a: funcion fuera del catalogo rechazada';
    END;

    BEGIN
        INSERT INTO permiso (codigo, nombre, modulo, funcion_requerida)
        VALUES ('QA.PRUEBA', 'Prueba de QA', 'QA', 'INVENTADA');
        RAISE WARNING 'FALLA 6b: un permiso quedo atado a una funcion inexistente';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN foreign_key_violation THEN
        RAISE NOTICE 'PASA 6b: permiso con funcion inexistente rechazado';
    END;

    IF v_fallas = 0 THEN
        RAISE NOTICE '=========== CANDADOS CONSOLA: 0 FALLAS ===========';
    ELSE
        RAISE EXCEPTION 'CANDADOS CONSOLA: % falla(s)', v_fallas;
    END IF;
END $$;

ROLLBACK;

-- Cuadre de integridad de la consola: la columna "filas" debe ser 0.
SELECT 'suscripcion de una empresa que no existe' AS control, count(*) AS filas
  FROM suscripcion s LEFT JOIN empresa e ON e.id = s.empresa_id
 WHERE e.id IS NULL
UNION ALL
SELECT 'suscripcion con hasta anterior a desde', count(*)
  FROM suscripcion WHERE hasta < desde
UNION ALL
SELECT 'periodos pagados solapados', count(*)
  FROM suscripcion a JOIN suscripcion b
    ON b.empresa_id = a.empresa_id AND b.id > a.id
 WHERE a.estado = 'PAGADA' AND b.estado = 'PAGADA'
   AND daterange(a.desde, a.hasta, '[]') && daterange(b.desde, b.hasta, '[]')
UNION ALL
SELECT 'plan_funcion fuera del catalogo', count(*)
  FROM plan_funcion pf LEFT JOIN funcion f ON f.codigo = pf.funcion
 WHERE f.codigo IS NULL
UNION ALL
SELECT 'permiso con funcion fuera del catalogo', count(*)
  FROM permiso p LEFT JOIN funcion f ON f.codigo = p.funcion_requerida
 WHERE p.funcion_requerida IS NOT NULL AND f.codigo IS NULL
UNION ALL
SELECT 'cupo del rol SuperAdmin', count(*)
  FROM plan_rol_cupo WHERE rol_id = 0
UNION ALL
SELECT 'empresa con mas de una sede principal', count(*)
  FROM (SELECT empresa_id FROM sede WHERE es_principal
         GROUP BY empresa_id HAVING count(*) > 1) AS x
UNION ALL
SELECT 'empresa con servicio_hasta sin ningun pago', count(*)
  FROM empresa e
 WHERE NOT EXISTS (SELECT 1 FROM suscripcion s
                    WHERE s.empresa_id = e.id AND s.estado = 'PAGADA')
   AND e.servicio_hasta > (now() AT TIME ZONE 'America/Lima')::date + 90
UNION ALL
SELECT 'rastro de pruebas QA en suscripcion', count(*)
  FROM suscripcion WHERE desde >= '2090-01-01'
UNION ALL
SELECT 'rastro de pruebas QA en permiso', count(*)
  FROM permiso WHERE codigo LIKE 'QA.%';
