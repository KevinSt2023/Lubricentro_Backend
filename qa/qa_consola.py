# -*- coding: utf-8 -*-
"""
LubriTech · qa/qa_consola.py — arnes de la Fase 1 (consola del SaaS).

Prueba, contra la API viva, lo que la Fase 1 promete:

  * la consola es solo del SuperAdmin: un Admin de empresa no la ve
  * el alta de una empresa crea empresa + sede principal + Admin de una vez,
    y devuelve la clave temporal UNA sola vez
  * el RUC es unico y no se puede editar; el correo del personal es unico global
  * el tope de sedes del plan muerde, y la sede principal esta protegida
  * el permiso efectivo es rol INTERSECCION plan: al bajar de plan, el mismo
    Admin pierde SEDES.GESTIONAR sin que nadie le toque el rol
  * un cambio de plan que dejaria a la empresa excedida se rechaza con 409
  * el pago encadena con el periodo anterior y no se pierde lo pagado;
    anularlo devuelve servicio_hasta a donde estaba
  * dar de baja una empresa corta el acceso al instante, y reactivarla lo devuelve
  * todo lo anterior queda en la bitacora

Lo que se exige: cero fallas, cero 500, y ningun 400 que frene un flujo
legitimo. Un 401/403 esperado SI es un resultado correcto.

La empresa de pruebas (RUC 20777000001) se CONSERVA entre barridos: el arnes
la crea la primera vez y la reutiliza despues, y termina dejandola como la
encontro (plan PROFESIONAL, dos sedes, activa). No deja pagos sueltos.

Uso:
    python qa/qa_consola.py            # contra http://localhost:9301
    python qa/qa_consola.py 9311       # contra el perfil de QA
"""

import json
import subprocess
import sys

import requests

PUERTO = sys.argv[1] if len(sys.argv) > 1 else "9301"
BASE = f"http://localhost:{PUERTO}/api"
CLAVE = "Admin2026*"

PSQL = r"C:\Program Files\PostgreSQL\18\bin\psql.exe"
BD = "LubriTech"

RUC_QA = "20777000001"
EMAIL_QA = "admin@lubritechqa.pe"
SEDE_1 = "Sede QA"
SEDE_2 = "Sede QA Norte"

fallas = []
pasadas = 0


def comprobar(condicion, titulo, detalle=""):
    global pasadas
    if condicion:
        pasadas += 1
        print(f"  PASA  {titulo}")
    else:
        fallas.append(f"{titulo} - {detalle}")
        print(f"  FALLA {titulo}  {detalle}")


def pedir(metodo, ruta, token=None, cuerpo=None, app=None):
    """Devuelve (codigo, json_o_texto). Nunca lanza por un codigo de error."""
    cabeceras = {"Content-Type": "application/json; charset=utf-8"}
    if token:
        cabeceras["Authorization"] = f"Bearer {token}"
    if app:
        cabeceras["X-App"] = app

    datos = None
    if cuerpo is not None:
        # El JSON se serializa aqui y se manda como bytes: nunca texto con
        # tildes pegado en la linea de comandos.
        datos = json.dumps(cuerpo, ensure_ascii=False).encode("utf-8")

    r = requests.request(metodo, f"{BASE}{ruta}", headers=cabeceras, data=datos, timeout=30)
    try:
        return r.status_code, r.json()
    except ValueError:
        return r.status_code, r.text


def login(email, clave=CLAVE):
    return pedir("POST", "/auth/login", cuerpo={"email": email, "password": clave})


def clave_de(respuesta):
    """El codigo de negocio que devuelve la API cuando rechaza algo."""
    return respuesta.get("codigo") if isinstance(respuesta, dict) else None


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
print("\n=== 1. La API esta arriba y las puertas abren ===")

codigo, salud = pedir("GET", "/salud")
comprobar(codigo == 200, "GET /api/salud responde 200", f"codigo {codigo}")

codigo, sa = login("superadmin@lubritech.pe")
comprobar(codigo == 200, "login del SuperAdmin", f"codigo {codigo} {sa}")
token_sa = sa.get("token") if codigo == 200 else None

codigo, admin_demo = login("admin@lubritechdemo.pe")
comprobar(codigo == 200, "login del Admin de la empresa demo", f"codigo {codigo}")
token_demo = admin_demo.get("token") if codigo == 200 else None
empresa_demo = admin_demo["perfil"]["empresaId"] if codigo == 200 else None

codigo, tecnico = login("tecnico@lubritechdemo.pe")
comprobar(codigo == 200, "login del tecnico de la empresa demo", f"codigo {codigo}")
token_tecnico = tecnico.get("token") if codigo == 200 else None

if not token_sa:
    print("\nSin token del SuperAdmin no hay nada que probar. Se corta aqui.")
    sys.exit(1)


# ===========================================================================
print("\n=== 2. La consola es solo del SuperAdmin ===")

for ruta in ("/sa/empresas", "/sa/planes", "/sa/tablero", "/sa/suscripciones"):
    codigo, _ = pedir("GET", ruta, token=token_demo)
    comprobar(codigo == 403, f"el Admin de empresa NO entra a GET {ruta}", f"codigo {codigo}")

codigo, _ = pedir("GET", "/sa/empresas", token=token_tecnico)
comprobar(codigo == 403, "el tecnico NO entra a la consola", f"codigo {codigo}")

codigo, _ = pedir("GET", "/sa/empresas")
comprobar(codigo == 401, "sin token la consola responde 401", f"codigo {codigo}")

if empresa_demo:
    codigo, _ = pedir("PUT", f"/sa/empresas/{empresa_demo}/estado", token=token_demo,
                      cuerpo={"activo": False, "motivo": "intento de QA"})
    comprobar(codigo == 403, "un Admin NO puede dar de baja a su propia empresa", f"codigo {codigo}")


# ===========================================================================
print("\n=== 3. Catalogo de funciones y planes ===")

codigo, funciones = pedir("GET", "/sa/planes/funciones", token=token_sa)
comprobar(codigo == 200 and isinstance(funciones, list) and len(funciones) >= 8,
          "el catalogo de funciones vendibles responde", f"codigo {codigo}")

codigos_funcion = {f["codigo"] for f in funciones} if isinstance(funciones, list) else set()
comprobar({"APP_OPERACIONES", "APP_CLIENTES", "MULTISEDE"} <= codigos_funcion,
          "estan las funciones de las apps moviles y multisede", str(sorted(codigos_funcion))[:150])

codigo, planes = pedir("GET", "/sa/planes", token=token_sa)
comprobar(codigo == 200 and isinstance(planes, list) and len(planes) == 3,
          "los tres planes responden", f"codigo {codigo}")

por_codigo = {p["codigo"]: p for p in planes} if isinstance(planes, list) else {}

comprobar("MULTISEDE" not in por_codigo.get("BASICO", {}).get("funciones", []),
          "el plan Basico NO incluye multisede")
comprobar("MULTISEDE" in por_codigo.get("PROFESIONAL", {}).get("funciones", []),
          "el plan Profesional SI incluye multisede")
comprobar(por_codigo.get("PREMIUM", {}).get("maxSedes") == 0,
          "el plan Premium no tiene tope de sedes",
          str(por_codigo.get("PREMIUM", {}).get("maxSedes")))

basico = por_codigo.get("BASICO", {})
cupos_basico = {c["rolId"]: c["cupo"] for c in basico.get("cupos", [])}
comprobar(0 not in cupos_basico, "ningun plan lleva cupo del rol SuperAdmin")
comprobar(cupos_basico.get(1) == 1, "el Basico habilita un solo Admin", str(cupos_basico))

# Guardar un plan con cupo del rol 0 se rechaza antes de llegar a la base.
if basico:
    malo = dict(basico)
    malo["cupos"] = basico.get("cupos", []) + [{"rolId": 0, "cupo": 1}]
    codigo, r = pedir("PUT", f"/sa/planes/{basico['id']}", token=token_sa, cuerpo=malo)
    comprobar(codigo == 400, "un cupo para el rol SuperAdmin se rechaza",
              f"codigo {codigo} {clave_de(r)}")

    malo = dict(basico)
    malo["funciones"] = basico.get("funciones", []) + ["INVENTADA"]
    codigo, r = pedir("PUT", f"/sa/planes/{basico['id']}", token=token_sa, cuerpo=malo)
    comprobar(codigo == 400 and clave_de(r) == "FUNCION_INVALIDA",
              "una funcion fuera del catalogo se rechaza", f"codigo {codigo} {clave_de(r)}")

    # Bajar el Basico a 0 sedes dejaria excedida a cualquier empresa suya.
    codigo, r = pedir("GET", f"/sa/planes/{basico['id']}", token=token_sa)
    comprobar(codigo == 200 and r.get("codigo") == "BASICO",
              "el detalle de un plan responde", f"codigo {codigo}")


# ===========================================================================
print("\n=== 4. Listado de empresas: paginacion y filtros ===")

codigo, pagina = pedir("GET", "/sa/empresas?pagina=1&tamano=5000", token=token_sa)
comprobar(codigo == 200 and pagina.get("tamano") == 100,
          "el tope duro de pagina es 100 aunque pidan 5000", str(pagina.get("tamano")))
comprobar(codigo == 200 and "total" in pagina and "items" in pagina,
          "el listado viene paginado (items + total)")

total_api = pagina.get("total", -1)
total_bd = int(un_valor("SELECT count(*) FROM empresa;", "-2"))
comprobar(total_api == total_bd, "el total del listado cuadra con la base",
          f"api {total_api} vs base {total_bd}")

codigo, pagina = pedir("GET", "/sa/empresas?tamano=1", token=token_sa)
comprobar(codigo == 200 and len(pagina.get("items", [])) == 1,
          "tamano=1 devuelve una sola empresa", str(len(pagina.get("items", []))))

codigo, pagina = pedir("GET", "/sa/empresas?buscar=Vecino", token=token_sa)
comprobar(codigo == 200 and any(i["ruc"] == "20600987654" for i in pagina.get("items", [])),
          "la busqueda por nombre encuentra a la empresa vecina", str(pagina)[:150])

codigo, pagina = pedir("GET", "/sa/empresas?estado=VENCIDO", token=token_sa)
comprobar(codigo == 200 and all(i["estadoServicio"] == "VENCIDO" for i in pagina.get("items", [])),
          "el filtro por estado del servicio recorta de verdad")

codigo, pagina = pedir("GET", "/sa/empresas?estado=AL_DIA", token=token_sa)
comprobar(codigo == 200 and all(i["estadoServicio"] == "AL_DIA" for i in pagina.get("items", [])),
          "el filtro AL_DIA solo trae empresas al dia")

if empresa_demo:
    codigo, detalle = pedir("GET", f"/sa/empresas/{empresa_demo}", token=token_sa)
    comprobar(codigo == 200 and detalle.get("plan") and detalle.get("listaSedes"),
              "el detalle de una empresa trae plan y sedes", f"codigo {codigo}")
    comprobar(codigo == 200 and detalle.get("admin", {}).get("email") == "admin@lubritechdemo.pe",
              "el detalle identifica al Admin de la empresa")
    comprobar(codigo == 200 and all(u["cupo"] == 0 for u in detalle.get("usoPorRol", [])),
              "en Premium ningun rol tiene tope")

codigo, _ = pedir("GET", "/sa/empresas/999999", token=token_sa)
comprobar(codigo == 404, "una empresa que no existe responde 404", f"codigo {codigo}")


# ===========================================================================
print("\n=== 5. Alta de la empresa de pruebas y los candados del alta ===")

alta = {
    "ruc": RUC_QA,
    "razonSocial": "Lubricentro QA S.A.C.",
    "nombreComercial": "LubriTech QA",
    "direccion": "Av. de las Pruebas 100, Lima",
    "telefono": "014000000",
    "email": "contacto@lubritechqa.pe",
    "planCodigo": "PROFESIONAL",
    "diasDeCortesia": 7,
    "admin": {
        "dni": "70000001",
        "nombres": "Ana",
        "apellidos": "Prueba Torres",
        "email": EMAIL_QA,
        "telefono": "900000001",
    },
    "sedePrincipal": {"nombre": SEDE_1, "direccion": "Av. de las Pruebas 100, Lima"},
}

codigo, creada = pedir("POST", "/sa/empresas", token=token_sa, cuerpo=alta)

if codigo == 200:
    comprobar(True, "alta completa de la empresa de pruebas")
    comprobar(len(creada.get("claveTemporal", "")) >= 10,
              "el alta devuelve una clave temporal", str(creada.get("claveTemporal")))
    comprobar(creada.get("empresa", {}).get("sedes") == 1,
              "el alta deja la sede principal creada")
    empresa_qa = creada["empresa"]["id"]
else:
    comprobar(codigo == 409 and clave_de(creada) in ("RUC_DUPLICADO", "EMAIL_DUPLICADO"),
              "la empresa de pruebas ya existia y se reutiliza",
              f"codigo {codigo} {clave_de(creada)}")
    codigo, pagina = pedir("GET", f"/sa/empresas?buscar={RUC_QA}", token=token_sa)
    items = pagina.get("items", []) if codigo == 200 else []
    empresa_qa = items[0]["id"] if items else None

comprobar(empresa_qa is not None, "la empresa de pruebas esta disponible")

if empresa_qa is None:
    print("\nSin la empresa de pruebas no se puede seguir. Se corta aqui.")
    sys.exit(1)

# El RUC es unico.
codigo, r = pedir("POST", "/sa/empresas", token=token_sa, cuerpo=alta)
comprobar(codigo == 409 and clave_de(r) == "RUC_DUPLICADO",
          "un RUC repetido se rechaza con 409", f"codigo {codigo} {clave_de(r)}")

# El correo del personal es unico en TODO el sistema: si no, el login por
# correo seria ambiguo.
otro = json.loads(json.dumps(alta))
otro["ruc"] = "20777000002"
codigo, r = pedir("POST", "/sa/empresas", token=token_sa, cuerpo=otro)
comprobar(codigo == 409 and clave_de(r) == "EMAIL_DUPLICADO",
          "un correo de personal repetido se rechaza con 409", f"codigo {codigo} {clave_de(r)}")

# Validacion de entrada: RUC corto, correo invalido, cortesia absurda.
malo = json.loads(json.dumps(alta))
malo["ruc"] = "2077700"
codigo, _ = pedir("POST", "/sa/empresas", token=token_sa, cuerpo=malo)
comprobar(codigo == 400, "un RUC que no tiene 11 digitos se rechaza", f"codigo {codigo}")

malo = json.loads(json.dumps(alta))
malo["ruc"] = "20777000003"
malo["admin"]["email"] = "esto-no-es-un-correo"
codigo, _ = pedir("POST", "/sa/empresas", token=token_sa, cuerpo=malo)
comprobar(codigo == 400, "un correo del Admin invalido se rechaza", f"codigo {codigo}")

malo = json.loads(json.dumps(alta))
malo["ruc"] = "20777000004"
malo["admin"]["email"] = "qa.cortesia@lubritechqa.pe"
malo["diasDeCortesia"] = 900
codigo, _ = pedir("POST", "/sa/empresas", token=token_sa, cuerpo=malo)
comprobar(codigo == 400, "900 dias de cortesia se rechazan", f"codigo {codigo}")

malo = json.loads(json.dumps(alta))
malo["ruc"] = "20777000005"
malo["admin"]["email"] = "qa.plan@lubritechqa.pe"
malo["planCodigo"] = "NO_EXISTE"
codigo, r = pedir("POST", "/sa/empresas", token=token_sa, cuerpo=malo)
comprobar(codigo == 400 and clave_de(r) == "PLAN_INVALIDO",
          "un plan que no existe se rechaza", f"codigo {codigo} {clave_de(r)}")

RECHAZADAS = "('20777000002','20777000003','20777000004','20777000005')"
comprobar(un_valor(f"SELECT count(*) FROM empresa WHERE ruc IN {RECHAZADAS};") == "0",
          "ningun alta rechazada dejo empresa a medio crear",
          un_valor(f"SELECT string_agg(ruc, ',') FROM empresa WHERE ruc IN {RECHAZADAS};"))

# El RUC no se edita: identifica a la empresa ante SUNAT.
codigo, detalle = pedir("GET", f"/sa/empresas/{empresa_qa}", token=token_sa)
ruc_antes = detalle.get("ruc") if codigo == 200 else None
codigo, r = pedir("PUT", f"/sa/empresas/{empresa_qa}", token=token_sa, cuerpo={
    "ruc": "20999999999",
    "razonSocial": "Lubricentro QA S.A.C.",
    "nombreComercial": "LubriTech QA",
    "direccion": "Av. de las Pruebas 100, Lima",
    "telefono": "014000000",
    "email": "contacto@lubritechqa.pe",
})
comprobar(codigo == 200, "la edicion de datos de la empresa responde", f"codigo {codigo} {r}")
comprobar(un_valor(f"SELECT ruc FROM empresa WHERE id = {empresa_qa};") == ruc_antes,
          "el RUC no cambia aunque lo manden en el cuerpo")


# ===========================================================================
print("\n=== 6. Clave del Admin y primer ingreso ===")

codigo, r = pedir("POST", f"/sa/empresas/{empresa_qa}/clave-admin", token=token_sa)
comprobar(codigo == 200 and len(r.get("claveTemporal", "")) >= 10,
          "el SuperAdmin puede generar una clave temporal", f"codigo {codigo}")
temporal = r.get("claveTemporal") if codigo == 200 else None

token_qa = None
if temporal:
    codigo, sesion = login(EMAIL_QA, temporal)
    comprobar(codigo == 200, "el Admin entra con la clave temporal", f"codigo {codigo} {sesion}")
    comprobar(codigo == 200 and sesion["perfil"]["debeCambiarClave"] is True,
              "el primer ingreso exige cambiar la clave")
    comprobar(codigo == 200 and sesion["perfil"]["rol"] == "Admin",
              "la cuenta creada por el alta es el Admin de la empresa")
    comprobar(codigo == 200 and sesion["perfil"]["empresaId"] == empresa_qa,
              "el Admin nace amarrado a su empresa")

    if codigo == 200:
        codigo, _ = pedir("POST", "/auth/cambiar-clave", token=sesion["token"],
                          cuerpo={"claveActual": temporal, "claveNueva": CLAVE})
        comprobar(codigo == 200, "el Admin cambia su clave", f"codigo {codigo}")

        codigo, _ = pedir("GET", "/sedes", token=sesion["token"])
        comprobar(codigo == 401, "cambiar la clave cierra la sesion anterior", f"codigo {codigo}")

    codigo, sesion = login(EMAIL_QA, CLAVE)
    comprobar(codigo == 200, "el Admin entra con su clave nueva", f"codigo {codigo}")
    comprobar(codigo == 200 and sesion["perfil"]["debeCambiarClave"] is False,
              "ya no se le vuelve a exigir el cambio")
    token_qa = sesion.get("token") if codigo == 200 else None
    comprobar(codigo == 200 and "MULTISEDE" in sesion["perfil"]["funciones"],
              "el perfil trae las funciones del plan Profesional",
              str(sesion["perfil"].get("funciones")) if codigo == 200 else "")

comprobar(token_qa is not None, "hay sesion del Admin de pruebas para seguir")


# ===========================================================================
print("\n=== 7. Aislamiento: cada Admin ve solo su empresa (criterio 11) ===")

if token_qa:
    codigo, sedes = pedir("GET", "/sedes", token=token_qa)
    comprobar(codigo == 200, "el Admin ve sus sedes", f"codigo {codigo}")
    comprobar(codigo == 200 and sedes and all(s["empresaId"] == empresa_qa for s in sedes),
              "todas las sedes que ve son de su empresa",
              str({s["empresaId"] for s in sedes}) if codigo == 200 else "")

    codigo, _ = pedir("GET", "/sa/empresas", token=token_qa)
    comprobar(codigo == 403, "el Admin de pruebas no entra a la consola", f"codigo {codigo}")

if token_demo:
    codigo, sedes_demo = pedir("GET", "/sedes", token=token_demo)
    comprobar(codigo == 200 and all(s["empresaId"] == empresa_demo for s in sedes_demo),
              "el Admin de la demo solo ve las sedes de la demo")
    comprobar(codigo == 200 and empresa_qa not in {s["empresaId"] for s in sedes_demo},
              "la empresa de pruebas no se le asoma a la demo")


# ===========================================================================
print("\n=== 8. Sedes: tope del plan y sede principal ===")

if token_qa:
    codigo, sedes = pedir("GET", "/sedes", token=token_qa)
    nombres = {s["nombre"] for s in sedes} if codigo == 200 else set()

    if SEDE_2 not in nombres:
        codigo, r = pedir("POST", "/sedes", token=token_qa,
                          cuerpo={"nombre": SEDE_2, "direccion": "Av. Norte 200, Lima"})
        comprobar(codigo == 200, "el Admin abre una segunda sede (plan con multisede)",
                  f"codigo {codigo} {r}")
    else:
        comprobar(True, "la segunda sede de pruebas ya existia")

    codigo, r = pedir("POST", "/sedes", token=token_qa, cuerpo={"nombre": SEDE_2})
    comprobar(codigo == 409 and clave_de(r) == "NOMBRE_DUPLICADO",
              "dos sedes con el mismo nombre no entran", f"codigo {codigo} {clave_de(r)}")

    codigo, r = pedir("POST", "/sedes", token=token_qa, cuerpo={"nombre": "Sede QA Sur"})
    comprobar(codigo == 409 and clave_de(r) == "TOPE_DE_SEDES",
              "la tercera sede choca con el tope del plan", f"codigo {codigo} {clave_de(r)}")

    codigo, sedes = pedir("GET", "/sedes", token=token_qa)
    por_nombre = {s["nombre"]: s for s in sedes} if codigo == 200 else {}
    principal = next((s for s in sedes if s["esPrincipal"]), None) if codigo == 200 else None

    comprobar(principal is not None and principal["nombre"] == SEDE_1,
              "la sede principal es la que creo el alta",
              principal["nombre"] if principal else "ninguna")

    # Desactivar la principal dejaria a la empresa sin sede de referencia.
    if principal:
        codigo, r = pedir("PUT", f"/sedes/{principal['id']}", token=token_qa, cuerpo={
            "nombre": principal["nombre"], "direccion": principal.get("direccion"),
            "telefono": principal.get("telefono"), "activo": False})
        comprobar(codigo == 409 and clave_de(r) == "SEDE_PRINCIPAL",
                  "la sede principal no se puede desactivar", f"codigo {codigo} {clave_de(r)}")

    # Mover la principal y devolverla: una sola principal en todo momento.
    if SEDE_2 in por_nombre and principal:
        codigo, r = pedir("PUT", f"/sedes/{por_nombre[SEDE_2]['id']}/principal", token=token_qa)
        comprobar(codigo == 200 and r.get("esPrincipal") is True,
                  "la segunda sede pasa a ser la principal", f"codigo {codigo}")
        comprobar(un_valor(f"SELECT count(*) FROM sede WHERE empresa_id = {empresa_qa} "
                           "AND es_principal;") == "1",
                  "sigue habiendo una sola sede principal")

        codigo, r = pedir("PUT", f"/sedes/{principal['id']}/principal", token=token_qa)
        comprobar(codigo == 200 and r.get("esPrincipal") is True,
                  "la principal vuelve a su sitio", f"codigo {codigo}")

    # Una sede de OTRA empresa no existe para este Admin.
    sede_ajena = un_valor(f"SELECT id FROM sede WHERE empresa_id <> {empresa_qa} LIMIT 1;")
    if sede_ajena:
        codigo, r = pedir("PUT", f"/sedes/{sede_ajena}/principal", token=token_qa)
        comprobar(codigo == 404, "una sede de otra empresa responde 404, no 200",
                  f"codigo {codigo} {clave_de(r)}")

if token_tecnico:
    codigo, _ = pedir("POST", "/sedes", token=token_tecnico, cuerpo={"nombre": "Sede del tecnico"})
    comprobar(codigo == 403, "un tecnico no abre sedes", f"codigo {codigo}")


# ===========================================================================
print("\n=== 9. Cambio de plan: el permiso efectivo es rol INTERSECCION plan ===")

if token_qa:
    # Con dos sedes activas, bajar al Basico (una sola sede) debe rebotar.
    codigo, r = pedir("PUT", f"/sa/empresas/{empresa_qa}/plan", token=token_sa,
                      cuerpo={"planCodigo": "BASICO", "motivo": "prueba de QA"})
    comprobar(codigo == 409 and clave_de(r) == "PLAN_NO_ALCANZA",
              "bajar de plan con la empresa excedida se rechaza con 409",
              f"codigo {codigo} {clave_de(r)}")
    comprobar(un_valor(f"SELECT p.codigo FROM empresa e JOIN plan p ON p.id = e.plan_id "
                       f"WHERE e.id = {empresa_qa};") == "PROFESIONAL",
              "el plan rechazado no se aplico a medias")

    codigo, sedes = pedir("GET", "/sedes", token=token_qa)
    segunda = next((s for s in sedes if s["nombre"] == SEDE_2), None) if codigo == 200 else None

    if segunda:
        # Se desactiva la segunda sede para que el Basico si alcance.
        codigo, _ = pedir("PUT", f"/sedes/{segunda['id']}", token=token_qa, cuerpo={
            "nombre": segunda["nombre"], "direccion": segunda.get("direccion"),
            "telefono": segunda.get("telefono"), "activo": False})
        comprobar(codigo == 200, "se puede desactivar una sede que no es la principal",
                  f"codigo {codigo}")

        codigo, r = pedir("PUT", f"/sa/empresas/{empresa_qa}/plan", token=token_sa,
                          cuerpo={"planCodigo": "BASICO", "motivo": "prueba de QA"})
        comprobar(codigo == 200, "con la empresa dentro de norma, el plan si cambia",
                  f"codigo {codigo} {r}")

        # El Basico no vende multisede: el MISMO Admin, con el MISMO rol,
        # pierde el permiso. Nadie le toco el rol.
        codigo, r = pedir("POST", "/sedes", token=token_qa, cuerpo={"nombre": "Sede QA Sur"})
        comprobar(codigo == 403, "en Basico el Admin ya no puede abrir sedes",
                  f"codigo {codigo} {clave_de(r)}")

        codigo, _ = pedir("GET", "/sedes", token=token_qa)
        comprobar(codigo == 200, "pero sigue viendo y configurando las que tiene",
                  f"codigo {codigo}")

        codigo, sesion = login(EMAIL_QA, CLAVE)
        comprobar(codigo == 200 and "MULTISEDE" not in sesion["perfil"]["funciones"],
                  "el perfil ya no anuncia multisede",
                  str(sesion["perfil"].get("funciones")) if codigo == 200 else "")
        comprobar(codigo == 200 and "SEDES.GESTIONAR" not in sesion["perfil"]["permisos"],
                  "SEDES.GESTIONAR desaparece del permiso efectivo")
        if codigo == 200:
            token_qa = sesion["token"]

        # Y se devuelve todo a como estaba.
        codigo, _ = pedir("PUT", f"/sa/empresas/{empresa_qa}/plan", token=token_sa,
                          cuerpo={"planCodigo": "PROFESIONAL", "motivo": "fin de la prueba"})
        comprobar(codigo == 200, "volver a Profesional funciona", f"codigo {codigo}")

        codigo, _ = pedir("PUT", f"/sedes/{segunda['id']}", token=token_qa, cuerpo={
            "nombre": segunda["nombre"], "direccion": segunda.get("direccion"),
            "telefono": segunda.get("telefono"), "activo": True})
        comprobar(codigo == 200, "la segunda sede vuelve a activarse", f"codigo {codigo}")


# ===========================================================================
print("\n=== 10. Suscripciones: encadenar, anular y recalcular ===")

codigo, detalle = pedir("GET", f"/sa/empresas/{empresa_qa}", token=token_sa)
hasta_antes = detalle.get("servicioHasta") if codigo == 200 else None
comprobar(hasta_antes is not None, "se sabe hasta cuando llega el servicio hoy")

codigo, r = pedir("POST", f"/sa/empresas/{empresa_qa}/suscripciones", token=token_sa,
                  cuerpo={"meses": 0})
comprobar(codigo == 400, "cero meses se rechaza", f"codigo {codigo}")

codigo, r = pedir("POST", f"/sa/empresas/{empresa_qa}/suscripciones", token=token_sa,
                  cuerpo={"meses": 99})
comprobar(codigo == 400, "99 meses se rechaza", f"codigo {codigo}")

codigo, pago = pedir("POST", f"/sa/empresas/{empresa_qa}/suscripciones", token=token_sa,
                     cuerpo={"meses": 1, "referencia": "QA-BARRIDO"})
comprobar(codigo == 200, "se registra un pago de un mes", f"codigo {codigo} {pago}")

pago_id = pago.get("id") if codigo == 200 else None

if pago_id:
    comprobar(pago["desde"] > hasta_antes,
              "el periodo nuevo arranca DESPUES del ya pagado: no se pierde nada",
              f"desde {pago['desde']} vs hasta anterior {hasta_antes}")
    comprobar(pago["estado"] == "PAGADA", "el pago queda registrado como pagado")
    comprobar(float(pago["monto"]) == 189.00,
              "el monto sale del precio de lista del plan", str(pago.get("monto")))

    codigo, detalle = pedir("GET", f"/sa/empresas/{empresa_qa}", token=token_sa)
    comprobar(codigo == 200 and detalle["servicioHasta"] == pago["hasta"],
              "el servicio de la empresa se extiende hasta el nuevo periodo",
              f"{detalle.get('servicioHasta')} vs {pago['hasta']}")

    codigo, lista = pedir("GET", f"/sa/empresas/{empresa_qa}/suscripciones", token=token_sa)
    comprobar(codigo == 200 and any(s["id"] == pago_id for s in lista.get("items", [])),
              "el pago aparece en el historial de la empresa", f"codigo {codigo}")

    codigo, r = pedir("POST", f"/sa/suscripciones/{pago_id}/anular", token=token_sa, cuerpo={})
    comprobar(codigo == 400, "anular sin motivo se rechaza", f"codigo {codigo} {clave_de(r)}")

    codigo, r = pedir("POST", f"/sa/suscripciones/{pago_id}/anular", token=token_sa,
                      cuerpo={"motivo": "pago de prueba del barrido de QA"})
    comprobar(codigo == 200, "el pago se anula con motivo", f"codigo {codigo} {r}")

    codigo, detalle = pedir("GET", f"/sa/empresas/{empresa_qa}", token=token_sa)
    comprobar(codigo == 200 and detalle["servicioHasta"] == hasta_antes,
              "al anular, el servicio vuelve a donde estaba",
              f"{detalle.get('servicioHasta')} vs {hasta_antes}")

    codigo, r = pedir("POST", f"/sa/suscripciones/{pago_id}/anular", token=token_sa,
                      cuerpo={"motivo": "otra vez"})
    comprobar(codigo == 409 and clave_de(r) == "YA_ANULADA",
              "un pago anulado no se anula dos veces", f"codigo {codigo} {clave_de(r)}")

codigo, lista = pedir("GET", "/sa/suscripciones?tamano=5000", token=token_sa)
comprobar(codigo == 200 and lista.get("tamano") == 100,
          "el historial global de pagos tambien tiene tope de pagina",
          str(lista.get("tamano")))


# ===========================================================================
print("\n=== 11. Baja y reactivacion: el corte es instantaneo ===")

codigo, r = pedir("PUT", f"/sa/empresas/{empresa_qa}/estado", token=token_sa,
                  cuerpo={"activo": False})
comprobar(codigo == 400 and clave_de(r) == "MOTIVO_REQUERIDO",
          "dar de baja sin motivo se rechaza", f"codigo {codigo} {clave_de(r)}")

codigo, r = pedir("PUT", f"/sa/empresas/{empresa_qa}/estado", token=token_sa,
                  cuerpo={"activo": False, "motivo": "corte de prueba del barrido"})
comprobar(codigo == 200, "la empresa se da de baja con motivo", f"codigo {codigo} {r}")

if token_qa:
    codigo, _ = pedir("GET", "/sedes", token=token_qa)
    comprobar(codigo == 403, "el token que ya tenia el Admin deja de servir al instante",
              f"codigo {codigo}")

codigo, r = pedir("POST", "/auth/login", cuerpo={"email": EMAIL_QA, "password": CLAVE})
comprobar(codigo == 403, "el Admin de una empresa de baja no puede entrar", f"codigo {codigo}")

codigo, pagina = pedir("GET", "/sa/empresas?estado=INACTIVA", token=token_sa)
comprobar(codigo == 200 and any(i["id"] == empresa_qa for i in pagina.get("items", [])),
          "la empresa aparece como INACTIVA en la consola")

codigo, r = pedir("PUT", f"/sa/empresas/{empresa_qa}/estado", token=token_sa,
                  cuerpo={"activo": True})
comprobar(codigo == 200, "reactivar no exige motivo", f"codigo {codigo} {r}")

codigo, sesion = login(EMAIL_QA, CLAVE)
comprobar(codigo == 200, "el Admin vuelve a entrar en cuanto se reactiva", f"codigo {codigo}")
token_qa = sesion.get("token") if codigo == 200 else token_qa


# ===========================================================================
print("\n=== 12. Salud de la empresa y tablero del SaaS ===")

codigo, salud_emp = pedir("GET", f"/sa/empresas/{empresa_qa}/salud", token=token_sa)
comprobar(codigo == 200, "la pantalla de salud responde", f"codigo {codigo}")
if codigo == 200:
    comprobar(salud_emp["tieneAdmin"] is True, "salud detecta que la empresa tiene Admin")
    comprobar(isinstance(salud_emp.get("alertas"), list),
              "salud devuelve la lista de cosas que mirar")
    comprobar(salud_emp["maxSedes"] == 2 and salud_emp["sedesUsadas"] <= 2,
              "salud cuenta las sedes contra el tope del plan",
              f"{salud_emp.get('sedesUsadas')}/{salud_emp.get('maxSedes')}")
    comprobar(salud_emp["sedesExcedidas"] is False, "la empresa de pruebas no esta excedida")

codigo, tablero = pedir("GET", "/sa/tablero", token=token_sa)
comprobar(codigo == 200, "el tablero del SaaS responde", f"codigo {codigo}")
if codigo == 200:
    comprobar(tablero["empresas"] == total_bd, "el tablero cuenta todas las empresas",
              f"{tablero['empresas']} vs {total_bd}")
    comprobar(tablero["activas"] + tablero["inactivas"] == tablero["empresas"],
              "activas + inactivas cuadra con el total")
    comprobar(tablero["alDia"] + tablero["porVencer"] + tablero["vencidas"] == tablero["activas"],
              "los estados del servicio suman las activas",
              f"{tablero['alDia']}+{tablero['porVencer']}+{tablero['vencidas']} vs {tablero['activas']}")
    comprobar(sum(p["empresas"] for p in tablero["porPlan"]) == tablero["empresas"],
              "el reparto por plan suma el total de empresas")

    activas_bd = un_valor("SELECT coalesce(sum(p.precio_mensual),0)::numeric(12,2) "
                          "FROM empresa e JOIN plan p ON p.id = e.plan_id WHERE e.activo;")
    comprobar(abs(float(tablero["ingresoRecurrenteMensual"]) - float(activas_bd)) < 0.01,
              "el ingreso recurrente cuadra con los planes de las empresas activas",
              f"api {tablero['ingresoRecurrenteMensual']} vs base {activas_bd}")


# ===========================================================================
print("\n=== 13. Bitacora: todo cambio queda escrito (criterio 6) ===")

for accion in ("SA.EMPRESA.ALTA", "SA.EMPRESA.EDICION", "SA.EMPRESA.PLAN",
               "SA.EMPRESA.BAJA", "SA.EMPRESA.REACTIVACION", "SA.EMPRESA.CLAVE_ADMIN",
               "SEDE.ALTA", "SEDE.EDICION", "SEDE.PRINCIPAL",
               "SA.SUSCRIPCION.PAGO", "SA.SUSCRIPCION.ANULACION"):
    filas = un_valor(f"SELECT count(*) FROM bitacora WHERE accion = '{accion}';", "0")
    comprobar(filas != "0", f"la bitacora registro {accion}", f"{filas} filas")

con_autor = un_valor("SELECT count(*) FROM bitacora WHERE accion LIKE 'SA.%' "
                     "AND usuario_id IS NULL;", "-1")
comprobar(con_autor == "0", "toda accion de la consola tiene autor", f"{con_autor} sin autor")


# ===========================================================================
print("\n=== 14. Cuadre: la consola no deja datos huerfanos ===")

controles = [
    ("empresa sin sede principal",
     "SELECT count(*) FROM empresa e WHERE NOT EXISTS "
     "(SELECT 1 FROM sede s WHERE s.empresa_id = e.id AND s.es_principal)"),
    ("empresa con mas de una sede principal",
     "SELECT count(*) FROM (SELECT empresa_id FROM sede WHERE es_principal "
     "GROUP BY empresa_id HAVING count(*) > 1) x"),
    ("empresa sin Admin activo",
     "SELECT count(*) FROM empresa e WHERE NOT EXISTS "
     "(SELECT 1 FROM usuario u WHERE u.empresa_id = e.id AND u.rol_id = 1 AND u.activo)"),
    ("usuario con sede de otra empresa",
     "SELECT count(*) FROM usuario u JOIN sede s ON s.id = u.sede_id "
     "WHERE s.empresa_id IS DISTINCT FROM u.empresa_id"),
    ("periodos pagados solapados",
     "SELECT count(*) FROM suscripcion a JOIN suscripcion b "
     "ON b.empresa_id = a.empresa_id AND b.id > a.id "
     "WHERE a.estado = 'PAGADA' AND b.estado = 'PAGADA' "
     "AND daterange(a.desde, a.hasta, '[]') && daterange(b.desde, b.hasta, '[]')"),
    ("empresa con sedes activas sobre el tope del plan",
     "SELECT count(*) FROM empresa e JOIN plan p ON p.id = e.plan_id "
     "WHERE p.max_sedes > 0 AND (SELECT count(*) FROM sede s "
     "WHERE s.empresa_id = e.id AND s.activo) > p.max_sedes"),
    ("plan_funcion fuera del catalogo",
     "SELECT count(*) FROM plan_funcion pf LEFT JOIN funcion f ON f.codigo = pf.funcion "
     "WHERE f.codigo IS NULL"),
    ("suscripcion de una empresa inexistente",
     "SELECT count(*) FROM suscripcion s LEFT JOIN empresa e ON e.id = s.empresa_id "
     "WHERE e.id IS NULL"),
    ("empresa con servicio vigente sin ningun periodo pagado",
     "SELECT count(*) FROM empresa e WHERE e.servicio_hasta >= "
     "(now() AT TIME ZONE 'America/Lima')::date AND NOT EXISTS "
     "(SELECT 1 FROM suscripcion s WHERE s.empresa_id = e.id AND s.estado = 'PAGADA')"),
]

for titulo, sql in controles:
    valor = un_valor(sql + ";", "-1")
    comprobar(valor == "0", f"cuadre: {titulo}", f"{valor} fila(s)")

# La empresa de pruebas queda como la encontramos.
estado_final = un_valor(
    f"SELECT p.codigo || '/' || e.activo::text || '/' || "
    f"(SELECT count(*) FROM sede s WHERE s.empresa_id = e.id AND s.activo)::text "
    f"FROM empresa e JOIN plan p ON p.id = e.plan_id WHERE e.id = {empresa_qa};")
comprobar(estado_final == "PROFESIONAL/true/2",
          "la empresa de pruebas queda en su estado de siempre", estado_final)

pagos_vivos = un_valor(f"SELECT count(*) FROM suscripcion WHERE empresa_id = {empresa_qa} "
                       "AND estado = 'PAGADA' AND referencia = 'QA-BARRIDO';", "-1")
comprobar(pagos_vivos == "0", "el barrido no dejo pagos de prueba vigentes",
          f"{pagos_vivos} pago(s)")


# ===========================================================================
print("\n" + "=" * 70)
if fallas:
    print(f"RESULTADO: {len(fallas)} FALLA(S) de {pasadas + len(fallas)} comprobaciones")
    for f in fallas:
        print(f"  - {f}")
    sys.exit(1)

print(f"RESULTADO: {pasadas} comprobaciones, 0 fallas")
