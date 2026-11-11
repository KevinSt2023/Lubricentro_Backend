# -*- coding: utf-8 -*-
"""
LubriTech · qa/qa_nucleo.py — arnes de la Fase 0 (nucleo).

Prueba, contra la API viva, lo que la Fase 0 promete:

  * la API responde y llega a la base
  * las dos puertas de login funcionan y dan audiencias distintas
  * los permisos efectivos son rol INTERSECCION plan, no el rol a secas
  * un rol que no maneja dinero no tiene ningun permiso de dinero
  * un token del portal del cliente NO entra al ERP, y al reves
  * la consola del SuperAdmin es solo del rol 0
  * el filtro de empresa recorta de verdad: un Admin ve solo sus sedes
  * el refresh rota, y reusar uno viejo revoca toda la cadena
  * el bloqueo por intentos fallidos muerde

Lo que se exige: cero fallas, cero 500, y ningun 400 que frene un flujo
legitimo. Un 401/403 esperado SI es un resultado correcto.

Uso:
    python qa/qa_nucleo.py            # contra http://localhost:9301
    python qa/qa_nucleo.py 9311       # contra el perfil de QA
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

fallas = []
pasadas = 0


def comprobar(condicion, titulo, detalle=""):
    global pasadas
    if condicion:
        pasadas += 1
        print(f"  PASA  {titulo}")
    else:
        fallas.append(f"{titulo} — {detalle}")
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

    r = requests.request(metodo, f"{BASE}{ruta}", headers=cabeceras, data=datos, timeout=20)
    try:
        return r.status_code, r.json()
    except ValueError:
        return r.status_code, r.text


def login(email, clave=CLAVE, ruta="/auth/login", dispositivo=None):
    cuerpo = {"email": email, "password": clave}
    if dispositivo:
        cuerpo["dispositivo"] = dispositivo
    return pedir("POST", ruta, cuerpo=cuerpo)


def psql(sql):
    """Para limpiar lo que la API no expone todavia. Siempre con UTF8."""
    return subprocess.run(
        [PSQL, "-h", "localhost", "-U", "postgres", "-d", BD, "-t", "-A",
         "-P", "pager=off", "-c", sql],
        capture_output=True, text=True,
        env={"PGPASSWORD": "admin", "PGCLIENTENCODING": "UTF8",
             "SystemRoot": r"C:\Windows", "PATH": r"C:\Windows\System32"},
    )


# ===========================================================================
print("\n=== 1. La API esta arriba y llega a la base ===")

codigo, salud = pedir("GET", "/salud")
comprobar(codigo == 200, "GET /api/salud responde 200", f"codigo {codigo}")
comprobar(isinstance(salud, dict) and salud.get("baseDeDatos") == "arriba",
          "la API llega a la base de datos", str(salud)[:120])

# El eje de tiempo: UTC real guardado, dia de Lima calculado.
if isinstance(salud, dict):
    comprobar(salud.get("utc", "").endswith("+00:00"),
              "la hora del servidor va en UTC real", salud.get("utc", ""))


print("\n=== 2. Las dos puertas de login ===")

codigo, sa = login("superadmin@lubritech.pe")
comprobar(codigo == 200, "login del SuperAdmin", f"codigo {codigo} {sa}")
token_sa = sa.get("token") if codigo == 200 else None
comprobar(codigo == 200 and sa["audiencia"] == "lubritech-erp",
          "el SuperAdmin recibe audiencia del ERP")
comprobar(codigo == 200 and sa["perfil"]["empresaId"] is None,
          "el SuperAdmin no tiene empresa")

codigo, admin = login("admin@lubritechdemo.pe")
comprobar(codigo == 200, "login del Admin de la empresa demo", f"codigo {codigo} {admin}")
token_admin = admin.get("token") if codigo == 200 else None
empresa_admin = admin["perfil"]["empresaId"] if codigo == 200 else None

codigo, tecnico = login("tecnico@lubritechdemo.pe")
comprobar(codigo == 200, "login del Tecnico", f"codigo {codigo}")
token_tecnico = tecnico.get("token") if codigo == 200 else None

codigo, vecino = login("admin@lubritechvecino.pe")
comprobar(codigo == 200, "login del Admin de la otra empresa", f"codigo {codigo}")
token_vecino = vecino.get("token") if codigo == 200 else None

codigo, cli = login("cliente@demo.pe", ruta="/auth/cliente/login")
comprobar(codigo == 200, "login del cliente en el portal", f"codigo {codigo} {cli}")
token_cliente = cli.get("token") if codigo == 200 else None
comprobar(codigo == 200 and cli["audiencia"] == "lubritech-cliente",
          "el cliente recibe la audiencia del portal")
comprobar(codigo == 200 and cli["perfil"]["permisos"] == [] and cli["perfil"]["rolId"] is None,
          "el cliente no trae rol ni permisos del ERP")

codigo, _ = login("admin@lubritechdemo.pe", clave="ClaveEquivocada1")
comprobar(codigo == 401, "clave incorrecta devuelve 401", f"codigo {codigo}")

codigo, _ = login("no.existe@lubritech.pe")
comprobar(codigo == 401, "correo inexistente devuelve 401 (no 404)", f"codigo {codigo}")

# El personal no entra por la puerta del cliente ni al reves.
codigo, _ = login("admin@lubritechdemo.pe", ruta="/auth/cliente/login")
comprobar(codigo == 401, "el personal no entra por la puerta del portal", f"codigo {codigo}")

codigo, _ = login("cliente@demo.pe")
comprobar(codigo == 401, "el cliente no entra por la puerta del ERP", f"codigo {codigo}")


print("\n=== 3. Permisos efectivos: rol interseccion plan ===")

permisos_admin = admin["perfil"]["permisos"] if token_admin else []
funciones_admin = admin["perfil"]["funciones"] if token_admin else []
permisos_tecnico = tecnico["perfil"]["permisos"] if token_tecnico else []
funciones_vecino = vecino["perfil"]["funciones"] if token_vecino else []

comprobar("CLIENTES.VER" in permisos_admin, "el Admin tiene CLIENTES.VER")
comprobar("FINANZAS.VER" in permisos_admin, "el Admin ve finanzas")
comprobar("VISOR_3D" in funciones_admin,
          "el plan Premium habilita el visor 3D", str(funciones_admin))
comprobar("VISOR3D.USAR" in permisos_admin,
          "y por eso el permiso del visor existe para esta empresa")

# La otra empresa esta en plan Profesional: sin visor 3D ni flotas.
comprobar("VISOR_3D" not in funciones_vecino,
          "el plan Profesional NO habilita el visor 3D", str(funciones_vecino))
permisos_vecino = vecino["perfil"]["permisos"] if token_vecino else []
comprobar("VISOR3D.USAR" not in permisos_vecino,
          "y el permiso del visor desaparece aunque el rol Admin lo tenga")
comprobar("CLIENTES.VER" in permisos_vecino,
          "pero los permisos que no dependen del plan siguen ahi")

print("\n  -- el Tecnico no ve dinero --")
comprobar("ORDENES.VER_ASIGNADAS" in permisos_tecnico,
          "el Tecnico ve sus ordenes asignadas")
dinero = ["CAJA.COBRAR", "CAJA.ABRIR", "CAJA.CERRAR", "FINANZAS.VER",
          "ORDENES.COSTOS", "ALMACEN.COSTOS", "TABLERO.EJECUTIVO"]
sobra = [p for p in dinero if p in permisos_tecnico]
comprobar(not sobra, "el Tecnico no tiene NINGUN permiso de dinero", str(sobra))


print("\n=== 4. Las audiencias son una frontera, no una sugerencia ===")

codigo, cuerpo = pedir("GET", "/salud/solo-erp", token=token_cliente)
comprobar(codigo == 403, "un token del portal NO entra al ERP", f"codigo {codigo} {cuerpo}")

codigo, cuerpo = pedir("GET", "/salud/solo-cliente", token=token_admin)
comprobar(codigo == 403, "un token del ERP NO entra al portal", f"codigo {codigo} {cuerpo}")

codigo, _ = pedir("GET", "/salud/solo-cliente", token=token_cliente)
comprobar(codigo == 200, "el cliente si entra a lo del portal", f"codigo {codigo}")

codigo, _ = pedir("GET", "/salud/solo-erp", token=token_admin)
comprobar(codigo == 200, "el Admin si entra a lo del ERP", f"codigo {codigo}")

codigo, _ = pedir("GET", "/salud/solo-erp", token=token_tecnico)
comprobar(codigo == 403, "el Tecnico sin CLIENTES.VER recibe 403", f"codigo {codigo}")

codigo, _ = pedir("GET", "/salud/solo-superadmin", token=token_admin)
comprobar(codigo == 403, "el Admin no entra a la consola del SaaS", f"codigo {codigo}")

codigo, _ = pedir("GET", "/salud/solo-superadmin", token=token_sa)
comprobar(codigo == 200, "el SuperAdmin si entra a su consola", f"codigo {codigo}")

codigo, _ = pedir("GET", "/salud/aislamiento")
comprobar(codigo == 401, "sin token, 401", f"codigo {codigo}")

codigo, _ = pedir("GET", "/salud/aislamiento", token="esto.no.es.un.token")
comprobar(codigo == 401, "con un token inventado, 401", f"codigo {codigo}")


print("\n=== 5. Aislamiento entre empresas (criterio 11) ===")

codigo, ais_admin = pedir("GET", "/salud/aislamiento", token=token_admin)
comprobar(codigo == 200, "el Admin consulta el aislamiento", f"codigo {codigo}")

if codigo == 200:
    comprobar(ais_admin["empresasDeLasSedesVisibles"] == [empresa_admin],
              "el Admin ve sedes de UNA sola empresa: la suya",
              str(ais_admin["empresasDeLasSedesVisibles"]))
    comprobar(ais_admin["sedesVisibles"] < ais_admin["sedesTotales"],
              "el filtro recorta de verdad (ve menos de lo que hay)",
              f"{ais_admin['sedesVisibles']} de {ais_admin['sedesTotales']}")
    comprobar(ais_admin["clientesVisibles"] < ais_admin["clientesTotales"],
              "y tambien recorta los clientes",
              f"{ais_admin['clientesVisibles']} de {ais_admin['clientesTotales']}")

codigo, ais_vecino = pedir("GET", "/salud/aislamiento", token=token_vecino)
if codigo == 200:
    comprobar(ais_vecino["empresasDeLasSedesVisibles"] != ais_admin.get("empresasDeLasSedesVisibles"),
              "cada Admin ve su propia empresa, no la del otro",
              f"{ais_vecino['empresasDeLasSedesVisibles']} vs {ais_admin.get('empresasDeLasSedesVisibles')}")

# El SuperAdmin tampoco ve nada por el filtro: tiene que pedirlo explicito.
codigo, ais_sa = pedir("GET", "/salud/aislamiento", token=token_sa)
if codigo == 200:
    comprobar(ais_sa["sedesVisibles"] == 0,
              "el filtro falla CERRADO incluso para el SuperAdmin",
              f"ve {ais_sa['sedesVisibles']} sedes sin IgnoreQueryFilters")


print("\n=== 6. Perfil y cambio de clave ===")

codigo, perfil = pedir("GET", "/auth/perfil", token=token_admin)
comprobar(codigo == 200 and perfil["email"] == "admin@lubritechdemo.pe",
          "GET /auth/perfil devuelve el perfil correcto", f"codigo {codigo}")

codigo, cuerpo = pedir("POST", "/auth/cambiar-clave", token=token_admin,
                       cuerpo={"claveActual": CLAVE, "claveNueva": "corta"})
comprobar(codigo == 400, "una clave nueva debil se rechaza con 400", f"codigo {codigo}")

codigo, cuerpo = pedir("POST", "/auth/cambiar-clave", token=token_admin,
                       cuerpo={"claveActual": "LaQueNoEs1", "claveNueva": "Nueva2026x"})
comprobar(codigo == 400, "clave actual equivocada se rechaza con 400", f"codigo {codigo}")

codigo, cuerpo = pedir("POST", "/auth/login", cuerpo={"email": "no-es-un-correo", "password": "x"})
comprobar(codigo == 400, "un correo con formato invalido se rechaza con 400", f"codigo {codigo}")


print("\n=== 7. El refresh rota y detecta reuso ===")

dispositivo = {"identificador": "qa-telefono-1", "aplicacion": "OPERACIONES",
               "plataforma": "ANDROID", "tokenPush": "qa-token-push",
               "modelo": "QA Phone", "versionApp": "0.1.0", "versionOs": "15"}

codigo, sesion1 = login("cajero@lubritechdemo.pe", dispositivo=dispositivo)
comprobar(codigo == 200, "login desde una app registra el dispositivo", f"codigo {codigo}")
refresh1 = sesion1.get("refreshToken") if codigo == 200 else None

codigo, sesion2 = pedir("POST", "/auth/refrescar", cuerpo={"refreshToken": refresh1})
comprobar(codigo == 200, "el refresh devuelve un token nuevo", f"codigo {codigo} {sesion2}")
refresh2 = sesion2.get("refreshToken") if codigo == 200 else None
comprobar(refresh2 is not None and refresh2 != refresh1,
          "el refresh ROTA: el token nuevo es distinto del viejo")

# Reusar el primero: es robo. Se cae toda la cadena.
codigo, cuerpo = pedir("POST", "/auth/refrescar", cuerpo={"refreshToken": refresh1})
comprobar(codigo == 401 and isinstance(cuerpo, dict) and cuerpo.get("codigo") == "REFRESH_REUSADO",
          "reusar un refresh ya rotado devuelve 401 REFRESH_REUSADO", f"codigo {codigo} {cuerpo}")

# Y el segundo tambien murio, porque se revoco la cadena completa.
codigo, cuerpo = pedir("POST", "/auth/refrescar", cuerpo={"refreshToken": refresh2})
comprobar(codigo == 401,
          "tras el reuso, el refresh vigente tambien queda revocado", f"codigo {codigo}")

codigo, _ = pedir("POST", "/auth/refrescar", cuerpo={"refreshToken": "inventado"})
comprobar(codigo == 401, "un refresh inventado devuelve 401", f"codigo {codigo}")


print("\n=== 8. Cerrar sesion en todos los dispositivos ===")

codigo, sesion3 = login("almacen@lubritechdemo.pe")
token_alm = sesion3.get("token") if codigo == 200 else None
refresh_alm = sesion3.get("refreshToken") if codigo == 200 else None

codigo, _ = pedir("POST", "/auth/cerrar-todo", token=token_alm)
comprobar(codigo == 200, "cerrar-todo responde 200", f"codigo {codigo}")

# El token viejo ya no vale: su sesion_version quedo atras.
codigo, cuerpo = pedir("GET", "/auth/perfil", token=token_alm)
comprobar(codigo == 401 and isinstance(cuerpo, dict) and cuerpo.get("codigo") == "SESION_CERRADA",
          "el token anterior deja de valer al instante", f"codigo {codigo} {cuerpo}")

codigo, _ = pedir("POST", "/auth/refrescar", cuerpo={"refreshToken": refresh_alm})
comprobar(codigo == 401, "y su refresh tambien quedo revocado", f"codigo {codigo}")


print("\n=== 9. El bloqueo por intentos fallidos muerde ===")

victima = "supervisor@lubritechdemo.pe"
codigos = [login(victima, clave="MalaClave9")[0] for _ in range(5)]
comprobar(all(c == 401 for c in codigos),
          "los 5 intentos fallidos devuelven 401", str(codigos))

codigo, cuerpo = login(victima)   # ahora con la clave BUENA
comprobar(codigo == 423 and isinstance(cuerpo, dict) and cuerpo.get("codigo") == "BLOQUEADO",
          "al 6.o intento la cuenta esta bloqueada (423), aun con la clave correcta",
          f"codigo {codigo} {cuerpo}")

# Se deja la cuenta como estaba: el arnes no deja rastro.
r = psql(f"UPDATE usuario SET intentos_fallidos = 0, bloqueado_hasta = NULL "
         f"WHERE lower(email) = '{victima}';")
comprobar(r.returncode == 0, "se desbloquea la cuenta de prueba", r.stderr[:160])

codigo, _ = login(victima)
comprobar(codigo == 200, "y vuelve a entrar", f"codigo {codigo}")


print("\n=== 10. Cuadre: ni un 500, ni basura en la base ===")

r = psql("SELECT count(*) FROM usuario u JOIN sede s ON s.id = u.sede_id "
         "WHERE s.empresa_id IS DISTINCT FROM u.empresa_id;")
comprobar(r.stdout.strip() == "0", "ningun usuario con sede de otra empresa", r.stdout.strip())

r = psql("SELECT count(*) FROM usuario WHERE empresa_id IS NULL AND rol_id <> 0;")
comprobar(r.stdout.strip() == "0", "ningun usuario sin empresa que no sea SuperAdmin", r.stdout.strip())

r = psql("SELECT count(*) FROM cliente_acceso a LEFT JOIN cliente c ON c.id = a.cliente_id "
         "WHERE c.id IS NULL;")
comprobar(r.stdout.strip() == "0", "ninguna cuenta de portal huerfana", r.stdout.strip())

r = psql("SELECT count(*) FROM dispositivo WHERE usuario_id IS NULL AND acceso_id IS NULL;")
comprobar(r.stdout.strip() == "0", "ningun dispositivo sin dueno", r.stdout.strip())

r = psql("SELECT count(*) FROM bitacora WHERE accion LIKE 'AUTH.%';")
comprobar(r.returncode == 0 and int(r.stdout.strip() or 0) > 0,
          "la bitacora registro los accesos", r.stdout.strip())


# ===========================================================================
print("\n" + "=" * 62)
if fallas:
    print(f"RESULTADO: {len(fallas)} FALLA(S) de {pasadas + len(fallas)} comprobaciones")
    for f in fallas:
        print(f"  - {f}")
    sys.exit(1)

print(f"RESULTADO: {pasadas} comprobaciones, 0 FALLAS")
print("=" * 62)
