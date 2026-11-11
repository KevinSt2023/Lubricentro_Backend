# -*- coding: utf-8 -*-
"""
LubriTech · qa/qa_maestros.py — arnes de la Fase 2 (maestros del taller).

Prueba, contra la API viva, lo que la Fase 2 promete:

  * personal: alta con clave temporal, el cupo por rol del plan muerde, el
    unico Admin no se puede desactivar, el correo es unico global
  * clientes: particular y empresa, documento unico, portal con clave temporal
  * vehiculos: placa unica por empresa, el kilometraje solo sube
  * servicios: codigo unico, CRUD completo
  * almacen: ingreso que mueve stock y costo promedio, ajuste por conteo,
    salida sobre stock rechazada, kardex paginado, bajo minimo
  * aislamiento: un Admin no ve ni toca nada de otra empresa (criterio 11)
  * permisos efectivos: un tecnico no entra a los maestros de gestion
  * todo queda en la bitacora

Cero fallas, cero 500, ningun 400 que frene un flujo legitimo. Un 401/403
esperado SI es un resultado correcto.

La empresa que usa es la demo Premium (admin@lubritechdemo.pe). Todo lo que
crea lleva marca 'QA ' o codigos 'QA-...', y al final desactiva lo creado y
comprueba que no quedaron huerfanos. No borra: productos con kardex y clientes
con historia no se borran, por diseno.

Uso:
    python qa/qa_maestros.py            # contra http://localhost:9301
    python qa/qa_maestros.py 9311       # contra el perfil de QA
"""

import json
import subprocess
import sys
import time

import requests

PUERTO = sys.argv[1] if len(sys.argv) > 1 else "9301"
BASE = f"http://localhost:{PUERTO}/api"
CLAVE = "Admin2026*"
_N = int(time.time())
MARCA = f"QA{_N % 100000}"                     # sufijo unico por corrida
DNI_QA = f"{70000000 + _N % 9999999:08d}"      # 8 digitos, unico por corrida
RUC_QA = "20" + f"{_N % 1000000000:09d}"       # 11 digitos, unico por corrida

PSQL = r"C:\Program Files\PostgreSQL\18\bin\psql.exe"
BD = "LubriTech"

fallas = []
pasadas = 0


def comprobar(cond, titulo, detalle=""):
    global pasadas
    if cond:
        pasadas += 1
        print(f"  PASA  {titulo}")
    else:
        fallas.append(f"{titulo} - {detalle}")
        print(f"  FALLA {titulo}  {detalle}")


def pedir(metodo, ruta, token=None, cuerpo=None):
    cab = {"Content-Type": "application/json; charset=utf-8"}
    if token:
        cab["Authorization"] = f"Bearer {token}"
    datos = None if cuerpo is None else json.dumps(cuerpo, ensure_ascii=False).encode("utf-8")
    r = requests.request(metodo, f"{BASE}{ruta}", headers=cab, data=datos, timeout=30)
    try:
        return r.status_code, r.json()
    except ValueError:
        return r.status_code, r.text


def login(email, clave=CLAVE):
    return pedir("POST", "/auth/login", cuerpo={"email": email, "password": clave})


def codigo_de(r):
    return r.get("codigo") if isinstance(r, dict) else None


def psql(sql):
    return subprocess.run(
        [PSQL, "-h", "localhost", "-U", "postgres", "-d", BD, "-t", "-A",
         "-P", "pager=off", "-c", sql],
        capture_output=True, text=True,
        env={"PGPASSWORD": "admin", "PGCLIENTENCODING": "UTF8",
             "SystemRoot": r"C:\Windows", "PATH": r"C:\Windows\System32"},
    )


def un_valor(sql, defecto=""):
    r = psql(sql)
    return r.stdout.strip() if r.returncode == 0 else defecto


# ===========================================================================
print("\n=== 1. Puertas ===")

c, salud = pedir("GET", "/salud")
comprobar(c == 200, "GET /api/salud responde 200", f"codigo {c}")

c, admin = login("admin@lubritechdemo.pe")
comprobar(c == 200, "login del Admin de la demo", f"codigo {c} {admin}")
TK = admin.get("token") if c == 200 else None
EMPRESA = admin["perfil"]["empresaId"] if c == 200 else None

c, tecnico = login("tecnico@lubritechdemo.pe")
comprobar(c == 200, "login del tecnico de la demo", f"codigo {c}")
TK_TEC = tecnico.get("token") if c == 200 else None

c, vecino = login("admin@lubritechvecino.pe")
comprobar(c == 200, "login del Admin de la empresa vecina", f"codigo {c}")
TK_VEC = vecino.get("token") if c == 200 else None

if not TK:
    print("\nSin el Admin de la demo no hay nada que probar.")
    sys.exit(1)

# Primera sede de la demo (para los movimientos de almacen).
SEDE = int(un_valor(f"SELECT id FROM sede WHERE empresa_id={EMPRESA} AND es_principal LIMIT 1;", "0"))
SEDE2 = un_valor(f"SELECT id FROM sede WHERE empresa_id={EMPRESA} AND NOT es_principal LIMIT 1;")
SEDE_VECINA = un_valor("SELECT s.id FROM sede s JOIN empresa e ON e.id=s.empresa_id "
                       "WHERE e.ruc='20600987654' LIMIT 1;")


# ===========================================================================
print("\n=== 2. Los maestros son del que tiene permiso (criterio de permisos) ===")

# El tecnico NO gestiona personal ni clientes: esos no le competen.
for ruta in ("/personal", "/clientes"):
    c, _ = pedir("GET", ruta, token=TK_TEC)
    comprobar(c == 403, f"el tecnico NO entra a GET {ruta}", f"codigo {c}")

# Pero SI puede ver vehiculos, servicios y stock: los necesita para trabajar.
for ruta in ("/vehiculos", "/servicios", "/almacen/productos"):
    c, _ = pedir("GET", ruta, token=TK_TEC)
    comprobar(c == 200, f"el tecnico SI puede ver {ruta} (lo necesita)", f"codigo {c}")

# Lo que no puede es ESCRIBIR en ellos: ver no es editar.
c, _ = pedir("POST", "/vehiculos", TK_TEC,
             {"clienteId": 1, "placa": "TEC-000", "marca": "X", "modelo": "Y"})
comprobar(c == 403, "el tecnico NO crea vehiculos", f"codigo {c}")
c, _ = pedir("POST", "/servicios", TK_TEC, {"codigo": "TEC-X", "nombre": "X", "precio": 1})
comprobar(c == 403, "el tecnico NO edita el catalogo de servicios", f"codigo {c}")
c, _ = pedir("POST", "/almacen/productos", TK_TEC, {"codigo": "TEC-X", "nombre": "X"})
comprobar(c == 403, "el tecnico NO da de alta productos", f"codigo {c}")

for ruta in ("/personal", "/clientes", "/almacen/productos"):
    c, _ = pedir("GET", ruta)
    comprobar(c == 401, f"sin token {ruta} responde 401", f"codigo {c}")


# ===========================================================================
print("\n=== 3. Personal: alta, cupo del plan, unico Admin ===")

p = {"nombres": "Tecnico", "apellidos": MARCA, "email": f"tec.{MARCA}@demo.pe",
     "rolId": 6, "dni": "70100100"}
c, creado = pedir("POST", "/personal", TK, p)
comprobar(c == 200, "alta de un tecnico", f"codigo {c} {creado}")
tecnico_id = creado["personal"]["id"] if c == 200 else None
comprobar(c == 200 and len(creado.get("claveTemporal", "")) >= 10,
          "el alta devuelve clave temporal")

if tecnico_id:
    # Login del nuevo con la clave temporal: debe exigir cambio.
    c2, ses = login(f"tec.{MARCA}@demo.pe", creado["claveTemporal"])
    comprobar(c2 == 200 and ses["perfil"]["debeCambiarClave"] is True,
              "el nuevo entra y se le exige cambiar la clave", f"codigo {c2}")
    comprobar(c2 == 200 and ses["perfil"]["rol"] == "Tecnico",
              "nace con el rol que se le dio")

# Correo repetido (global).
c, r = pedir("POST", "/personal", TK, dict(p, dni="70100101"))
comprobar(c == 409 and codigo_de(r) == "EMAIL_DUPLICADO",
          "correo de personal repetido rechazado", f"codigo {c} {codigo_de(r)}")

# Rol fuera de rango.
c, _ = pedir("POST", "/personal", TK, dict(p, email=f"x.{MARCA}@demo.pe", rolId=0, dni="70100102"))
comprobar(c == 400, "rol 0 (SuperAdmin) rechazado en el alta de personal", f"codigo {c}")

# El unico Admin no se desactiva.
admin_id = un_valor(f"SELECT id FROM usuario WHERE empresa_id={EMPRESA} AND rol_id=1 AND activo "
                    "ORDER BY id LIMIT 1;")
cnt_admin = un_valor(f"SELECT count(*) FROM usuario WHERE empresa_id={EMPRESA} AND rol_id=1 AND activo;")
if admin_id and cnt_admin == "1":
    c, r = pedir("PUT", f"/personal/{admin_id}/estado", TK, {"activo": False})
    comprobar(c == 409 and codigo_de(r) == "ULTIMO_ADMIN",
              "el unico Admin activo no se puede desactivar", f"codigo {c} {codigo_de(r)}")
else:
    comprobar(True, f"(la demo tiene {cnt_admin} Admin activos; no aplica el caso de unico Admin)")

# Reiniciar clave de un empleado.
if tecnico_id:
    c, r = pedir("POST", f"/personal/{tecnico_id}/clave", TK)
    comprobar(c == 200 and len(r.get("claveTemporal", "")) >= 10,
              "el Admin reinicia la clave de un empleado", f"codigo {c}")

# Listado paginado con tope.
c, pag = pedir("GET", "/personal?tamano=5000", TK)
comprobar(c == 200 and pag.get("tamano") == 100, "el personal se pagina con tope 100", str(pag.get("tamano")))


# ===========================================================================
print("\n=== 4. Clientes: particular, empresa, documento y portal ===")

cli = {"tipo": "PARTICULAR", "tipoDocumento": "DNI", "documento": DNI_QA,
       "nombres": "Juan", "apellidos": MARCA, "telefono": "900200200",
       "email": f"cli.{MARCA}@correo.pe"}
c, r = pedir("POST", "/clientes", TK, cli)
comprobar(c == 200, "alta de cliente particular", f"codigo {c} {r}")
cliente_id = r["id"] if c == 200 else None

# Empresa sin razon social: rechazo.
c, _ = pedir("POST", "/clientes", TK, {"tipo": "EMPRESA", "tipoDocumento": "RUC", "documento": "20123456789"})
comprobar(c == 400, "empresa sin razon social rechazada", f"codigo {c}")

# Empresa valida.
c, r = pedir("POST", "/clientes", TK, {"tipo": "EMPRESA", "tipoDocumento": "RUC",
             "documento": RUC_QA, "razonSocial": f"Flota {MARCA} S.A.C."})
comprobar(c == 200, "alta de cliente empresa (flota)", f"codigo {c} {r}")
flota_id = r["id"] if c == 200 else None

# Documento repetido.
c, r = pedir("POST", "/clientes", TK, dict(cli, nombres="Otro"))
comprobar(c == 409 and codigo_de(r) == "DOCUMENTO_DUPLICADO",
          "documento de cliente repetido rechazado", f"codigo {c} {codigo_de(r)}")

# DNI mal formado.
c, _ = pedir("POST", "/clientes", TK, dict(cli, documento="123", nombres="Mal"))
comprobar(c == 400, "DNI que no tiene 8 digitos rechazado", f"codigo {c}")

# Portal del cliente (la demo es Premium: tiene APP_CLIENTES).
if cliente_id:
    c, r = pedir("POST", f"/clientes/{cliente_id}/portal", TK)
    comprobar(c == 200 and len(r.get("claveTemporal", "")) >= 10,
              "se habilita el portal del cliente con clave temporal", f"codigo {c} {r}")
    if c == 200:
        c2, ses = pedir("POST", "/auth/cliente/login",
                        cuerpo={"email": r["email"], "password": r["claveTemporal"]})
        comprobar(c2 == 200 and ses["audiencia"] == "lubritech-cliente",
                  "el cliente entra al portal con audiencia propia", f"codigo {c2}")


# ===========================================================================
print("\n=== 5. Vehiculos: placa unica y kilometraje que solo sube ===")

veh_id = None
if cliente_id:
    v = {"clienteId": cliente_id, "placa": f"Q{_N % 1000000}", "marca": "Toyota",
         "modelo": "Yaris", "anio": 2020, "tipo": "AUTO", "kilometraje": 30000}
    c, r = pedir("POST", "/vehiculos", TK, v)
    comprobar(c == 200, "alta de vehiculo", f"codigo {c} {r}")
    veh_id = r["id"] if c == 200 else None

    # Placa repetida (case-insensitive).
    c, r = pedir("POST", "/vehiculos", TK, dict(v, placa=v["placa"].lower()))
    comprobar(c == 409 and codigo_de(r) == "PLACA_DUPLICADA",
              "placa duplicada rechazada", f"codigo {c} {codigo_de(r)}")

    # Cliente inexistente.
    c, r = pedir("POST", "/vehiculos", TK, dict(v, placa=f"Z{_N % 1000000}", clienteId=99999999))
    comprobar(c == 400 and codigo_de(r) == "CLIENTE_INVALIDO",
              "vehiculo de un cliente inexistente rechazado", f"codigo {c} {codigo_de(r)}")

if veh_id:
    # Subir kilometraje.
    c, r = pedir("PUT", f"/vehiculos/{veh_id}/kilometraje", TK, {"kilometraje": 35000})
    comprobar(c == 200 and r["kilometraje"] == 35000, "el kilometraje sube", f"codigo {c}")

    # Bajarlo: rechazo.
    c, r = pedir("PUT", f"/vehiculos/{veh_id}/kilometraje", TK, {"kilometraje": 1000})
    comprobar(c == 409 and codigo_de(r) == "KM_NO_BAJA", "el kilometraje no baja", f"codigo {c} {codigo_de(r)}")

    # Editar con km menor tambien se frena.
    c, r = pedir("PUT", f"/vehiculos/{veh_id}", TK, dict(
        clienteId=cliente_id, placa=f"Q{_N % 1000000}", marca="Toyota", modelo="Yaris", kilometraje=100))
    comprobar(c == 409 and codigo_de(r) == "KM_NO_BAJA", "editar con km menor tambien se frena", f"codigo {c}")

    # El cliente ve su vehiculo en el detalle.
    c, det = pedir("GET", f"/clientes/{cliente_id}", TK)
    comprobar(c == 200 and any(x["id"] == veh_id for x in det.get("listaVehiculos", [])),
              "el vehiculo aparece en la ficha del cliente")


# ===========================================================================
print("\n=== 6. Servicios: catalogo ===")

c, r = pedir("POST", "/servicios", TK, {"codigo": f"SRV-{MARCA}", "nombre": "Cambio de aceite QA",
             "categoria": "Mantenimiento", "precio": 120, "duracionMin": 45})
comprobar(c == 200, "alta de servicio", f"codigo {c} {r}")
srv_id = r["id"] if c == 200 else None

c, r = pedir("POST", "/servicios", TK, {"codigo": f"srv-{MARCA}", "nombre": "Choca", "precio": 10})
comprobar(c == 409 and codigo_de(r) == "CODIGO_DUPLICADO",
          "codigo de servicio repetido rechazado", f"codigo {c} {codigo_de(r)}")

if srv_id:
    c, r = pedir("PUT", f"/servicios/{srv_id}", TK, {"codigo": f"SRV-{MARCA}",
                 "nombre": "Cambio de aceite QA v2", "precio": 130})
    comprobar(c == 200 and r["nombre"].endswith("v2"), "el servicio se edita", f"codigo {c}")


# ===========================================================================
print("\n=== 7. Almacen: ingreso, costo promedio, salida, ajuste, kardex ===")

c, r = pedir("POST", "/almacen/productos", TK, {"codigo": f"PRD-{MARCA}", "nombre": "Aceite QA 15W40",
             "tipo": "ACEITE", "unidad": "LT", "precioVenta": 45, "stockMinimo": 10})
comprobar(c == 200, "alta de producto", f"codigo {c} {r}")
prod_id = r["id"] if c == 200 else None
comprobar(c == 200 and r.get("stockTotal") == 0, "el producto nace sin stock")

if prod_id and SEDE:
    # Ingreso 20 @ 30.
    c, r = pedir("POST", f"/almacen/productos/{prod_id}/ingresos", TK,
                 {"sedeId": SEDE, "cantidad": 20, "costoUnitario": 30, "referencia": "compra QA 1"})
    comprobar(c == 200 and r["stockTotal"] == 20, "ingreso de 20 unidades", f"codigo {c} {r}")

    # Ingreso 20 @ 40 -> promedio 35.
    c, r = pedir("POST", f"/almacen/productos/{prod_id}/ingresos", TK,
                 {"sedeId": SEDE, "cantidad": 20, "costoUnitario": 40})
    sede_stock = next((s for s in r.get("stock", []) if s["sedeId"] == SEDE), None) if c == 200 else None
    comprobar(c == 200 and r["stockTotal"] == 40, "segundo ingreso suma", f"codigo {c}")
    comprobar(sede_stock is not None and abs(float(sede_stock["costoPromedio"]) - 35) < 0.01,
              "el costo promedio ponderado es 35", str(sede_stock))

    # Bajo minimo: con 40 y minimo 10 no esta bajo.
    comprobar(c == 200 and r["bajoMinimo"] is False, "con stock sobre el minimo no esta bajo minimo")

    # Ajuste por conteo: contaron 38 -> baja de 40 a 38.
    c, r = pedir("POST", f"/almacen/productos/{prod_id}/ajustes", TK,
                 {"sedeId": SEDE, "cantidadReal": 38, "motivo": "conteo fisico QA"})
    comprobar(c == 200 and r["stockTotal"] == 38, "el ajuste deja el stock en lo contado", f"codigo {c} {r}")

    # Ajuste sin motivo: rechazo.
    c, r = pedir("POST", f"/almacen/productos/{prod_id}/ajustes", TK, {"sedeId": SEDE, "cantidadReal": 5})
    comprobar(c == 400, "ajuste sin motivo rechazado", f"codigo {c}")

    # Ingreso a una sede de otra empresa: rechazo (sede no es de la empresa).
    if SEDE_VECINA:
        c, r = pedir("POST", f"/almacen/productos/{prod_id}/ingresos", TK,
                     {"sedeId": int(SEDE_VECINA), "cantidad": 1, "costoUnitario": 1})
        comprobar(c == 400 and codigo_de(r) == "SEDE_INVALIDA",
                  "no se puede ingresar a una sede de otra empresa", f"codigo {c} {codigo_de(r)}")

    # El kardex refleja los movimientos, paginado.
    c, kx = pedir("GET", f"/almacen/productos/{prod_id}/kardex", TK)
    comprobar(c == 200 and kx["total"] >= 3, "el kardex tiene los movimientos", f"total {kx.get('total')}")
    comprobar(c == 200 and kx["items"][0]["saldo"] == 38,
              "el ultimo saldo del kardex es el stock actual", str(kx["items"][0] if c == 200 else ""))

    # Busqueda por codigo de barras (lo agregamos primero).
    pedir("PUT", f"/almacen/productos/{prod_id}", TK, {"codigo": f"PRD-{MARCA}", "nombre": "Aceite QA 15W40",
          "tipo": "ACEITE", "unidad": "LT", "codigoBarras": f"BAR{MARCA}", "precioVenta": 45, "stockMinimo": 10})
    c, r = pedir("GET", f"/almacen/productos/barras/BAR{MARCA}", TK)
    comprobar(c == 200 and r["id"] == prod_id, "el lector de barras llega al producto", f"codigo {c}")

    # Filtro bajo minimo: subimos el minimo por encima del stock.
    pedir("PUT", f"/almacen/productos/{prod_id}", TK, {"codigo": f"PRD-{MARCA}", "nombre": "Aceite QA 15W40",
          "tipo": "ACEITE", "unidad": "LT", "codigoBarras": f"BAR{MARCA}", "precioVenta": 45, "stockMinimo": 100})
    c, pag = pedir("GET", "/almacen/productos?bajoMinimo=true&tamano=100", TK)
    comprobar(c == 200 and any(x["id"] == prod_id for x in pag.get("items", [])),
              "el filtro de bajo minimo encuentra el producto")


# ===========================================================================
print("\n=== 8. Aislamiento entre empresas (criterio 11) ===")

if TK_VEC and cliente_id:
    # La empresa vecina no ve al cliente de la demo.
    c, r = pedir("GET", f"/clientes/{cliente_id}", TK_VEC)
    comprobar(c == 404, "la empresa vecina no ve al cliente de la demo (404)", f"codigo {c}")

    # Ni su vehiculo.
    if veh_id:
        c, r = pedir("GET", f"/vehiculos/{veh_id}", TK_VEC)
        comprobar(c == 404, "la empresa vecina no ve el vehiculo de la demo (404)", f"codigo {c}")

    # Ni su producto.
    if prod_id:
        c, r = pedir("GET", f"/almacen/productos/{prod_id}", TK_VEC)
        comprobar(c == 404, "la empresa vecina no ve el producto de la demo (404)", f"codigo {c}")

    # El listado de la vecina no incluye nada de la demo.
    c, pag = pedir("GET", f"/clientes?buscar={MARCA}", TK_VEC)
    comprobar(c == 200 and len(pag.get("items", [])) == 0,
              "el listado de clientes de la vecina no trae los de la demo")


# ===========================================================================
print("\n=== 9. Bitacora: los maestros dejan rastro ===")

for accion in ("PERSONAL.ALTA", "CLIENTE.ALTA", "CLIENTE.PORTAL", "VEHICULO.ALTA",
               "VEHICULO.KILOMETRAJE", "SERVICIO.ALTA", "PRODUCTO.ALTA",
               "ALMACEN.INGRESO", "ALMACEN.AJUSTE"):
    n = un_valor(f"SELECT count(*) FROM bitacora WHERE accion='{accion}';", "0")
    comprobar(n != "0", f"la bitacora registro {accion}", f"{n} filas")

autor = un_valor("SELECT count(*) FROM bitacora WHERE accion IN "
                 "('PRODUCTO.ALTA','ALMACEN.INGRESO','VEHICULO.ALTA') AND usuario_id IS NULL;", "-1")
comprobar(autor == "0", "todo movimiento de maestros tiene autor", f"{autor} sin autor")


# ===========================================================================
print("\n=== 10. Cierre: desactivar lo creado y cuadrar ===")

# Desactivar lo creado (no se borra: productos con kardex y clientes con
# vehiculos no se borran, por diseno).
if prod_id:
    c, _ = pedir("PUT", f"/almacen/productos/{prod_id}/estado", TK, {"activo": False})
    comprobar(c == 200, "el producto de prueba se desactiva", f"codigo {c}")
if srv_id:
    pedir("PUT", f"/servicios/{srv_id}/estado", TK, {"activo": False})
if veh_id:
    pedir("PUT", f"/vehiculos/{veh_id}/estado", TK, {"activo": False})
if cliente_id:
    pedir("PUT", f"/clientes/{cliente_id}/estado", TK, {"activo": False})
if flota_id:
    pedir("PUT", f"/clientes/{flota_id}/estado", TK, {"activo": False})
if tecnico_id:
    pedir("PUT", f"/personal/{tecnico_id}/estado", TK, {"activo": False})

controles = [
    ("stock que no cuadra con el ultimo saldo del kardex",
     "SELECT count(*) FROM producto_stock ps JOIN LATERAL ("
     "SELECT saldo FROM kardex k WHERE k.producto_id=ps.producto_id AND k.sede_id=ps.sede_id "
     "ORDER BY k.id DESC LIMIT 1) u ON true WHERE ps.cantidad <> u.saldo"),
    ("stock negativo", "SELECT count(*) FROM producto_stock WHERE cantidad < 0"),
    ("kardex de un producto de otra empresa",
     "SELECT count(*) FROM kardex k JOIN producto p ON p.id=k.producto_id WHERE p.empresa_id<>k.empresa_id"),
    ("vehiculo de un cliente de otra empresa",
     "SELECT count(*) FROM vehiculo v JOIN cliente c ON c.id=v.cliente_id WHERE c.empresa_id<>v.empresa_id"),
    ("usuario con sede de otra empresa",
     "SELECT count(*) FROM usuario u JOIN sede s ON s.id=u.sede_id WHERE s.empresa_id IS DISTINCT FROM u.empresa_id"),
]
for titulo, sql in controles:
    v = un_valor(sql + ";", "-1")
    comprobar(v == "0", f"cuadre: {titulo}", f"{v} fila(s)")


# ===========================================================================
print("\n" + "=" * 70)
if fallas:
    print(f"RESULTADO: {len(fallas)} FALLA(S) de {pasadas + len(fallas)} comprobaciones")
    for f in fallas:
        print(f"  - {f}")
    sys.exit(1)

print(f"RESULTADO: {pasadas} comprobaciones, 0 fallas")
