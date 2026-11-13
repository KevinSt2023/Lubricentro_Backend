# -*- coding: utf-8 -*-
"""
LubriTech · qa/qa_ordenes.py — arnes de la Fase 3 (ordenes de trabajo).

Prueba, contra la API viva, el ciclo completo de una orden:

  * los 11 estados responden y vienen en orden
  * alta en Recibido, avance de a un estado, rechazo de saltos y retrocesos
  * asignacion de tecnico; el tecnico ve solo sus ordenes (VER_ASIGNADAS)
  * checklist: plantilla por defecto y respuestas guardadas
  * consumo de repuesto En ejecucion -> mueve stock y kardex; fuera de ese
    estado se rechaza; sin stock se rechaza
  * servicios: se agregan, el total cuadra
  * costos y margen solo para quien tiene ORDENES.COSTOS
  * evidencia: valida por bytes (un texto plano disfrazado de foto se rechaza;
    un PNG real entra) y se puede descargar
  * anular exige motivo y cierra la orden; una orden final no cambia
  * historial inmutable con cada paso; aislamiento entre empresas
  * cero 500, cero datos huerfanos

Uso:
    python qa/qa_ordenes.py            # contra http://localhost:9301
    python qa/qa_ordenes.py 9311       # contra el perfil de QA
"""

import base64
import json
import struct
import subprocess
import sys
import time
import zlib

import requests

PUERTO = sys.argv[1] if len(sys.argv) > 1 else "9301"
BASE = f"http://localhost:{PUERTO}/api"
CLAVE = "Admin2026*"
_N = int(time.time())
MARCA = f"QO{_N % 100000}"

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


def png_real():
    """Un PNG 1x1 valido de verdad (firma + IHDR + IDAT + IEND)."""
    def chunk(tipo, datos):
        c = tipo + datos
        return struct.pack(">I", len(datos)) + c + struct.pack(">I", zlib.crc32(c) & 0xffffffff)
    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0))
    idat = chunk(b"IDAT", zlib.compress(b"\x00\xff\xff\xff"))
    iend = chunk(b"IEND", b"")
    return sig + ihdr + idat + iend


# ===========================================================================
print("\n=== 1. Puertas y catalogo de estados ===")

c, _ = pedir("GET", "/salud")
comprobar(c == 200, "salud 200", f"codigo {c}")

c, admin = login("admin@lubritechdemo.pe")
comprobar(c == 200, "login Admin demo", f"codigo {c}")
TK = admin.get("token") if c == 200 else None
EMP = admin["perfil"]["empresaId"] if c == 200 else None

c, ases = login("asesor@lubritechdemo.pe")
TK_ASE = ases.get("token") if c == 200 else None

c, tec = login("tecnico@lubritechdemo.pe")
TK_TEC = tec.get("token") if c == 200 else None
TEC_ID = tec["perfil"]["usuarioId"] if c == 200 else None

c, sup = login("supervisor@lubritechdemo.pe")
TK_SUP = sup.get("token") if c == 200 else None

c, caj = login("cajero@lubritechdemo.pe")
TK_CAJ = caj.get("token") if c == 200 else None

c, vec = login("admin@lubritechvecino.pe")
TK_VEC = vec.get("token") if c == 200 else None

if not TK:
    print("sin Admin no hay nada que probar"); sys.exit(1)

SEDE = int(un_valor(f"SELECT id FROM sede WHERE empresa_id={EMP} AND es_principal LIMIT 1;", "0"))

c, estados = pedir("GET", "/ordenes/estados", TK)
comprobar(c == 200 and len(estados) == 11, "los 11 estados responden", f"codigo {c} n={len(estados) if c==200 else '-'}")
comprobar(c == 200 and estados[0]["codigo"] == "CITA_REGISTRADA" and estados[-1]["codigo"] == "ANULADA",
          "los estados vienen en orden (cita ... anulada)")


# ===========================================================================
print("\n=== 2. Preparacion: cliente, vehiculo y producto con stock ===")

c, cli = pedir("POST", "/clientes", TK, {"tipo": "PARTICULAR", "tipoDocumento": "DNI",
    "documento": f"{70000000 + _N % 9999999:08d}", "nombres": "Ord", "apellidos": MARCA,
    "telefono": "900", "email": f"ord.{MARCA}@correo.pe"})
CLI = cli["id"] if c == 200 else None

c, veh = pedir("POST", "/vehiculos", TK, {"clienteId": CLI, "placa": f"O{_N % 1000000}",
    "marca": "Toyota", "modelo": "Hilux", "kilometraje": 20000})
VEH = veh["id"] if c == 200 else None

c, prod = pedir("POST", "/almacen/productos", TK, {"codigo": f"OP-{MARCA}", "nombre": "Aceite orden QA",
    "tipo": "ACEITE", "unidad": "LT", "precioVenta": 50, "stockMinimo": 2})
PROD = prod["id"] if c == 200 else None
pedir("POST", f"/almacen/productos/{PROD}/ingresos", TK, {"sedeId": SEDE, "cantidad": 20, "costoUnitario": 30})
comprobar(CLI and VEH and PROD, "cliente, vehiculo y producto con stock listos")

# Un vehiculo de la empresa vecina, para el aislamiento.
VEH_VEC = un_valor(f"SELECT id FROM vehiculo v JOIN empresa e ON e.id=v.empresa_id WHERE e.ruc='20600987654' LIMIT 1;")


# ===========================================================================
print("\n=== 3. Alta y avance del ciclo ===")

c, orden = pedir("POST", "/ordenes", TK, {"clienteId": CLI, "vehiculoId": VEH, "sedeId": SEDE,
    "estadoInicial": "RECIBIDO", "kilometrajeIngreso": 25000, "observaciones": "ruido al frenar"})
comprobar(c == 200, "alta de orden en Recibido", f"codigo {c} {orden}")
ORD = orden["id"] if c == 200 else None
comprobar(c == 200 and orden["numero"] >= 1, "la orden recibe numero correlativo")
comprobar(c == 200 and len(orden["historial"]) == 1, "el alta deja el primer renglon de historial")
comprobar(un_valor(f"SELECT kilometraje FROM vehiculo WHERE id={VEH};") == "25000",
          "el km de ingreso actualizo el odometro del vehiculo (20000->25000)")

# vehiculo de otro cliente: rechazo
c, r = pedir("POST", "/ordenes", TK, {"clienteId": CLI, "vehiculoId": int(VEH_VEC or 999999),
    "sedeId": SEDE, "estadoInicial": "RECIBIDO"})
comprobar(c in (400, 404), "no se abre orden con vehiculo ajeno", f"codigo {c} {cod(r)}")

# saltar estado: rechazo
c, r = pedir("PUT", f"/ordenes/{ORD}/estado", TK, {"estado": "EN_EJECUCION"})
comprobar(c == 409 and cod(r) == "SALTO_DE_ESTADO", "saltar de Recibido a Ejecucion se rechaza", f"codigo {c} {cod(r)}")

# avanzar de a uno hasta Ejecucion
for e in ["EN_DIAGNOSTICO", "COTIZACION_ENVIADA", "APROBADA", "EN_EJECUCION"]:
    c, r = pedir("PUT", f"/ordenes/{ORD}/estado", TK, {"estado": e, "observacion": f"paso {e}"})
    comprobar(c == 200 and r["estado"], f"avance a {e}", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 4. Checklist ===")

c, plantilla = pedir("GET", "/ordenes/checklist-plantilla?soloActivos=true", TK)
comprobar(c == 200 and len(plantilla) >= 10, "la plantilla por defecto trae los puntos", f"n={len(plantilla) if c==200 else '-'}")

c, r = pedir("PUT", f"/ordenes/{ORD}/checklist", TK_TEC, {"items": [
    {"item": "Nivel de aceite de motor", "resultado": "OK"},
    {"item": "Pastillas y liquido de frenos", "resultado": "MALO", "nota": "cambiar pastillas"},
    {"item": "Estado y presion de llantas", "resultado": "OBSERVADO"}]})
comprobar(c == 200 and len(r["checklist"]) == 3, "el tecnico guarda el checklist", f"codigo {c} {cod(r)}")

c, r = pedir("PUT", f"/ordenes/{ORD}/checklist", TK_TEC, {"items": [
    {"item": "Frenos", "resultado": "REGULAR"}]})
comprobar(c == 400, "un resultado de checklist invalido se rechaza", f"codigo {c}")


# ===========================================================================
print("\n=== 5. Consumo de repuesto (mueve kardex) ===")

# En ejecucion: consume y baja stock de 20 a 17
c, r = pedir("POST", f"/ordenes/{ORD}/consumos", TK, {"productoId": PROD, "sedeId": SEDE, "cantidad": 3})
comprobar(c == 200, "consumo de 3 unidades En ejecucion", f"codigo {c} {cod(r)}")
comprobar(un_valor(f"SELECT cantidad FROM producto_stock WHERE producto_id={PROD} AND sede_id={SEDE};") == "17.000",
          "el consumo bajo el stock 20->17")
comprobar(c == 200 and len(r["consumos"]) == 1 and float(r["totalRepuestos"]) == 150.0,
          "el total de repuestos cuadra (3 x 50 = 150)", f"{r.get('totalRepuestos')}")

# consumir mas que el stock: rechazo (candado de la base como 409)
c, r = pedir("POST", f"/ordenes/{ORD}/consumos", TK, {"productoId": PROD, "sedeId": SEDE, "cantidad": 9999})
comprobar(c == 409, "consumir mas que el stock se rechaza (409, no 500)", f"codigo {c} {cod(r)}")

# el stock no se movio por el intento fallido
comprobar(un_valor(f"SELECT cantidad FROM producto_stock WHERE producto_id={PROD} AND sede_id={SEDE};") == "17.000",
          "el consumo fallido no dejo el stock a medias")


# ===========================================================================
print("\n=== 6. Servicios y total ===")

c, r = pedir("POST", f"/ordenes/{ORD}/servicios", TK, {"descripcion": "Mano de obra frenos", "precio": 80, "cantidad": 1})
comprobar(c == 200 and float(r["totalServicios"]) == 80.0, "servicio agregado, total servicios 80", f"codigo {c}")
comprobar(c == 200 and float(r["total"]) == 230.0, "el total general suma servicios + repuestos (80+150)")


# ===========================================================================
print("\n=== 7. Costos y margen: solo con ORDENES.COSTOS ===")

# El Admin tiene ORDENES.COSTOS -> ve costo y margen
c, det = pedir("GET", f"/ordenes/{ORD}", TK)
tiene_costo = det["consumos"][0].get("costoUnitario") is not None if c == 200 and det.get("consumos") else False
comprobar(c == 200 and tiene_costo and det.get("costoRepuestos") is not None,
          "el Admin ve costo y margen", f"margen={det.get('margen')}")
comprobar(c == 200 and abs(float(det["margen"]) - (150 - 90)) < 0.01,
          "el margen cuadra (venta 150 - costo 90 = 60)", f"{det.get('margen')}")

# El asesor NO tiene ORDENES.COSTOS -> no ve costo
if TK_ASE:
    c, det2 = pedir("GET", f"/ordenes/{ORD}", TK_ASE)
    sin_costo = c == 200 and (not det2.get("consumos") or det2["consumos"][0].get("costoUnitario") is None)
    comprobar(sin_costo and det2.get("costoRepuestos") is None,
              "el asesor NO ve costos ni margen", f"costoRep={det2.get('costoRepuestos')}")


# ===========================================================================
print("\n=== 8. Evidencias: validacion por bytes ===")

# Un texto plano que se hace pasar por foto: rechazo
falso = base64.b64encode(b"esto no es una imagen, es texto").decode()
c, r = pedir("POST", f"/ordenes/{ORD}/evidencias", TK, {"tipo": "FOTO", "nombre": "falsa.jpg", "contenidoBase64": falso})
comprobar(c == 400 and cod(r) in ("NO_ES_IMAGEN", "TIPO_DESCONOCIDO"),
          "un archivo que no es imagen se rechaza por bytes", f"codigo {c} {cod(r)}")

# Un PNG real: entra
png = base64.b64encode(png_real()).decode()
c, r = pedir("POST", f"/ordenes/{ORD}/evidencias", TK, {"tipo": "FOTO", "nombre": "antes.png", "contenidoBase64": png})
comprobar(c == 200 and r.get("mime") == "image/png", "un PNG real entra como evidencia", f"codigo {c} {r}")
EV = r["id"] if c == 200 else None

if EV:
    # Descargar la evidencia devuelve el binario
    rr = requests.get(f"{BASE}/ordenes/{ORD}/evidencias/{EV}", headers={"Authorization": f"Bearer {TK}"}, timeout=20)
    comprobar(rr.status_code == 200 and rr.headers.get("Content-Type", "").startswith("image/png"),
              "la evidencia se descarga con su tipo real", f"codigo {rr.status_code}")


# ===========================================================================
print("\n=== 9. Asignacion, cola de la sede y toma del tecnico (VER_ASIGNADAS) ===")

c, r = pedir("PUT", f"/ordenes/{ORD}/tecnico", TK, {"tecnicoId": TEC_ID})
comprobar(c == 200 and r["tecnicoId"] == TEC_ID, "se asigna el tecnico", f"codigo {c}")

# Una orden nueva SIN asignar, en la sede del tecnico: es la "cola" que debe ver y poder tomar.
c, pool = pedir("POST", "/ordenes", TK, {"clienteId": CLI, "vehiculoId": VEH, "sedeId": SEDE,
    "estadoInicial": "RECIBIDO", "observaciones": "cola sin asignar"})
POOL = pool["id"] if c == 200 else None
comprobar(c == 200 and pool.get("tecnicoId") is None, "orden de cola creada sin tecnico", f"codigo {c}")

if TK_TEC and POOL:
    # El tecnico tiene VER_ASIGNADAS pero no VER: ve lo suyo + la cola sin asignar de su sede.
    c, pag = pedir("GET", "/ordenes", TK_TEC)
    items = pag.get("items", []) if c == 200 else []
    comprobar(c == 200 and all(o["tecnicoId"] in (TEC_ID, None) for o in items),
              "el tecnico solo ve lo suyo o lo sin asignar de su sede", f"codigo {c}")
    comprobar(any(o["id"] == ORD for o in items), "su orden asignada le aparece")
    comprobar(any(o["id"] == POOL and o["tecnicoId"] is None for o in items),
              "la orden sin asignar de su sede aparece en su cola")

    # Puede abrir el detalle de una orden de su cola (antes de tomarla).
    c, _ = pedir("GET", f"/ordenes/{POOL}", TK_TEC)
    comprobar(c == 200, "el tecnico ve el detalle de una orden de su cola", f"codigo {c}")

    # Toma la orden de la cola: se auto-asigna.
    c, r = pedir("POST", f"/ordenes/{POOL}/tomar", TK_TEC)
    comprobar(c == 200 and r.get("tecnicoId") == TEC_ID, "el tecnico toma la orden de la cola", f"codigo {c} {cod(r)}")

    # Ya es suya: tomarla de nuevo se rechaza.
    c, r = pedir("POST", f"/ordenes/{POOL}/tomar", TK_TEC)
    comprobar(c == 409 and cod(r) == "YA_ASIGNADA", "no se vuelve a tomar una orden ya tomada", f"codigo {c} {cod(r)}")

    # Ahora aparece a su nombre.
    c, pag = pedir("GET", "/ordenes", TK_TEC)
    comprobar(c == 200 and any(o["id"] == POOL and o["tecnicoId"] == TEC_ID for o in pag.get("items", [])),
              "la orden tomada queda a su nombre")

# Un tecnico de OTRA empresa no ve ni toma nuestra cola (aislamiento).
c, tecvec = login("tecnico@lubritechvecino.pe")
TK_TEC_VEC = tecvec.get("token") if c == 200 else None
if TK_TEC_VEC and POOL:
    c, r = pedir("POST", f"/ordenes/{POOL}/tomar", TK_TEC_VEC)
    comprobar(c == 404, "un tecnico de otra empresa no toma nuestra orden (404)", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 10. Cierre del ciclo hasta Entregada ===")

for e in ["CONTROL_CALIDAD", "LISTA", "FACTURADA", "ENTREGADA"]:
    c, r = pedir("PUT", f"/ordenes/{ORD}/estado", TK, {"estado": e})
    comprobar(c == 200, f"avance a {e}", f"codigo {c} {cod(r)}")

# Entregada es final: no cambia mas
c, r = pedir("PUT", f"/ordenes/{ORD}/estado", TK, {"estado": "CITA_REGISTRADA"})
comprobar(c == 409 and cod(r) in ("ORDEN_FINAL", "SALTO_DE_ESTADO"),
          "una orden Entregada no cambia de estado", f"codigo {c} {cod(r)}")

# No se consume en una orden que no esta En ejecucion
c, r = pedir("POST", f"/ordenes/{ORD}/consumos", TK, {"productoId": PROD, "sedeId": SEDE, "cantidad": 1})
comprobar(c == 409 and cod(r) == "ORDEN_NO_EN_EJECUCION",
          "no se consume repuesto fuera de En ejecucion", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 11. Anulacion ===")

# Nueva orden para anular
c, o2 = pedir("POST", "/ordenes", TK, {"clienteId": CLI, "vehiculoId": VEH, "sedeId": SEDE, "estadoInicial": "RECIBIDO"})
ORD2 = o2["id"] if c == 200 else None

c, r = pedir("POST", f"/ordenes/{ORD2}/anular", TK, {"motivo": ""})
comprobar(c == 400, "anular sin motivo se rechaza", f"codigo {c}")

c, r = pedir("POST", f"/ordenes/{ORD2}/anular", TK, {"motivo": "el cliente desistio"})
comprobar(c == 200 and r["estado"] == "Anulada", "se anula con motivo", f"codigo {c} {cod(r)}")

c, r = pedir("PUT", f"/ordenes/{ORD2}/estado", TK, {"estado": "EN_DIAGNOSTICO"})
comprobar(c == 409, "una orden anulada no revive", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 12. Aislamiento y bitacora ===")

if TK_VEC and ORD:
    c, r = pedir("GET", f"/ordenes/{ORD}", TK_VEC)
    comprobar(c == 404, "la empresa vecina no ve la orden de la demo (404)", f"codigo {c}")

for accion in ("ORDEN.ALTA", "ORDEN.ESTADO", "ORDEN.CONSUMO", "ORDEN.SERVICIO",
               "ORDEN.CHECKLIST", "ORDEN.EVIDENCIA", "ORDEN.ASIGNACION", "ORDEN.TOMA", "ORDEN.ANULACION"):
    n = un_valor(f"SELECT count(*) FROM bitacora WHERE accion='{accion}';", "0")
    comprobar(n != "0", f"la bitacora registro {accion}", f"{n} filas")


# ===========================================================================
print("\n=== 13. Cuadre ===")

controles = [
    ("stock que no cuadra con el ultimo saldo del kardex",
     "SELECT count(*) FROM producto_stock ps JOIN LATERAL (SELECT saldo FROM kardex k "
     "WHERE k.producto_id=ps.producto_id AND k.sede_id=ps.sede_id ORDER BY k.id DESC LIMIT 1) u ON true "
     "WHERE ps.cantidad <> u.saldo"),
    ("orden con vehiculo de otra empresa",
     "SELECT count(*) FROM orden o JOIN vehiculo v ON v.id=o.vehiculo_id WHERE v.empresa_id<>o.empresa_id"),
    ("consumo sin movimiento de kardex",
     "SELECT count(*) FROM orden_consumo oc LEFT JOIN kardex k ON k.id=oc.kardex_id "
     "WHERE oc.kardex_id IS NOT NULL AND k.id IS NULL"),
    ("numero de orden repetido por empresa",
     "SELECT count(*) FROM (SELECT empresa_id,numero FROM orden GROUP BY empresa_id,numero HAVING count(*)>1) x"),
    ("orden sin su primer renglon de historial",
     "SELECT count(*) FROM orden o WHERE NOT EXISTS (SELECT 1 FROM orden_historial h WHERE h.orden_id=o.id)"),
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
