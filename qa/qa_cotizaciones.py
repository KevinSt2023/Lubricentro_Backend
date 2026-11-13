# -*- coding: utf-8 -*-
"""
LubriTech · qa/qa_cotizaciones.py — arnes de la Fase 4 (cotizaciones y aprobaciones).

Prueba, contra la API viva, el flujo completo:

  * el tecnico registra un hallazgo; el asesor lo ve
  * el asesor arma una cotizacion versionada (BORRADOR) con sus renglones;
    el subtotal y el total cuadran
  * un borrador se edita; una enviada ya no (se congela)
  * aplicar antes de aprobar se rechaza
  * la version correlativa por orden: una v2 enviada reemplaza a la v1
  * el cliente, desde su portal, ve SOLO sus cotizaciones y decide
  * la decision queda guardada (fecha, decision) y mueve la cotizacion
  * un cliente ajeno no ve ni decide la cotizacion de otro (404)
  * decidir dos veces se rechaza
  * aprobada, los servicios pasan a la orden (el renglon pasa a ejecucion);
    aplicar de nuevo no duplica
  * el rechazo tambien se guarda, y una rechazada no se aplica
  * permisos y audiencias: el tecnico no cotiza; un token de ERP no entra al
    portal y uno de cliente no entra al ERP
  * bitacora de cada paso; cero 500, cero datos huerfanos

Uso:
    python qa/qa_cotizaciones.py            # contra http://localhost:9301
    python qa/qa_cotizaciones.py 9311       # contra el perfil de QA
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
MARCA = f"QC{_N % 100000}"

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


def login(email, ruta="/auth/login", clave=CLAVE):
    return pedir("POST", ruta, cuerpo={"email": email, "password": clave})


def cod(r):
    return r.get("codigo") if isinstance(r, dict) else None


def psql(sql):
    return subprocess.run(
        [PSQL, "-h", "localhost", "-U", "postgres", "-d", BD, "-t", "-A",
         "-P", "pager=off", "-c", sql],
        capture_output=True, text=True,
        env={"PGPASSWORD": "admin", "PGCLIENTENCODING": "UTF8",
             "SystemRoot": r"C:\Windows", "PATH": r"C:\Windows\System32"})


def un_valor(sql, d=""):
    r = psql(sql)
    return r.stdout.strip() if r.returncode == 0 else d


# ===========================================================================
print("\n=== 1. Puertas: personal y portal del cliente ===")

c, _ = pedir("GET", "/salud")
comprobar(c == 200, "salud 200", f"codigo {c}")

c, admin = login("admin@lubritechdemo.pe")
comprobar(c == 200, "login Admin demo", f"codigo {c}")
TK = admin.get("token") if c == 200 else None
EMP = admin["perfil"]["empresaId"] if c == 200 else None

c, ase = login("asesor@lubritechdemo.pe")
TK_ASE = ase.get("token") if c == 200 else None

c, tec = login("tecnico@lubritechdemo.pe")
TK_TEC = tec.get("token") if c == 200 else None

c, cliente = login("cliente@demo.pe", "/auth/cliente/login")
comprobar(c == 200 and cliente.get("token"), "login portal del cliente (cliente@demo.pe)", f"codigo {c}")
TK_CLI = cliente.get("token") if c == 200 else None
CLI_PORTAL = cliente["perfil"]["clienteId"] if c == 200 else None

c, vecino = login("elena@vecino.pe", "/auth/cliente/login")
TK_CLI_VEC = vecino.get("token") if c == 200 else None

if not (TK and TK_ASE and TK_CLI and CLI_PORTAL):
    print("sin las cuentas base no hay nada que probar"); sys.exit(1)

SEDE = int(un_valor(f"SELECT id FROM sede WHERE empresa_id={EMP} AND es_principal LIMIT 1;", "0"))


# ===========================================================================
print("\n=== 2. Preparacion: vehiculo del cliente del portal y una orden ===")

c, veh = pedir("POST", "/vehiculos", TK, {"clienteId": CLI_PORTAL, "placa": f"C{_N % 1000000}",
    "marca": "Nissan", "modelo": "Versa", "kilometraje": 30000})
VEH = veh["id"] if c == 200 else None

c, orden = pedir("POST", "/ordenes", TK, {"clienteId": CLI_PORTAL, "vehiculoId": VEH, "sedeId": SEDE,
    "estadoInicial": "RECIBIDO", "observaciones": "revision general"})
ORD = orden["id"] if c == 200 else None
pedir("PUT", f"/ordenes/{ORD}/estado", TK, {"estado": "EN_DIAGNOSTICO"})
comprobar(VEH and ORD, "vehiculo del cliente y orden en diagnostico listos", f"veh={VEH} ord={ORD}")


# ===========================================================================
print("\n=== 3. Hallazgo del tecnico ===")

c, h = pedir("POST", f"/ordenes/{ORD}/hallazgos", TK_TEC,
             {"descripcion": "Pastillas de freno al limite", "severidad": "CRITICO"})
comprobar(c == 200, "el tecnico registra un hallazgo", f"codigo {c} {cod(h)}")

c, hs = pedir("GET", f"/ordenes/{ORD}/hallazgos", TK_ASE)
comprobar(c == 200 and any(x["severidad"] == "CRITICO" for x in hs), "el asesor ve el hallazgo", f"codigo {c}")

# el asesor no tiene COTIZACION.HALLAZGO
c, r = pedir("POST", f"/ordenes/{ORD}/hallazgos", TK_ASE, {"descripcion": "x", "severidad": "LEVE"})
comprobar(c == 403, "el asesor no registra hallazgos (sin permiso)", f"codigo {c}")


# ===========================================================================
print("\n=== 4. El asesor arma la cotizacion (borrador) ===")

c, cot = pedir("POST", f"/ordenes/{ORD}/cotizaciones", TK_ASE, {"nota": "cambio de pastillas", "renglones": [
    {"tipo": "SERVICIO", "descripcion": "Mano de obra frenos", "cantidad": 1, "precioUnitario": 90},
    {"tipo": "REPUESTO", "descripcion": "Juego de pastillas", "cantidad": 2, "precioUnitario": 55}]})
comprobar(c == 200 and cot["estado"] == "BORRADOR", "cotizacion creada en borrador", f"codigo {c} {cod(cot)}")
COT = cot["id"] if c == 200 else None
comprobar(c == 200 and float(cot["total"]) == 200.0, "el total cuadra (90 + 2x55 = 200)", f"{cot.get('total')}")
comprobar(c == 200 and cot["version"] == 1, "la primera version es la 1")
comprobar(c == 200 and any(float(x["subtotal"]) == 110.0 for x in cot["renglones"]),
          "el subtotal del renglon lo calcula la base (2 x 55 = 110)")

# el tecnico no puede armar cotizaciones
c, r = pedir("POST", f"/ordenes/{ORD}/cotizaciones", TK_TEC, {"renglones": [
    {"tipo": "SERVICIO", "descripcion": "x", "cantidad": 1, "precioUnitario": 1}]})
comprobar(c == 403, "el tecnico no arma cotizaciones (sin permiso)", f"codigo {c}")

# editar el borrador: baja la cantidad de pastillas a 1
c, r = pedir("PUT", f"/cotizaciones/{COT}", TK_ASE, {"nota": "ajuste", "renglones": [
    {"tipo": "SERVICIO", "descripcion": "Mano de obra frenos", "cantidad": 1, "precioUnitario": 90},
    {"tipo": "REPUESTO", "descripcion": "Juego de pastillas", "cantidad": 1, "precioUnitario": 55}]})
comprobar(c == 200 and float(r["total"]) == 145.0, "el borrador se edita (nuevo total 145)", f"codigo {c} {r.get('total')}")

# aplicar antes de aprobar: rechazo
c, r = pedir("POST", f"/cotizaciones/{COT}/aplicar", TK_ASE)
comprobar(c == 409 and cod(r) == "NO_APROBADA", "no se aplica una cotizacion sin aprobar", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 5. Enviar y versionar ===")

c, r = pedir("POST", f"/cotizaciones/{COT}/enviar", TK_ASE)
comprobar(c == 200 and r["estado"] == "ENVIADA", "la cotizacion se envia", f"codigo {c} {cod(r)}")

# una enviada ya no se edita
c, r = pedir("PUT", f"/cotizaciones/{COT}", TK_ASE, {"renglones": [
    {"tipo": "SERVICIO", "descripcion": "otra cosa", "cantidad": 1, "precioUnitario": 10}]})
comprobar(c == 409 and cod(r) == "COTIZACION_CONGELADA", "una cotizacion enviada no se edita", f"codigo {c} {cod(r)}")

# una segunda version reemplaza a la primera al enviarse
c, cot2 = pedir("POST", f"/ordenes/{ORD}/cotizaciones", TK_ASE, {"nota": "v2 con alineacion", "renglones": [
    {"tipo": "SERVICIO", "descripcion": "Mano de obra frenos", "cantidad": 1, "precioUnitario": 90},
    {"tipo": "REPUESTO", "descripcion": "Juego de pastillas", "cantidad": 1, "precioUnitario": 55},
    {"tipo": "SERVICIO", "descripcion": "Alineacion", "cantidad": 1, "precioUnitario": 40}]})
COT2 = cot2["id"] if c == 200 else None
comprobar(c == 200 and cot2["version"] == 2, "la segunda version es la 2", f"codigo {c}")

c, r = pedir("POST", f"/cotizaciones/{COT2}/enviar", TK_ASE)
comprobar(c == 200 and r["estado"] == "ENVIADA", "se envia la v2", f"codigo {c} {cod(r)}")
c, v1 = pedir("GET", f"/cotizaciones/{COT}", TK_ASE)
comprobar(c == 200 and v1["estado"] == "REEMPLAZADA", "la v1 quedo REEMPLAZADA al enviar la v2", f"estado {v1.get('estado')}")
comprobar(un_valor(f"SELECT count(*) FROM cotizacion WHERE orden_id={ORD} AND estado='ENVIADA';") == "1",
          "solo hay una cotizacion enviada en la orden")


# ===========================================================================
print("\n=== 6. El cliente ve y decide desde su portal ===")

c, mias = pedir("GET", "/portal/cotizaciones", TK_CLI)
comprobar(c == 200 and any(x["id"] == COT2 for x in mias), "el cliente ve su cotizacion pendiente", f"codigo {c}")

# un token del ERP no entra al portal
c, r = pedir("GET", "/portal/cotizaciones", TK)
comprobar(c == 403 and cod(r) == "AUDIENCIA", "un token del ERP no entra al portal del cliente", f"codigo {c} {cod(r)}")

# el cliente vecino no ve ni abre esta cotizacion
if TK_CLI_VEC:
    c, aj = pedir("GET", "/portal/cotizaciones", TK_CLI_VEC)
    comprobar(c == 200 and all(x["id"] != COT2 for x in aj), "el cliente vecino no ve la cotizacion ajena")
    c, r = pedir("GET", f"/portal/cotizaciones/{COT2}", TK_CLI_VEC)
    comprobar(c == 404, "abrir una cotizacion ajena da 404 (no 403 con datos)", f"codigo {c}")

# un token de cliente no entra al ERP
c, r = pedir("GET", f"/cotizaciones/{COT2}", TK_CLI)
comprobar(c == 403, "un token de cliente no entra a los endpoints del ERP", f"codigo {c}")

# el cliente aprueba
c, r = pedir("POST", f"/portal/cotizaciones/{COT2}/decision", TK_CLI,
             {"decision": "APROBADA", "comentario": "procedan"})
comprobar(c == 200 and r["estado"] == "APROBADA" and r.get("decision"),
          "el cliente aprueba y la decision queda guardada", f"codigo {c} {cod(r)}")
comprobar(c == 200 and r["decision"]["decision"] == "APROBADA" and r.get("decididaEn"),
          "la decision guarda su valor y su fecha")

# decidir de nuevo: rechazo
c, r = pedir("POST", f"/portal/cotizaciones/{COT2}/decision", TK_CLI, {"decision": "RECHAZADA"})
comprobar(c == 409 and cod(r) == "NO_PENDIENTE", "no se decide dos veces", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 7. Aprobada -> el renglon pasa a ejecucion ===")

c, ped = pedir("GET", f"/ordenes/{ORD}", TK)
serv_antes = float(ped["totalServicios"]) if c == 200 else 0

c, r = pedir("POST", f"/cotizaciones/{COT2}/aplicar", TK_ASE)
comprobar(c == 200, "la cotizacion aprobada se aplica a la orden", f"codigo {c} {cod(r)}")

c, ped = pedir("GET", f"/ordenes/{ORD}", TK)
serv_desp = float(ped["totalServicios"]) if c == 200 else 0
# se aplican los 2 servicios aprobados: mano de obra 90 + alineacion 40 = 130
comprobar(abs((serv_desp - serv_antes) - 130.0) < 0.01,
          "los servicios aprobados entraron a la orden (+130)", f"antes {serv_antes} despues {serv_desp}")
comprobar(un_valor(f"SELECT count(*) FROM orden_servicio WHERE orden_id={ORD} AND cotizacion_renglon_id IS NOT NULL;") == "2",
          "los renglones de la orden quedan vinculados a la cotizacion")

# aplicar de nuevo no duplica
c, r = pedir("POST", f"/cotizaciones/{COT2}/aplicar", TK_ASE)
comprobar(c == 409 and cod(r) == "NADA_QUE_APLICAR", "aplicar dos veces no duplica", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 8. El rechazo tambien se guarda ===")

c, o2 = pedir("POST", "/ordenes", TK, {"clienteId": CLI_PORTAL, "vehiculoId": VEH, "sedeId": SEDE,
    "estadoInicial": "RECIBIDO"})
ORD2 = o2["id"] if c == 200 else None
c, cr = pedir("POST", f"/ordenes/{ORD2}/cotizaciones", TK_ASE, {"renglones": [
    {"tipo": "SERVICIO", "descripcion": "Cambio de embrague", "cantidad": 1, "precioUnitario": 800}]})
COT3 = cr["id"] if c == 200 else None
pedir("POST", f"/cotizaciones/{COT3}/enviar", TK_ASE)

c, r = pedir("POST", f"/portal/cotizaciones/{COT3}/decision", TK_CLI,
             {"decision": "RECHAZADA", "comentario": "muy caro"})
comprobar(c == 200 and r["estado"] == "RECHAZADA", "el cliente rechaza y queda guardado", f"codigo {c} {cod(r)}")

c, r = pedir("POST", f"/cotizaciones/{COT3}/aplicar", TK_ASE)
comprobar(c == 409 and cod(r) == "NO_APROBADA", "una cotizacion rechazada no se aplica", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 9. Bitacora ===")

for accion in ("COTIZACION.HALLAZGO", "COTIZACION.ALTA", "COTIZACION.ENVIO",
               "COTIZACION.DECISION", "COTIZACION.APLICA"):
    n = un_valor(f"SELECT count(*) FROM bitacora WHERE accion='{accion}';", "0")
    comprobar(n != "0", f"la bitacora registro {accion}", f"{n} filas")


# ===========================================================================
print("\n=== 10. Cuadre ===")

controles = [
    ("cotizacion con orden de otra empresa",
     "SELECT count(*) FROM cotizacion c JOIN orden o ON o.id=c.orden_id WHERE o.empresa_id<>c.empresa_id"),
    ("version de cotizacion repetida en una orden",
     "SELECT count(*) FROM (SELECT orden_id,version FROM cotizacion GROUP BY orden_id,version HAVING count(*)>1) x"),
    ("mas de una cotizacion enviada por orden",
     "SELECT count(*) FROM (SELECT orden_id FROM cotizacion WHERE estado='ENVIADA' GROUP BY orden_id HAVING count(*)>1) y"),
    ("cotizacion decidida sin su fila de decision",
     "SELECT count(*) FROM cotizacion c WHERE c.estado IN ('APROBADA','RECHAZADA') "
     "AND NOT EXISTS (SELECT 1 FROM cotizacion_decision d WHERE d.cotizacion_id=c.id)"),
    ("renglon de orden cotizado sin aprobacion",
     "SELECT count(*) FROM orden_servicio os JOIN cotizacion_renglon r ON r.id=os.cotizacion_renglon_id "
     "JOIN cotizacion c ON c.id=r.cotizacion_id WHERE c.estado<>'APROBADA'"),
    ("mas de una decision por cotizacion",
     "SELECT count(*) FROM (SELECT cotizacion_id FROM cotizacion_decision GROUP BY cotizacion_id HAVING count(*)>1) z"),
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
