-- ============================================================================
-- LubriTech · qa/04_candados_cotizaciones.sql  —  candados de la Fase 4
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f qa/04_candados_cotizaciones.sql
--
-- Comprueba que los candados de la cotizacion muerden: version correlativa,
-- renglon solo editable en borrador, decision solo sobre una enviada, estado
-- que no se fuerza sin decision, decision inmutable, una sola enviada por
-- orden, y el renglon que no pasa a ejecucion sin aprobacion. Todo en una
-- transaccion con ROLLBACK al final: no persiste nada.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    v_emp int; v_sede int; v_cli bigint; v_veh bigint; v_srv bigint;
    v_ord bigint; v_cot bigint; v_cot2 bigint; v_ren bigint;
    v_v1 int; v_v2 int; v_estado text; v_fallas int := 0;
BEGIN
    SELECT id INTO v_emp FROM empresa WHERE ruc = '20600123456';
    SELECT id INTO v_sede FROM sede WHERE empresa_id = v_emp AND es_principal;
    SELECT id INTO v_cli FROM cliente WHERE empresa_id = v_emp LIMIT 1;

    INSERT INTO vehiculo (empresa_id, cliente_id, placa, marca, modelo, kilometraje)
    VALUES (v_emp, v_cli, 'COTQA-1', 'Toyota', 'Yaris', 5000) RETURNING id INTO v_veh;

    INSERT INTO servicio (empresa_id, codigo, nombre, precio)
    VALUES (v_emp, 'COTQA-SRV', 'Cambio de aceite QA', 120) RETURNING id INTO v_srv;

    INSERT INTO orden (empresa_id, sede_id, cliente_id, vehiculo_id, estado_id)
    VALUES (v_emp, v_sede, v_cli, v_veh, 3) RETURNING id INTO v_ord;

    -- ----------------------------------------------------------------
    -- 1. La version de la cotizacion es correlativa por orden.
    -- ----------------------------------------------------------------
    INSERT INTO cotizacion (empresa_id, orden_id) VALUES (v_emp, v_ord) RETURNING id, version INTO v_cot, v_v1;
    INSERT INTO cotizacion (empresa_id, orden_id) VALUES (v_emp, v_ord) RETURNING id, version INTO v_cot2, v_v2;
    IF v_v2 = v_v1 + 1 THEN RAISE NOTICE 'PASA 1: version correlativa por orden (% y %)', v_v1, v_v2;
    ELSE RAISE WARNING 'FALLA 1: versiones % y %', v_v1, v_v2; v_fallas := v_fallas + 1; END IF;

    -- Un renglon en el borrador v_cot.
    INSERT INTO cotizacion_renglon (empresa_id, cotizacion_id, tipo, servicio_id, descripcion, cantidad, precio_unitario)
    VALUES (v_emp, v_cot, 'SERVICIO', v_srv, 'Cambio de aceite', 1, 120) RETURNING id INTO v_ren;

    -- El subtotal lo calcula la base.
    IF (SELECT subtotal FROM cotizacion_renglon WHERE id = v_ren) = 120 THEN
        RAISE NOTICE 'PASA 1b: subtotal calculado por la base (1 x 120 = 120)';
    ELSE RAISE WARNING 'FALLA 1b: subtotal %', (SELECT subtotal FROM cotizacion_renglon WHERE id = v_ren); v_fallas := v_fallas + 1; END IF;

    -- ----------------------------------------------------------------
    -- 2. No se puede forzar APROBADA sin una fila de decision.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE cotizacion SET estado = 'APROBADA' WHERE id = v_cot;
        RAISE WARNING 'FALLA 2: dejo aprobar a mano sin decision'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 2: no se aprueba sin la decision del cliente';
    END;

    -- ----------------------------------------------------------------
    -- 3. No se decide una cotizacion que no esta ENVIADA.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO cotizacion_decision (empresa_id, cotizacion_id, decision, cliente_id)
        VALUES (v_emp, v_cot, 'APROBADA', v_cli);
        RAISE WARNING 'FALLA 3: dejo decidir un borrador'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 3: no se decide un borrador (solo ENVIADA)';
    END;

    -- Enviar la cotizacion.
    UPDATE cotizacion SET estado = 'ENVIADA', enviada_en = now() WHERE id = v_cot;

    -- ----------------------------------------------------------------
    -- 4. Una cotizacion ENVIADA congela sus renglones.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO cotizacion_renglon (empresa_id, cotizacion_id, tipo, descripcion, cantidad, precio_unitario)
        VALUES (v_emp, v_cot, 'SERVICIO', 'Extra', 1, 10);
        RAISE WARNING 'FALLA 4a: dejo agregar renglon a una enviada'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 4a: no se agrega renglon a una cotizacion enviada';
    END;
    BEGIN
        UPDATE cotizacion_renglon SET precio_unitario = 1 WHERE id = v_ren;
        RAISE WARNING 'FALLA 4b: dejo editar renglon de una enviada'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 4b: no se edita renglon de una cotizacion enviada';
    END;

    -- ----------------------------------------------------------------
    -- 5. Una sola cotizacion ENVIADA por orden a la vez.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE cotizacion SET estado = 'ENVIADA', enviada_en = now() WHERE id = v_cot2;
        RAISE WARNING 'FALLA 5: dejo dos cotizaciones enviadas en la misma orden'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'PASA 5: solo una cotizacion enviada por orden';
    END;

    -- ----------------------------------------------------------------
    -- 6. El renglon cotizado no pasa a ejecucion sin aprobacion.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO orden_servicio (empresa_id, orden_id, servicio_id, descripcion, precio, cantidad, cotizacion_renglon_id)
        VALUES (v_emp, v_ord, v_srv, 'Cambio de aceite', 120, 1, v_ren);
        RAISE WARNING 'FALLA 6: dejo pasar a ejecucion un renglon no aprobado'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 6: renglon no aprobado no pasa a ejecucion';
    END;

    -- ----------------------------------------------------------------
    -- 7. La decision del cliente aprueba la cotizacion (unica puerta).
    -- ----------------------------------------------------------------
    INSERT INTO cotizacion_decision (empresa_id, cotizacion_id, decision, cliente_id, comentario)
    VALUES (v_emp, v_cot, 'APROBADA', v_cli, 'de acuerdo');
    SELECT estado INTO v_estado FROM cotizacion WHERE id = v_cot;
    IF v_estado = 'APROBADA' THEN RAISE NOTICE 'PASA 7: la decision movio la cotizacion a APROBADA';
    ELSE RAISE WARNING 'FALLA 7: estado tras decision %', v_estado; v_fallas := v_fallas + 1; END IF;

    -- ----------------------------------------------------------------
    -- 8. La decision es inmutable.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE cotizacion_decision SET decision = 'RECHAZADA' WHERE cotizacion_id = v_cot;
        RAISE WARNING 'FALLA 8a: decision editable'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 8a: decision inmutable (UPDATE)';
    END;
    BEGIN
        DELETE FROM cotizacion_decision WHERE cotizacion_id = v_cot;
        RAISE WARNING 'FALLA 8b: decision borrable'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 8b: decision inmutable (DELETE)';
    END;

    -- ----------------------------------------------------------------
    -- 9. Una cotizacion ya decidida no cambia de estado.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE cotizacion SET estado = 'REEMPLAZADA' WHERE id = v_cot;
        RAISE WARNING 'FALLA 9: una aprobada cambio de estado'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 9: una cotizacion decidida no cambia';
    END;

    -- ----------------------------------------------------------------
    -- 10. Con la cotizacion APROBADA, el renglon si pasa a ejecucion.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO orden_servicio (empresa_id, orden_id, servicio_id, descripcion, precio, cantidad, cotizacion_renglon_id)
        VALUES (v_emp, v_ord, v_srv, 'Cambio de aceite', 120, 1, v_ren);
        RAISE NOTICE 'PASA 10: renglon aprobado pasa a ejecucion';
    EXCEPTION WHEN raise_exception THEN RAISE WARNING 'FALLA 10: no dejo pasar un renglon aprobado'; v_fallas := v_fallas + 1;
    END;

    IF v_fallas = 0 THEN
        RAISE NOTICE '=========== CANDADOS COTIZACIONES: 0 FALLAS ===========';
    ELSE
        RAISE EXCEPTION 'CANDADOS COTIZACIONES: % falla(s)', v_fallas;
    END IF;
END $$;

ROLLBACK;

-- Cuadre de integridad de cotizaciones: columna "filas" debe ser 0.
SELECT 'cotizacion con orden de otra empresa' AS control, count(*) AS filas
  FROM cotizacion c JOIN orden o ON o.id = c.orden_id WHERE o.empresa_id <> c.empresa_id
UNION ALL
SELECT 'renglon con cotizacion de otra empresa', count(*)
  FROM cotizacion_renglon r JOIN cotizacion c ON c.id = r.cotizacion_id WHERE c.empresa_id <> r.empresa_id
UNION ALL
SELECT 'decision con cotizacion de otra empresa', count(*)
  FROM cotizacion_decision d JOIN cotizacion c ON c.id = d.cotizacion_id WHERE c.empresa_id <> d.empresa_id
UNION ALL
SELECT 'version de cotizacion repetida en una orden', count(*)
  FROM (SELECT orden_id, version FROM cotizacion GROUP BY orden_id, version HAVING count(*) > 1) x
UNION ALL
SELECT 'mas de una cotizacion enviada por orden', count(*)
  FROM (SELECT orden_id FROM cotizacion WHERE estado = 'ENVIADA' GROUP BY orden_id HAVING count(*) > 1) y
UNION ALL
SELECT 'cotizacion aprobada o rechazada sin su decision', count(*)
  FROM cotizacion c WHERE c.estado IN ('APROBADA','RECHAZADA')
    AND NOT EXISTS (SELECT 1 FROM cotizacion_decision d WHERE d.cotizacion_id = c.id)
UNION ALL
SELECT 'renglon de orden cotizado sin aprobacion', count(*)
  FROM orden_servicio os JOIN cotizacion_renglon r ON r.id = os.cotizacion_renglon_id
  JOIN cotizacion c ON c.id = r.cotizacion_id WHERE c.estado <> 'APROBADA'
UNION ALL
SELECT 'rastro de pruebas QA en cotizaciones', count(*)
  FROM vehiculo WHERE placa LIKE 'COTQA-%';
