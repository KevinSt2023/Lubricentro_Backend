-- ============================================================================
-- LubriTech · 01_seed.sql
-- Catalogos (roles, permisos, planes) y datos de demostracion para dev.
--
--   PGCLIENTENCODING=UTF8 "C:/Program Files/PostgreSQL/18/bin/psql.exe" \
--     -h localhost -U postgres -d LubriTech -v ON_ERROR_STOP=1 -P pager=off \
--     -f scripts/01_seed.sql
--
-- Los USUARIOS no se siembran aqui: los crea el backend al arrancar
-- (SembradoInicial), para que el hash lo genere la misma libreria que lo
-- verifica. Sembrar un hash a mano en SQL es la receta para un "clave
-- incorrecta" que nadie entiende.
--
-- Idempotente: se puede volver a correr.
-- ============================================================================

BEGIN;

-- ---------------------------------------------------------------------------
-- ROLES
-- maneja_dinero = false deja al rol fuera de cualquier permiso de dinero,
-- con candado en la base (fn_rol_permiso_dinero).
-- ---------------------------------------------------------------------------

INSERT INTO rol (id, nombre, descripcion, maneja_dinero) VALUES
    (0, 'SuperAdmin',          'Consola del SaaS. Sin empresa.',                     false),
    (1, 'Admin',               'Propietario del negocio.',                            true),
    (2, 'Gerente',             'Gerencia y tablero ejecutivo.',                       true),
    (3, 'Contador',            'Finanzas y comprobantes.',                            true),
    (4, 'Supervisor',          'Jefe de taller. Asigna y controla calidad.',          false),
    (5, 'Asesor de servicio',  'Recibe el vehiculo y arma la cotizacion.',            false),
    (6, 'Tecnico',             'Mecanico u operario. Ejecuta el trabajo.',            false),
    (7, 'Cajero',              'Cobra y cierra turno.',                                true),
    (8, 'Almacenero',          'Almacen, repuestos e insumos.',                       false)
ON CONFLICT (id) DO UPDATE
   SET nombre = EXCLUDED.nombre,
       descripcion = EXCLUDED.descripcion,
       maneja_dinero = EXCLUDED.maneja_dinero;

-- ---------------------------------------------------------------------------
-- PERMISOS
-- ---------------------------------------------------------------------------

INSERT INTO permiso (codigo, nombre, modulo, es_dinero, funcion_requerida) VALUES
    -- Personal
    ('PERSONAL.VER',            'Ver personal',                  'PERSONAL',       false, NULL),
    ('PERSONAL.CREAR',          'Dar de alta personal',          'PERSONAL',       false, NULL),
    ('PERSONAL.EDITAR',         'Editar personal',               'PERSONAL',       false, NULL),
    ('PERSONAL.PERMISOS',       'Editar la matriz de permisos',  'PERSONAL',       false, NULL),
    -- Clientes
    ('CLIENTES.VER',            'Ver clientes',                  'CLIENTES',       false, NULL),
    ('CLIENTES.CREAR',          'Registrar clientes',            'CLIENTES',       false, NULL),
    ('CLIENTES.EDITAR',         'Editar clientes',               'CLIENTES',       false, NULL),
    ('CLIENTES.PORTAL',         'Habilitar acceso al portal',    'CLIENTES',       false, 'APP_CLIENTES'),
    -- Vehiculos
    ('VEHICULOS.VER',           'Ver vehiculos',                 'VEHICULOS',      false, NULL),
    ('VEHICULOS.CREAR',         'Registrar vehiculos',           'VEHICULOS',      false, NULL),
    ('VEHICULOS.EDITAR',        'Editar vehiculos',              'VEHICULOS',      false, NULL),
    ('VEHICULOS.FLOTA',         'Gestionar flotas de empresa',   'VEHICULOS',      false, 'FLOTAS'),
    -- Ordenes de trabajo
    ('ORDENES.VER',             'Ver ordenes de trabajo',        'ORDENES',        false, NULL),
    ('ORDENES.VER_ASIGNADAS',   'Ver solo las ordenes propias',  'ORDENES',        false, NULL),
    ('ORDENES.CREAR',           'Abrir orden de trabajo',        'ORDENES',        false, NULL),
    ('ORDENES.ASIGNAR',         'Asignar tecnico a la orden',    'ORDENES',        false, NULL),
    ('ORDENES.ESTADO',          'Cambiar estado de la orden',    'ORDENES',        false, NULL),
    ('ORDENES.CHECKLIST',       'Llenar checklist de inspeccion','ORDENES',        false, NULL),
    ('ORDENES.EVIDENCIA',       'Subir fotos y evidencias',      'ORDENES',        false, NULL),
    ('ORDENES.NOTA_INTERNA',    'Escribir y leer notas internas','ORDENES',        false, NULL),
    ('ORDENES.CALIDAD',         'Aprobar control de calidad',    'ORDENES',        false, NULL),
    ('ORDENES.ENTREGAR',        'Entregar el vehiculo',          'ORDENES',        false, NULL),
    ('ORDENES.ANULAR',          'Anular orden de trabajo',       'ORDENES',        false, NULL),
    ('ORDENES.COSTOS',          'Ver costo y margen de la orden','ORDENES',         true, NULL),
    -- Cotizaciones y aprobaciones
    ('COTIZACION.VER',          'Ver cotizaciones',              'COTIZACION',     false, NULL),
    ('COTIZACION.CREAR',        'Armar cotizacion',              'COTIZACION',     false, NULL),
    ('COTIZACION.ENVIAR',       'Enviar cotizacion al cliente',  'COTIZACION',     false, NULL),
    ('COTIZACION.HALLAZGO',     'Registrar hallazgo tecnico',    'COTIZACION',     false, NULL),
    -- Agenda
    ('AGENDA.VER',              'Ver agenda',                    'AGENDA',         false, NULL),
    ('AGENDA.AGENDAR',          'Registrar y mover citas',       'AGENDA',         false, NULL),
    -- Servicios
    ('SERVICIOS.VER',           'Ver catalogo de servicios',     'SERVICIOS',      false, NULL),
    ('SERVICIOS.EDITAR',        'Editar catalogo de servicios',  'SERVICIOS',      false, NULL),
    -- Almacen
    ('ALMACEN.VER',             'Ver almacen y stock',           'ALMACEN',        false, NULL),
    ('ALMACEN.INGRESAR',        'Ingresar mercaderia',           'ALMACEN',        false, NULL),
    ('ALMACEN.CONSUMIR',        'Consumir repuesto en la orden', 'ALMACEN',        false, NULL),
    ('ALMACEN.SOLICITAR',       'Solicitar materiales',          'ALMACEN',        false, NULL),
    ('ALMACEN.AJUSTAR',         'Ajustar inventario',            'ALMACEN',        false, NULL),
    ('ALMACEN.COSTOS',          'Ver costo de adquisicion',      'ALMACEN',         true, NULL),
    -- Caja
    ('CAJA.ABRIR',              'Abrir turno de caja',           'CAJA',            true, NULL),
    ('CAJA.COBRAR',             'Cobrar',                        'CAJA',            true, NULL),
    ('CAJA.ANULAR_COBRO',       'Revertir un cobro',             'CAJA',            true, NULL),
    ('CAJA.CERRAR',             'Cerrar turno y arquear',        'CAJA',            true, NULL),
    -- Finanzas y reportes
    ('FINANZAS.VER',            'Ver finanzas',                  'FINANZAS',        true, NULL),
    ('REPORTES.VER',            'Ver reportes',                  'REPORTES',       false, NULL),
    ('REPORTES.AVANZADOS',      'Reportes avanzados',            'REPORTES',       false, 'REPORTES_AVANZADOS'),
    ('TABLERO.EJECUTIVO',       'Tablero ejecutivo',             'TABLERO',         true, NULL),
    -- Configuracion y transversales
    ('CONFIGURACION.VER',       'Ver configuracion',             'CONFIGURACION',  false, NULL),
    ('CONFIGURACION.EDITAR',    'Editar configuracion',          'CONFIGURACION',  false, NULL),
    ('SEDES.GESTIONAR',         'Gestionar sedes',               'CONFIGURACION',  false, 'MULTISEDE'),
    ('BITACORA.VER',            'Ver bitacora',                  'BITACORA',       false, NULL),
    ('VISOR3D.USAR',            'Usar el visor 3D',              'VISOR3D',        false, 'VISOR_3D'),
    ('DISPOSITIVOS.GESTIONAR',  'Gestionar dispositivos y sesiones','SEGURIDAD',   false, NULL)
ON CONFLICT (codigo) DO UPDATE
   SET nombre = EXCLUDED.nombre,
       modulo = EXCLUDED.modulo,
       es_dinero = EXCLUDED.es_dinero,
       funcion_requerida = EXCLUDED.funcion_requerida;

-- ---------------------------------------------------------------------------
-- MATRIZ ROL x PERMISO (punto de partida; el Admin la edita despues)
-- El SuperAdmin no lleva filas: no opera empresas, las administra.
-- ---------------------------------------------------------------------------

-- Admin: todo lo de su empresa.
INSERT INTO rol_permiso (rol_id, permiso_codigo)
SELECT 1, codigo FROM permiso
ON CONFLICT DO NOTHING;

-- Gerente: todo menos tocar la matriz de permisos y ajustar inventario.
INSERT INTO rol_permiso (rol_id, permiso_codigo)
SELECT 2, codigo FROM permiso
 WHERE codigo NOT IN ('PERSONAL.PERMISOS','ALMACEN.AJUSTAR','CAJA.ANULAR_COBRO')
ON CONFLICT DO NOTHING;

-- Contador: dinero y papeles, nada de taller.
INSERT INTO rol_permiso (rol_id, permiso_codigo) VALUES
    (3,'FINANZAS.VER'), (3,'REPORTES.VER'), (3,'ORDENES.VER'), (3,'ORDENES.COSTOS'),
    (3,'CLIENTES.VER'), (3,'ALMACEN.VER'), (3,'ALMACEN.COSTOS'), (3,'BITACORA.VER'),
    (3,'CONFIGURACION.VER')
ON CONFLICT DO NOTHING;

-- Supervisor (jefe de taller): manda el taller, no ve plata.
INSERT INTO rol_permiso (rol_id, permiso_codigo) VALUES
    (4,'ORDENES.VER'), (4,'ORDENES.CREAR'), (4,'ORDENES.ASIGNAR'), (4,'ORDENES.ESTADO'),
    (4,'ORDENES.CHECKLIST'), (4,'ORDENES.EVIDENCIA'), (4,'ORDENES.NOTA_INTERNA'),
    (4,'ORDENES.CALIDAD'), (4,'ORDENES.ENTREGAR'),
    (4,'COTIZACION.VER'), (4,'COTIZACION.HALLAZGO'),
    (4,'AGENDA.VER'), (4,'AGENDA.AGENDAR'), (4,'SERVICIOS.VER'),
    (4,'CLIENTES.VER'), (4,'VEHICULOS.VER'), (4,'VEHICULOS.CREAR'), (4,'VEHICULOS.EDITAR'),
    (4,'ALMACEN.VER'), (4,'ALMACEN.SOLICITAR'), (4,'REPORTES.VER'), (4,'VISOR3D.USAR')
ON CONFLICT DO NOTHING;

-- Asesor de servicio: la cara al cliente.
INSERT INTO rol_permiso (rol_id, permiso_codigo) VALUES
    (5,'ORDENES.VER'), (5,'ORDENES.CREAR'), (5,'ORDENES.ESTADO'),
    (5,'ORDENES.CHECKLIST'), (5,'ORDENES.EVIDENCIA'), (5,'ORDENES.ENTREGAR'),
    (5,'COTIZACION.VER'), (5,'COTIZACION.CREAR'), (5,'COTIZACION.ENVIAR'),
    (5,'AGENDA.VER'), (5,'AGENDA.AGENDAR'), (5,'SERVICIOS.VER'),
    (5,'CLIENTES.VER'), (5,'CLIENTES.CREAR'), (5,'CLIENTES.EDITAR'), (5,'CLIENTES.PORTAL'),
    (5,'VEHICULOS.VER'), (5,'VEHICULOS.CREAR'), (5,'VEHICULOS.EDITAR'),
    (5,'ALMACEN.VER'), (5,'VISOR3D.USAR')
ON CONFLICT DO NOTHING;

-- Tecnico: solo lo suyo, y nada de dinero.
INSERT INTO rol_permiso (rol_id, permiso_codigo) VALUES
    (6,'ORDENES.VER_ASIGNADAS'), (6,'ORDENES.ESTADO'), (6,'ORDENES.CHECKLIST'),
    (6,'ORDENES.EVIDENCIA'), (6,'ORDENES.NOTA_INTERNA'),
    (6,'COTIZACION.HALLAZGO'),
    (6,'AGENDA.VER'), (6,'VEHICULOS.VER'), (6,'SERVICIOS.VER'),
    (6,'ALMACEN.VER'), (6,'ALMACEN.CONSUMIR'), (6,'ALMACEN.SOLICITAR'),
    (6,'VISOR3D.USAR')
ON CONFLICT DO NOTHING;

-- Cajero.
INSERT INTO rol_permiso (rol_id, permiso_codigo) VALUES
    (7,'CAJA.ABRIR'), (7,'CAJA.COBRAR'), (7,'CAJA.CERRAR'),
    (7,'ORDENES.VER'), (7,'CLIENTES.VER'), (7,'CLIENTES.CREAR'),
    (7,'VEHICULOS.VER'), (7,'AGENDA.VER'), (7,'REPORTES.VER')
ON CONFLICT DO NOTHING;

-- Almacenero.
INSERT INTO rol_permiso (rol_id, permiso_codigo) VALUES
    (8,'ALMACEN.VER'), (8,'ALMACEN.INGRESAR'), (8,'ALMACEN.CONSUMIR'),
    (8,'ALMACEN.SOLICITAR'), (8,'ALMACEN.AJUSTAR'),
    (8,'ORDENES.VER'), (8,'SERVICIOS.VER'), (8,'REPORTES.VER')
ON CONFLICT DO NOTHING;

-- ---------------------------------------------------------------------------
-- PLANES
-- OJO: los precios son PROVISIONALES, puestos para que el candado de cupo
-- tenga algo con que trabajar. Se reemplazan con la cotizacion real.
-- ---------------------------------------------------------------------------

INSERT INTO plan (codigo, nombre, precio_mensual, max_sedes, orden) VALUES
    ('BASICO',       'Basico',       99.00,  1, 1),
    ('PROFESIONAL',  'Profesional', 189.00,  2, 2),
    ('PREMIUM',      'Premium',     329.00,  0, 3)
ON CONFLICT (codigo) DO UPDATE
   SET nombre = EXCLUDED.nombre,
       max_sedes = EXCLUDED.max_sedes,
       orden = EXCLUDED.orden;

-- Funciones por plan.
INSERT INTO plan_funcion (plan_id, funcion)
SELECT p.id, f.funcion
  FROM plan p
  JOIN (VALUES
        ('BASICO','APP_OPERACIONES'),
        ('BASICO','APP_CLIENTES'),

        ('PROFESIONAL','APP_OPERACIONES'),
        ('PROFESIONAL','APP_CLIENTES'),
        ('PROFESIONAL','MULTISEDE'),
        ('PROFESIONAL','REPORTES_AVANZADOS'),
        ('PROFESIONAL','OFFLINE'),

        ('PREMIUM','APP_OPERACIONES'),
        ('PREMIUM','APP_CLIENTES'),
        ('PREMIUM','MULTISEDE'),
        ('PREMIUM','REPORTES_AVANZADOS'),
        ('PREMIUM','OFFLINE'),
        ('PREMIUM','VISOR_3D'),
        ('PREMIUM','FLOTAS'),
        ('PREMIUM','FACTURACION_ELECTRONICA')
       ) AS f(plan_codigo, funcion) ON f.plan_codigo = p.codigo
ON CONFLICT DO NOTHING;

-- Cupo por rol. Sin fila = el plan no habilita ese rol. 0 = sin tope.
INSERT INTO plan_rol_cupo (plan_id, rol_id, cupo)
SELECT p.id, c.rol_id, c.cupo
  FROM plan p
  JOIN (VALUES
        -- Basico: un taller chico.
        ('BASICO', 1, 1), ('BASICO', 5, 1), ('BASICO', 6, 2),
        ('BASICO', 7, 1), ('BASICO', 8, 1),
        -- Profesional.
        ('PROFESIONAL', 1, 1), ('PROFESIONAL', 2, 1), ('PROFESIONAL', 4, 2),
        ('PROFESIONAL', 5, 2), ('PROFESIONAL', 6, 6), ('PROFESIONAL', 7, 2),
        ('PROFESIONAL', 8, 2),
        -- Premium: sin topes.
        ('PREMIUM', 1, 0), ('PREMIUM', 2, 0), ('PREMIUM', 3, 0), ('PREMIUM', 4, 0),
        ('PREMIUM', 5, 0), ('PREMIUM', 6, 0), ('PREMIUM', 7, 0), ('PREMIUM', 8, 0)
       ) AS c(plan_codigo, rol_id, cupo) ON c.plan_codigo = p.codigo
ON CONFLICT (plan_id, rol_id) DO UPDATE SET cupo = EXCLUDED.cupo;

-- ---------------------------------------------------------------------------
-- EMPRESA DE DEMOSTRACION (solo dev)
-- ---------------------------------------------------------------------------

INSERT INTO empresa (ruc, razon_social, nombre_comercial, direccion, telefono, email,
                     plan_id, servicio_hasta)
SELECT '20600123456',
       'Lubricentro Demo S.A.C.',
       'LubriTech Demo',
       'Av. Siempre Viva 742, Lima',
       '014567890',
       'contacto@lubritechdemo.pe',
       p.id,
       DATE '2027-12-31'
  FROM plan p WHERE p.codigo = 'PREMIUM'
ON CONFLICT (ruc) DO NOTHING;

INSERT INTO sede (empresa_id, nombre, direccion, telefono, es_principal)
SELECT e.id, 'Sede Principal', 'Av. Siempre Viva 742, Lima', '014567890', true
  FROM empresa e WHERE e.ruc = '20600123456'
ON CONFLICT (empresa_id, nombre) DO NOTHING;

INSERT INTO sede (empresa_id, nombre, direccion, telefono, es_principal)
SELECT e.id, 'Sede Norte', 'Av. Tupac Amaru 1500, Lima', '014567891', false
  FROM empresa e WHERE e.ruc = '20600123456'
ON CONFLICT (empresa_id, nombre) DO NOTHING;

-- SEGUNDA empresa de demostracion. No es relleno: sin dos empresas con
-- datos, el aislamiento entre empresas no se puede PROBAR, solo suponer.
-- El arnes la usa en cada barrido, asi que se conserva.
INSERT INTO empresa (ruc, razon_social, nombre_comercial, direccion, telefono, email,
                     plan_id, servicio_hasta)
SELECT '20600987654',
       'Lubricentro Vecino S.A.C.',
       'LubriTech Vecino',
       'Av. Los Proceres 980, Lima',
       '014567899',
       'contacto@lubritechvecino.pe',
       p.id,
       DATE '2027-12-31'
  FROM plan p WHERE p.codigo = 'PROFESIONAL'
ON CONFLICT (ruc) DO NOTHING;

INSERT INTO sede (empresa_id, nombre, direccion, telefono, es_principal)
SELECT e.id, 'Sede Vecino', 'Av. Los Proceres 980, Lima', '014567899', true
  FROM empresa e WHERE e.ruc = '20600987654'
ON CONFLICT (empresa_id, nombre) DO NOTHING;

INSERT INTO cliente (empresa_id, tipo, tipo_documento, documento, nombres, apellidos,
                     telefono, email, direccion)
SELECT e.id, 'PARTICULAR', 'DNI', '33445566', 'Elena', 'Torres Campos',
       '912345678', 'elena@vecino.pe', 'Jr. Union 45, Lima'
  FROM empresa e WHERE e.ruc = '20600987654'
ON CONFLICT DO NOTHING;

-- Cliente de demostracion (su cuenta del portal la crea el backend).
INSERT INTO cliente (empresa_id, tipo, tipo_documento, documento, nombres, apellidos,
                     telefono, email, direccion)
SELECT e.id, 'PARTICULAR', 'DNI', '44556677', 'Carlos', 'Ramirez Soto',
       '987654321', 'cliente@demo.pe', 'Calle Los Olivos 123, Lima'
  FROM empresa e WHERE e.ruc = '20600123456'
ON CONFLICT DO NOTHING;

-- El pago que respalda ese servicio. Una empresa con servicio hasta 2027 y
-- sin un solo pago registrado es una incoherencia que por la API no se puede
-- producir (registrar el pago ES lo que mueve servicio_hasta), asi que la
-- demo tampoco debe nacer asi: el cuadre de qa/01_candados_consola.sql la
-- marcaria.
INSERT INTO suscripcion (empresa_id, plan_id, desde, hasta, monto, estado, referencia)
SELECT e.id, e.plan_id, DATE '2026-01-01', DATE '2027-12-31',
       p.precio_mensual * 24, 'PAGADA', 'SIEMBRA-DEMO'
  FROM empresa e JOIN plan p ON p.id = e.plan_id
 WHERE e.ruc IN ('20600123456', '20600987654')
   AND NOT EXISTS (SELECT 1 FROM suscripcion s WHERE s.empresa_id = e.id);

COMMIT;

-- Resumen de lo sembrado.
SELECT 'roles'        AS catalogo, count(*) FROM rol
UNION ALL SELECT 'permisos',    count(*) FROM permiso
UNION ALL SELECT 'rol_permiso', count(*) FROM rol_permiso
UNION ALL SELECT 'planes',      count(*) FROM plan
UNION ALL SELECT 'empresas',    count(*) FROM empresa
UNION ALL SELECT 'sedes',       count(*) FROM sede
UNION ALL SELECT 'clientes',    count(*) FROM cliente;
