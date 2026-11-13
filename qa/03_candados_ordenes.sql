-- ============================================================================
-- LubriTech · qa/03_candados_ordenes.sql  —  candados de la Fase 3
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f qa/03_candados_ordenes.sql
--
-- Comprueba que los candados de la orden muerden: avance de a un estado,
-- estado final que no cambia, correlativo por empresa, consumo que mueve
-- kardex, historial y consumo inmutables. Todo en una transaccion con
-- ROLLBACK al final: no persiste nada.
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    v_emp int; v_sede int; v_cli bigint; v_veh bigint; v_prod bigint;
    v_ord bigint; v_ord2 bigint; v_num int; v_num2 int; v_saldo numeric; v_fallas int := 0;
BEGIN
    SELECT id INTO v_emp FROM empresa WHERE ruc = '20600123456';
    SELECT id INTO v_sede FROM sede WHERE empresa_id = v_emp AND es_principal;
    SELECT id INTO v_cli FROM cliente WHERE empresa_id = v_emp LIMIT 1;

    INSERT INTO vehiculo (empresa_id, cliente_id, placa, marca, modelo, kilometraje)
    VALUES (v_emp, v_cli, 'ORDQA-1', 'Toyota', 'Corolla', 10000) RETURNING id INTO v_veh;

    INSERT INTO producto (empresa_id, codigo, nombre, tipo, unidad, precio_venta)
    VALUES (v_emp, 'ORDQA-PRD', 'Aceite QA', 'ACEITE', 'LT', 60) RETURNING id INTO v_prod;

    INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad, costo_unitario)
    VALUES (v_emp, v_prod, v_sede, 'INGRESO', 10, 30);

    -- ----------------------------------------------------------------
    -- 1. El numero de orden es correlativo por empresa.
    -- ----------------------------------------------------------------
    INSERT INTO orden (empresa_id, sede_id, cliente_id, vehiculo_id, estado_id)
    VALUES (v_emp, v_sede, v_cli, v_veh, 2) RETURNING id, numero INTO v_ord, v_num;
    INSERT INTO orden (empresa_id, sede_id, cliente_id, vehiculo_id, estado_id)
    VALUES (v_emp, v_sede, v_cli, v_veh, 2) RETURNING id, numero INTO v_ord2, v_num2;

    IF v_num2 = v_num + 1 THEN RAISE NOTICE 'PASA 1: correlativo por empresa (% y %)', v_num, v_num2;
    ELSE RAISE WARNING 'FALLA 1: numeros % y %', v_num, v_num2; v_fallas := v_fallas + 1; END IF;

    -- ----------------------------------------------------------------
    -- 2. La orden avanza de a un estado; no se salta.
    -- ----------------------------------------------------------------
    UPDATE orden SET estado_id = 3 WHERE id = v_ord;
    RAISE NOTICE 'PASA 2a: 2->3 (avance de a uno)';

    BEGIN
        UPDATE orden SET estado_id = 6 WHERE id = v_ord;
        RAISE WARNING 'FALLA 2b: dejo saltar 3->6'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 2b: salto 3->6 rechazado';
    END;

    BEGIN
        UPDATE orden SET estado_id = 2 WHERE id = v_ord;
        RAISE WARNING 'FALLA 2c: dejo retroceder 3->2'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 2c: retroceso 3->2 rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 3. Anular desde cualquier estado no final, y una final no cambia.
    -- ----------------------------------------------------------------
    UPDATE orden SET estado_id = 11, anulada_motivo = 'prueba' WHERE id = v_ord2;
    RAISE NOTICE 'PASA 3a: anular desde Recibido permitido';

    BEGIN
        UPDATE orden SET estado_id = 3 WHERE id = v_ord2;
        RAISE WARNING 'FALLA 3b: una orden anulada cambio'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 3b: orden anulada (final) no cambia';
    END;

    -- Llevar v_ord hasta Entregada y comprobar que ahi queda.
    UPDATE orden SET estado_id = 4 WHERE id = v_ord;
    UPDATE orden SET estado_id = 5 WHERE id = v_ord;
    UPDATE orden SET estado_id = 6 WHERE id = v_ord;

    -- ----------------------------------------------------------------
    -- 4. Consumir repuesto en la orden mueve el kardex.
    -- ----------------------------------------------------------------
    INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad, referencia)
    VALUES (v_emp, v_prod, v_sede, 'SALIDA', 4, 'orden ' || v_num);
    SELECT cantidad INTO v_saldo FROM producto_stock WHERE producto_id = v_prod AND sede_id = v_sede;
    IF v_saldo = 6 THEN RAISE NOTICE 'PASA 4a: consumo movio stock 10->6';
    ELSE RAISE WARNING 'FALLA 4a: stock=%', v_saldo; v_fallas := v_fallas + 1; END IF;

    INSERT INTO orden_consumo (empresa_id, orden_id, producto_id, sede_id, cantidad, costo_unitario, precio_venta)
    VALUES (v_emp, v_ord, v_prod, v_sede, 4, 30, 60);

    -- No se puede consumir mas que el stock: la base lo frena.
    BEGIN
        INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad)
        VALUES (v_emp, v_prod, v_sede, 'SALIDA', 999);
        RAISE WARNING 'FALLA 4b: dejo consumir mas que el stock'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 4b: consumo > stock rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 5. El consumo es inmutable.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE orden_consumo SET cantidad = 1 WHERE orden_id = v_ord;
        RAISE WARNING 'FALLA 5a: consumo editable'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 5a: consumo inmutable (UPDATE)';
    END;
    BEGIN
        DELETE FROM orden_consumo WHERE orden_id = v_ord;
        RAISE WARNING 'FALLA 5b: consumo borrable'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 5b: consumo inmutable (DELETE)';
    END;

    -- ----------------------------------------------------------------
    -- 6. El historial es inmutable.
    -- ----------------------------------------------------------------
    INSERT INTO orden_historial (empresa_id, orden_id, estado_nuevo_id, observacion)
    VALUES (v_emp, v_ord, 6, 'x');
    BEGIN
        UPDATE orden_historial SET observacion = 'y' WHERE orden_id = v_ord;
        RAISE WARNING 'FALLA 6a: historial editable'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 6a: historial inmutable (UPDATE)';
    END;
    BEGIN
        DELETE FROM orden_historial WHERE orden_id = v_ord;
        RAISE WARNING 'FALLA 6b: historial borrable'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 6b: historial inmutable (DELETE)';
    END;

    -- ----------------------------------------------------------------
    -- 7. Un resultado de checklist invalido no entra.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO orden_checklist (empresa_id, orden_id, item, resultado)
        VALUES (v_emp, v_ord, 'Frenos', 'MAS_O_MENOS');
        RAISE WARNING 'FALLA 7: entro un resultado invalido'; v_fallas := v_fallas + 1;
    EXCEPTION WHEN check_violation THEN RAISE NOTICE 'PASA 7: resultado de checklist invalido rechazado';
    END;

    IF v_fallas = 0 THEN
        RAISE NOTICE '=========== CANDADOS ORDENES: 0 FALLAS ===========';
    ELSE
        RAISE EXCEPTION 'CANDADOS ORDENES: % falla(s)', v_fallas;
    END IF;
END $$;

ROLLBACK;

-- Cuadre de integridad de ordenes: columna "filas" debe ser 0.
SELECT 'orden en un estado inexistente' AS control, count(*) AS filas
  FROM orden o LEFT JOIN orden_estado e ON e.id = o.estado_id WHERE e.id IS NULL
UNION ALL
SELECT 'orden con vehiculo de otra empresa', count(*)
  FROM orden o JOIN vehiculo v ON v.id = o.vehiculo_id WHERE v.empresa_id <> o.empresa_id
UNION ALL
SELECT 'orden con cliente de otra empresa', count(*)
  FROM orden o JOIN cliente c ON c.id = o.cliente_id WHERE c.empresa_id <> o.empresa_id
UNION ALL
SELECT 'orden con sede de otra empresa', count(*)
  FROM orden o JOIN sede s ON s.id = o.sede_id WHERE s.empresa_id <> o.empresa_id
UNION ALL
SELECT 'consumo sin su movimiento de kardex', count(*)
  FROM orden_consumo oc LEFT JOIN kardex k ON k.id = oc.kardex_id
 WHERE oc.kardex_id IS NOT NULL AND k.id IS NULL
UNION ALL
SELECT 'numero de orden repetido en una empresa', count(*)
  FROM (SELECT empresa_id, numero FROM orden GROUP BY empresa_id, numero HAVING count(*) > 1) x
UNION ALL
SELECT 'rastro de pruebas QA en orden', count(*)
  FROM vehiculo WHERE placa LIKE 'ORDQA-%';
