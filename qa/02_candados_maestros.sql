-- ============================================================================
-- LubriTech · qa/02_candados_maestros.sql  —  candados de la Fase 2
--
--   PGCLIENTENCODING=UTF8 psql -h localhost -U postgres -d LubriTech \
--     -P pager=off -f qa/02_candados_maestros.sql
--
-- Comprueba, contra los datos reales, que los candados del almacen y de los
-- vehiculos muerden. Todo corre en una transaccion que termina en ROLLBACK:
-- no persiste ni una fila (el kardex es inmutable, asi que NADA de esto puede
-- quedar fuera del rollback).
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

DO $$
DECLARE
    v_emp int; v_sede int; v_sede2 int; v_otra_emp int;
    v_prod bigint; v_prod_nc bigint; v_cli bigint; v_veh bigint;
    v_stock numeric; v_costo numeric; v_saldo numeric;
    v_fallas int := 0;
BEGIN
    SELECT id INTO v_emp FROM empresa WHERE ruc = '20600123456';
    SELECT id INTO v_sede FROM sede WHERE empresa_id = v_emp AND es_principal;
    SELECT id INTO v_sede2 FROM sede WHERE empresa_id = v_emp AND NOT es_principal LIMIT 1;
    SELECT id INTO v_otra_emp FROM empresa WHERE ruc = '20600987654';
    SELECT id INTO v_cli FROM cliente WHERE empresa_id = v_emp LIMIT 1;

    IF v_emp IS NULL OR v_sede IS NULL OR v_cli IS NULL THEN
        RAISE EXCEPTION 'Faltan datos de demostracion: corre scripts/01_seed.sql primero';
    END IF;

    INSERT INTO producto (empresa_id, codigo, nombre, tipo, unidad, precio_venta)
    VALUES (v_emp, 'QA-KARDEX', 'Producto QA', 'REPUESTO', 'UND', 100) RETURNING id INTO v_prod;

    INSERT INTO producto (empresa_id, codigo, nombre, controla_stock, precio_venta)
    VALUES (v_emp, 'QA-NOCTRL', 'Producto sin control', false, 10) RETURNING id INTO v_prod_nc;

    -- ----------------------------------------------------------------
    -- 1. El stock se mueve SOLO por el kardex.
    -- ----------------------------------------------------------------
    INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad, costo_unitario)
    VALUES (v_emp, v_prod, v_sede, 'INGRESO', 20, 50);

    SELECT cantidad, costo_promedio INTO v_stock, v_costo
      FROM producto_stock WHERE producto_id = v_prod AND sede_id = v_sede;

    IF v_stock = 20 AND v_costo = 50 THEN
        RAISE NOTICE 'PASA 1a: el ingreso creo el stock (20 @ 50)';
    ELSE
        RAISE WARNING 'FALLA 1a: stock=% costo=% (esperado 20 @ 50)', v_stock, v_costo;
        v_fallas := v_fallas + 1;
    END IF;

    -- Promedio ponderado: +10 @ 80 -> 30 unidades, promedio 60
    INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad, costo_unitario)
    VALUES (v_emp, v_prod, v_sede, 'INGRESO', 10, 80);
    SELECT cantidad, costo_promedio INTO v_stock, v_costo
      FROM producto_stock WHERE producto_id = v_prod AND sede_id = v_sede;
    IF v_stock = 30 AND v_costo = 60 THEN
        RAISE NOTICE 'PASA 1b: costo promedio ponderado correcto (30 @ 60)';
    ELSE
        RAISE WARNING 'FALLA 1b: stock=% costo=% (esperado 30 @ 60)', v_stock, v_costo;
        v_fallas := v_fallas + 1;
    END IF;

    -- El saldo que graba el kardex es el de despues del movimiento.
    SELECT saldo INTO v_saldo FROM kardex WHERE producto_id = v_prod ORDER BY id DESC LIMIT 1;
    IF v_saldo = 30 THEN RAISE NOTICE 'PASA 1c: el kardex graba el saldo real (30)';
    ELSE RAISE WARNING 'FALLA 1c: saldo=% (esperado 30)', v_saldo; v_fallas := v_fallas + 1;
    END IF;

    -- ----------------------------------------------------------------
    -- 2. Una salida no deja el stock en negativo.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad)
        VALUES (v_emp, v_prod, v_sede, 'SALIDA', 999);
        RAISE WARNING 'FALLA 2a: dejo sacar mas que el stock';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 2a: salida > stock rechazada';
    END;

    -- Un producto que NO controla stock si deja la salida (y no baja de cero).
    BEGIN
        INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad)
        VALUES (v_emp, v_prod_nc, v_sede, 'SALIDA', 5);
        SELECT cantidad INTO v_stock FROM producto_stock WHERE producto_id = v_prod_nc AND sede_id = v_sede;
        IF v_stock = 0 THEN RAISE NOTICE 'PASA 2b: producto sin control sale y queda en cero';
        ELSE RAISE WARNING 'FALLA 2b: stock=% (esperado 0)', v_stock; v_fallas := v_fallas + 1;
        END IF;
    EXCEPTION WHEN others THEN
        RAISE WARNING 'FALLA 2b: fallo la salida de un producto sin control (%)', SQLERRM;
        v_fallas := v_fallas + 1;
    END;

    -- ----------------------------------------------------------------
    -- 3. producto_stock no se edita a mano.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE producto_stock SET cantidad = 999 WHERE producto_id = v_prod AND sede_id = v_sede;
        RAISE WARNING 'FALLA 3a: se pudo editar producto_stock directo';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 3a: UPDATE directo a stock rechazado';
    END;

    BEGIN
        INSERT INTO producto_stock (producto_id, sede_id, cantidad, costo_promedio)
        VALUES (v_prod, v_sede2, 5, 5);
        RAISE WARNING 'FALLA 3b: se pudo insertar stock a mano';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 3b: INSERT directo a stock rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 4. El kardex es inmutable.
    -- ----------------------------------------------------------------
    BEGIN
        UPDATE kardex SET cantidad = 1 WHERE producto_id = v_prod;
        RAISE WARNING 'FALLA 4a: se pudo editar el kardex';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 4a: UPDATE al kardex rechazado';
    END;

    BEGIN
        DELETE FROM kardex WHERE producto_id = v_prod;
        RAISE WARNING 'FALLA 4b: se pudo borrar del kardex';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 4b: DELETE al kardex rechazado';
    END;

    -- ----------------------------------------------------------------
    -- 5. La cantidad del kardex es siempre positiva.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad)
        VALUES (v_emp, v_prod, v_sede, 'SALIDA', -5);
        RAISE WARNING 'FALLA 5: entro una cantidad negativa';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN check_violation THEN RAISE NOTICE 'PASA 5: cantidad negativa rechazada';
    END;

    -- ----------------------------------------------------------------
    -- 6. El stock por sede es independiente.
    -- ----------------------------------------------------------------
    IF v_sede2 IS NOT NULL THEN
        INSERT INTO kardex (empresa_id, producto_id, sede_id, tipo, cantidad, costo_unitario)
        VALUES (v_emp, v_prod, v_sede2, 'INGRESO', 7, 50);
        SELECT cantidad INTO v_stock FROM producto_stock WHERE producto_id = v_prod AND sede_id = v_sede2;
        IF v_stock = 7 THEN RAISE NOTICE 'PASA 6: el stock de otra sede es independiente (7)';
        ELSE RAISE WARNING 'FALLA 6: stock sede2=% (esperado 7)', v_stock; v_fallas := v_fallas + 1;
        END IF;
    END IF;

    -- ----------------------------------------------------------------
    -- 7. El kilometraje de un vehiculo no baja.
    -- ----------------------------------------------------------------
    INSERT INTO vehiculo (empresa_id, cliente_id, placa, marca, modelo, kilometraje)
    VALUES (v_emp, v_cli, 'QA-0001', 'Toyota', 'Hilux', 50000) RETURNING id INTO v_veh;

    BEGIN
        UPDATE vehiculo SET kilometraje = 40000 WHERE id = v_veh;
        RAISE WARNING 'FALLA 7a: el kilometraje bajo';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN raise_exception THEN RAISE NOTICE 'PASA 7a: bajar el kilometraje rechazado';
    END;

    BEGIN
        UPDATE vehiculo SET kilometraje = 60000 WHERE id = v_veh;
        RAISE NOTICE 'PASA 7b: subir el kilometraje si se permite';
    EXCEPTION WHEN others THEN
        RAISE WARNING 'FALLA 7b: no dejo subir el kilometraje (%)', SQLERRM;
        v_fallas := v_fallas + 1;
    END;

    -- ----------------------------------------------------------------
    -- 8. La placa es unica dentro de la empresa, insensible a mayusculas.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO vehiculo (empresa_id, cliente_id, placa, marca, modelo)
        VALUES (v_emp, v_cli, 'qa-0001', 'Nissan', 'Frontier');
        RAISE WARNING 'FALLA 8a: entro una placa duplicada (distinto case)';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'PASA 8a: placa duplicada rechazada';
    END;

    -- La misma placa en OTRA empresa si entra (son talleres distintos).
    IF v_otra_emp IS NOT NULL THEN
        DECLARE v_cli2 bigint;
        BEGIN
            SELECT id INTO v_cli2 FROM cliente WHERE empresa_id = v_otra_emp LIMIT 1;
            IF v_cli2 IS NOT NULL THEN
                INSERT INTO vehiculo (empresa_id, cliente_id, placa, marca, modelo)
                VALUES (v_otra_emp, v_cli2, 'QA-0001', 'Kia', 'Rio');
                RAISE NOTICE 'PASA 8b: la misma placa en otra empresa entra';
            END IF;
        EXCEPTION WHEN others THEN
            RAISE WARNING 'FALLA 8b: la placa choco entre empresas (%)', SQLERRM;
            v_fallas := v_fallas + 1;
        END;
    END IF;

    -- ----------------------------------------------------------------
    -- 9. El codigo de producto y de servicio es unico por empresa.
    -- ----------------------------------------------------------------
    BEGIN
        INSERT INTO producto (empresa_id, codigo, nombre) VALUES (v_emp, 'qa-kardex', 'Choca');
        RAISE WARNING 'FALLA 9a: codigo de producto duplicado (distinto case)';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'PASA 9a: codigo de producto duplicado rechazado';
    END;

    INSERT INTO servicio (empresa_id, codigo, nombre, precio) VALUES (v_emp, 'QA-SRV', 'Servicio QA', 50);
    BEGIN
        INSERT INTO servicio (empresa_id, codigo, nombre, precio) VALUES (v_emp, 'qa-srv', 'Choca', 10);
        RAISE WARNING 'FALLA 9b: codigo de servicio duplicado';
        v_fallas := v_fallas + 1;
    EXCEPTION WHEN unique_violation THEN RAISE NOTICE 'PASA 9b: codigo de servicio duplicado rechazado';
    END;

    IF v_fallas = 0 THEN
        RAISE NOTICE '=========== CANDADOS MAESTROS: 0 FALLAS ===========';
    ELSE
        RAISE EXCEPTION 'CANDADOS MAESTROS: % falla(s)', v_fallas;
    END IF;
END $$;

ROLLBACK;

-- Cuadre de integridad del almacen sobre los datos reales: columna "filas" = 0.
SELECT 'stock que no cuadra con el ultimo saldo del kardex' AS control, count(*) AS filas
  FROM producto_stock ps
  JOIN LATERAL (
        SELECT saldo FROM kardex k
         WHERE k.producto_id = ps.producto_id AND k.sede_id = ps.sede_id
         ORDER BY k.id DESC LIMIT 1
       ) ultimo ON true
 WHERE ps.cantidad <> ultimo.saldo
UNION ALL
SELECT 'stock negativo', count(*) FROM producto_stock WHERE cantidad < 0
UNION ALL
SELECT 'kardex de un producto de otra empresa', count(*)
  FROM kardex k JOIN producto p ON p.id = k.producto_id
 WHERE p.empresa_id <> k.empresa_id
UNION ALL
SELECT 'kardex en una sede de otra empresa', count(*)
  FROM kardex k JOIN sede s ON s.id = k.sede_id
 WHERE s.empresa_id <> k.empresa_id
UNION ALL
SELECT 'vehiculo de un cliente de otra empresa', count(*)
  FROM vehiculo v JOIN cliente c ON c.id = v.cliente_id
 WHERE c.empresa_id <> v.empresa_id
UNION ALL
SELECT 'rastro de pruebas QA en producto', count(*)
  FROM producto WHERE codigo LIKE 'QA-%'
UNION ALL
SELECT 'rastro de pruebas QA en vehiculo', count(*)
  FROM vehiculo WHERE placa LIKE 'QA-%';
