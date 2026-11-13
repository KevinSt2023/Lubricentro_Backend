# -*- coding: utf-8 -*-
"""
LubriTech · qa/qa_agenda.py — arnes de la Fase 5 (agenda y citas).

Una cita es una orden en CITA_REGISTRADA con fecha. Prueba, contra la API viva:

  * agendar una cita (POST /agenda) la deja en CITA_REGISTRADA con su fecha
  * sin fecha no hay cita: lo rechaza el validador (400) y la base (candado)
  * la cita aparece en el rango de la agenda y no fuera de el
  * permisos: AGENDA.VER para mirar, AGENDA.AGENDAR para agendar
  * reprogramar cambia la fecha, y solo mientras sigue siendo cita
  * recepcion (check-in): la cita pasa a Recibido y deja de estar en la agenda
  * aislamiento entre empresas; cero 500

Uso:
    python qa/qa_agenda.py            # contra http://localhost:9301
    python qa/qa_agenda.py 9311
"""

import json
import subprocess
import sys
import time
from datetime import datetime, timedelta, timezone

import requests

PUERTO = sys.argv[1] if len(sys.argv) > 1 else "9301"
BASE = f"http://localhost:{PUERTO}/api"
CLAVE = "Admin2026*"
_N = int(time.time())
LIMA = timezone(timedelta(hours=-5))

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


def iso(dias, hora=10):
    return (datetime.now(LIMA) + timedelta(days=dias)).replace(
        hour=hora, minute=0, second=0, microsecond=0).isoformat()


def dia(dias):
    return (datetime.now(LIMA) + timedelta(days=dias)).strftime("%Y-%m-%d")


# ===========================================================================
print("\n=== 1. Logins ===")

c, _ = pedir("GET", "/salud")
comprobar(c == 200, "salud 200", f"codigo {c}")

c, ase = login("asesor@lubritechdemo.pe")
comprobar(c == 200, "login Asesor (AGENDA.AGENDAR)", f"codigo {c}")
TK_ASE = ase.get("token") if c == 200 else None
EMP = ase["perfil"]["empresaId"] if c == 200 else None

c, tec = login("tecnico@lubritechdemo.pe")
TK_TEC = tec.get("token") if c == 200 else None

c, alm = login("almacen@lubritechdemo.pe")
TK_SIN = alm.get("token") if c == 200 else None  # almacenero: no tiene AGENDA.VER

c, vec = login("asesor@lubritechvecino.pe")
TK_VEC = vec.get("token") if c == 200 else None
if not TK_VEC:
    c, vec = login("admin@lubritechvecino.pe")
    TK_VEC = vec.get("token") if c == 200 else None

if not TK_ASE:
    print("sin Asesor no hay nada que probar"); sys.exit(1)

SEDE = int(un_valor(f"SELECT id FROM sede WHERE empresa_id={EMP} AND es_principal LIMIT 1;", "0"))


# ===========================================================================
print("\n=== 2. Preparacion: cliente y vehiculo ===")

c, cli = pedir("POST", "/clientes", TK_ASE, {"tipo": "PARTICULAR", "tipoDocumento": "DNI",
    "documento": f"{72000000 + _N % 9999999:08d}", "nombres": "Agenda", "apellidos": f"QA{_N % 10000}",
    "telefono": "999888777", "email": f"agenda.{_N}@correo.pe"})
CLI = cli["id"] if c == 200 else None

c, veh = pedir("POST", "/vehiculos", TK_ASE, {"clienteId": CLI, "placa": f"A{_N % 1000000}",
    "marca": "Toyota", "modelo": "Yaris", "kilometraje": 15000})
VEH = veh["id"] if c == 200 else None
PLACA = veh["placa"] if c == 200 else ""
comprobar(CLI and VEH, "cliente y vehiculo listos", f"cli={CLI} veh={VEH}")


# ===========================================================================
print("\n=== 3. Agendar una cita ===")

c, cita = pedir("POST", "/agenda", TK_ASE, {"clienteId": CLI, "vehiculoId": VEH, "sedeId": SEDE,
    "fechaCita": iso(2), "motivo": "Cambio de aceite y filtros"})
comprobar(c == 200, "se agenda la cita", f"codigo {c} {cita}")
CITA = cita["id"] if c == 200 else None
comprobar(c == 200 and cita["estado"] == "Cita registrada", "la cita nace en Cita registrada", f"estado {cita.get('estado') if c==200 else '-'}")
comprobar(c == 200 and cita.get("fechaCita"), "la cita guarda su fecha")

# Sin fecha: el validador lo rechaza (400)
c, r = pedir("POST", "/agenda", TK_ASE, {"clienteId": CLI, "vehiculoId": VEH, "sedeId": SEDE, "motivo": "sin fecha"})
comprobar(c == 400, "agendar sin fecha se rechaza (400)", f"codigo {c} {cod(r)}")

# El candado de la base: una cita sin fecha, metida por SQL directo, se rechaza
r = psql(f"INSERT INTO orden (empresa_id, sede_id, cliente_id, vehiculo_id, estado_id) "
         f"VALUES ({EMP}, {SEDE}, {CLI}, {VEH}, 1);")
comprobar(r.returncode != 0 and "fecha" in (r.stderr or "").lower(),
          "candado: la base rechaza una cita sin fecha", (r.stderr or "").strip()[:80])


# ===========================================================================
print("\n=== 4. La cita en el calendario ===")

c, lista = pedir("GET", "/agenda", TK_ASE)
comprobar(c == 200 and any(x["id"] == CITA for x in lista), "la cita aparece en la agenda (rango por defecto)", f"codigo {c}")
if c == 200:
    mia = next((x for x in lista if x["id"] == CITA), {})
    comprobar(mia.get("cliente") and mia.get("placa") and mia.get("telefono"),
              "la cita trae cliente, placa y telefono para el calendario")

# Fuera del rango (semana pasada) no aparece
c, fuera = pedir("GET", f"/agenda?desde={iso(-30)}&hasta={iso(-1)}", TK_ASE)
comprobar(c == 200 and all(x["id"] != CITA for x in fuera), "fuera del rango la cita no aparece", f"codigo {c}")


# ===========================================================================
print("\n=== 5. Permisos ===")

c, _ = pedir("GET", "/agenda", TK_TEC)
comprobar(c == 200, "el tecnico (AGENDA.VER) puede mirar la agenda", f"codigo {c}")

c, r = pedir("POST", "/agenda", TK_TEC, {"clienteId": CLI, "vehiculoId": VEH, "sedeId": SEDE, "fechaCita": iso(3)})
comprobar(c == 403, "el tecnico no puede agendar (sin AGENDA.AGENDAR)", f"codigo {c} {cod(r)}")

if TK_SIN:
    c, r = pedir("GET", "/agenda", TK_SIN)
    comprobar(c == 403, "el almacenero no ve la agenda (sin AGENDA.VER)", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 6. Reprogramar ===")

c, r = pedir("PUT", f"/agenda/{CITA}", TK_ASE, {"fechaCita": iso(5, 15)})
comprobar(c == 200, "se reprograma la cita", f"codigo {c} {cod(r)}")
comprobar(c == 200 and r.get("fechaCita", "")[:10] == iso(5, 15)[:10], "la nueva fecha queda guardada", r.get("fechaCita") if c == 200 else "")

# Aislamiento: la empresa vecina no reprograma nuestra cita
if TK_VEC:
    c, r = pedir("PUT", f"/agenda/{CITA}", TK_VEC, {"fechaCita": iso(6)})
    comprobar(c == 404, "la empresa vecina no toca nuestra cita (404)", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 7. Recepcion (check-in) ===")

c, r = pedir("POST", f"/agenda/{CITA}/recibir", TK_ASE, {"kilometrajeIngreso": 15500})
comprobar(c == 200 and r["estado"] == "Recibido", "la cita pasa a Recibido al llegar", f"codigo {c} {cod(r)}")
comprobar(c == 200 and r.get("kilometrajeIngreso") == 15500, "el kilometraje de ingreso queda registrado")

# Ya no es cita: no se reprograma ni se recibe de nuevo
c, r = pedir("PUT", f"/agenda/{CITA}", TK_ASE, {"fechaCita": iso(7)})
comprobar(c == 409 and cod(r) == "NO_ES_CITA", "una cita ya recibida no se reprograma", f"codigo {c} {cod(r)}")

c, r = pedir("POST", f"/agenda/{CITA}/recibir", TK_ASE, {})
comprobar(c == 409 and cod(r) == "NO_ES_CITA", "no se recibe dos veces la misma cita", f"codigo {c} {cod(r)}")

# Y ya no aparece en el calendario (dejo de ser cita)
c, lista = pedir("GET", "/agenda", TK_ASE)
comprobar(c == 200 and all(x["id"] != CITA for x in lista), "la cita recibida sale del calendario")


# ===========================================================================
print("\n=== 8. Cancelar una cita ===")

c, cita2 = pedir("POST", "/agenda", TK_ASE, {"clienteId": CLI, "vehiculoId": VEH, "sedeId": SEDE,
    "fechaCita": iso(4), "motivo": "revision de frenos"})
CITA2 = cita2["id"] if c == 200 else None

c, r = pedir("POST", f"/agenda/{CITA2}/cancelar", TK_ASE, {"motivo": ""})
comprobar(c == 400, "cancelar sin motivo se rechaza", f"codigo {c}")

c, r = pedir("POST", f"/agenda/{CITA2}/cancelar", TK_ASE, {"motivo": "el cliente ya no puede venir"})
comprobar(c == 200 and r["estado"] == "Anulada", "la cita se cancela con motivo", f"codigo {c} {cod(r)}")


# ===========================================================================
print("\n=== 9. Próximos mantenimientos ===")

def fijar_mant(fecha, km):
    return pedir("PUT", f"/vehiculos/{VEH}", TK_ASE, {
        "clienteId": CLI, "placa": PLACA, "marca": "Toyota", "modelo": "Yaris",
        "kilometraje": 15000, "proximoMantFecha": fecha, "proximoMantKm": km})

# Vencido: fecha de ayer y km por debajo del actual (15000)
c, _ = fijar_mant(dia(-1), 14000)
comprobar(c == 200, "se fija el objetivo de mantenimiento del vehiculo", f"codigo {c}")

c, lista = pedir("GET", "/agenda/proximos-mantenimientos", TK_ASE)
mio = next((m for m in lista if m["vehiculoId"] == VEH), None) if c == 200 else None
comprobar(mio is not None and mio["vencido"], "el vehiculo vencido aparece como vencido", f"codigo {c}")
comprobar(mio is not None and mio.get("telefono"), "trae el telefono para llamar al cliente")
comprobar(mio is not None and mio["kmRestantes"] == -1000 and mio["diasRestantes"] < 0,
          "calcula km y dias restantes (negativos = vencido)")

# Por vencer: fecha a 10 dias, km lejos -> aparece pero NO vencido
c, _ = fijar_mant(dia(10), 99999)
c, lista = pedir("GET", "/agenda/proximos-mantenimientos", TK_ASE)
mio = next((m for m in lista if m["vehiculoId"] == VEH), None) if c == 200 else None
comprobar(mio is not None and not mio["vencido"] and mio["diasRestantes"] == 10,
          "un vehiculo por vencer aparece sin estar vencido")

# Lejano: fecha a 2 anios, km lejos -> no aparece todavia
c, _ = fijar_mant(dia(730), 999999)
c, lista = pedir("GET", "/agenda/proximos-mantenimientos", TK_ASE)
comprobar(c == 200 and all(m["vehiculoId"] != VEH for m in lista),
          "un mantenimiento lejano no aparece todavia")

# Permiso: el tecnico (AGENDA.VER) tambien lo ve
if TK_TEC:
    c, _ = pedir("GET", "/agenda/proximos-mantenimientos", TK_TEC)
    comprobar(c == 200, "el tecnico ve los proximos mantenimientos (AGENDA.VER)", f"codigo {c}")


# ===========================================================================
print("\n=== 10. Cuadre ===")

v = un_valor("SELECT count(*) FROM orden o JOIN orden_estado e ON e.id=o.estado_id "
             "WHERE e.codigo='CITA_REGISTRADA' AND o.fecha_cita IS NULL;", "-1")
comprobar(v == "0", "cuadre: ninguna cita quedo sin fecha", f"{v} fila(s)")


# ===========================================================================
print("\n" + "=" * 70)
if fallas:
    print(f"RESULTADO: {len(fallas)} FALLA(S) de {pasadas + len(fallas)} comprobaciones")
    for f in fallas:
        print(f"  - {f}")
    sys.exit(1)
print(f"RESULTADO: {pasadas} comprobaciones, 0 fallas")
