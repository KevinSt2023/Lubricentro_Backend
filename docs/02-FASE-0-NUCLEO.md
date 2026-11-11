# Fase 0 · Núcleo — CERRADA el 2026-11-10

Lo que está construido, probado y corriendo. Esta fase no entrega pantallas:
entrega el piso sobre el que se paran el ERP web y las dos apps.

---

## Cómo se levanta

**1. Base de datos** (solo la primera vez):

```bash
PGCLIENTENCODING=UTF8 "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d postgres -c "CREATE DATABASE \"LubriTech\" WITH ENCODING 'UTF8' TEMPLATE template0;"
```

**2. Esquema y catálogos** (en orden, y siempre con `PGCLIENTENCODING=UTF8`):

```bash
PGCLIENTENCODING=UTF8 "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d LubriTech -v ON_ERROR_STOP=1 -P pager=off -f scripts/00_nucleo.sql
```

```bash
PGCLIENTENCODING=UTF8 "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d LubriTech -v ON_ERROR_STOP=1 -P pager=off -f scripts/01_seed.sql
```

**3. La API** — por `launch.json`, entrada `lubritech-backend` (puerto 9301).
Nunca con `dotnet run` a mano. Para QA en paralelo: `lubritech-backend-qa` (9311).

Al arrancar en Desarrollo siembra las cuentas con el hasher de la aplicación.
Es idempotente y **nunca pisa una clave existente**.

---

## Cuentas de dev

Todas con clave `Admin2026*`.

| Cuenta | Rol | Empresa |
|---|---|---|
| `superadmin@lubritech.pe` | SuperAdmin | — (sin empresa) |
| `admin@lubritechdemo.pe` | Admin | LubriTech Demo (plan Premium) |
| `supervisor@lubritechdemo.pe` | Supervisor | LubriTech Demo |
| `asesor@lubritechdemo.pe` | Asesor de servicio | LubriTech Demo |
| `tecnico@lubritechdemo.pe` | Técnico | LubriTech Demo |
| `cajero@lubritechdemo.pe` | Cajero | LubriTech Demo |
| `almacen@lubritechdemo.pe` | Almacenero | LubriTech Demo |
| `admin@lubritechvecino.pe` | Admin | LubriTech Vecino (plan Profesional) |
| `tecnico@lubritechvecino.pe` | Técnico | LubriTech Vecino |
| `cliente@demo.pe` | — portal | cliente de LubriTech Demo |
| `elena@vecino.pe` | — portal | cliente de LubriTech Vecino |

**La segunda empresa no es relleno.** Sin dos empresas con datos, el
aislamiento no se puede *probar*, solo suponer. El arnés la usa en cada
barrido, así que se conserva.

---

## Lo que quedó hecho

**Base** — `scripts/00_nucleo.sql`: planes (con funciones y cupo por rol),
empresas, suscripción, sedes, roles, permisos, matriz rol×permiso, personal,
refresh del personal, clientes, acceso al portal, refresh y recuperación del
cliente, dispositivos, bitácora e idempotencia.

**Candados en la base**, no solo en C#:

- `bitacora` y `suscripcion` inmutables (triggers que lanzan excepción).
- Un rol que no maneja dinero **no puede recibir** un permiso de dinero.
- El SuperAdmin es el único sin empresa; nadie más puede estar sin ella.
- La sede de un usuario tiene que ser de su propia empresa.
- Cupo de usuarios por rol y de sedes según el plan, con *advisory lock* para
  que dos altas simultáneas no pasen el tope entre las dos.
- Un solo Admin por empresa; una sola sede principal por empresa.
- Un dispositivo pertenece a un usuario **o** a un cliente, nunca a ninguno.

**Backend** — .NET 10, cuatro capas. EF Core 10 solo como ORM. Serilog a
consola y archivo rotativo. FluentValidation como filtro global. Tres hubs de
SignalR registrados (`/hub/erp`, `/hub/operaciones`, `/hub/clientes`), con
grupos por empresa, sede, usuario y cliente.

**Las dos puertas de identidad:**

```
POST /api/auth/login              personal   -> audiencia lubritech-erp
POST /api/auth/cliente/login      cliente    -> audiencia lubritech-cliente
POST /api/auth/refrescar          /  cliente/refrescar
POST /api/auth/cerrar             cierra esta sesión
POST /api/auth/cerrar-todo        cierra todas, al instante
POST /api/auth/cambiar-clave
GET  /api/auth/perfil
GET  /api/salud                   latido (GET y HEAD, nunca lanza)
GET  /api/salud/aislamiento       prueba del filtro de empresa
```

**Seguridad que funciona:**

- JWT de 30 minutos + refresh **rotativo** con detección de reuso: reusar un
  token ya rotado se trata como robo y revoca la cadena completa.
- `sesion_version` dentro del token: subirla cierra todas las sesiones de esa
  persona al instante, sin esperar que expire nada.
- Revalidación de sesión en **cada** petición (cuenta activa, sesión vigente,
  empresa activa y servicio al día), con caché de 30 s e invalidación inmediata.
- Bloqueo a los 5 intentos fallidos, 15 minutos.
- Hash con el `PasswordHasher` de ASP.NET Core Identity, con rehash automático
  cuando el hash guardado quedó con parámetros viejos.
- Filtro de empresa por reflexión que **falla cerrado** (-1, no 0).
- Permiso efectivo = rol ∩ plan, comprobado en el servidor en cada llamada.

---

## Tres decisiones que vale la pena recordar

**1. El proveedor de empresa es singleton a propósito.** EF compila el modelo
una sola vez por tipo de contexto y el filtro global queda con una referencia
a ese objeto dentro. Si fuera por petición, el modelo cacheado se quedaría
pegado a la primera petición del proceso y todas las siguientes filtrarían
por la empresa equivocada. Siendo singleton, lee el `HttpContext` en curso en
cada llamada. Es el tipo de error que no aparece en una prueba manual y
aparece con el primer cliente.

**2. El email del personal es único global, no por empresa.** El login es por
email y nada más; si dos empresas pudieran repetirlo, habría que pedir la
empresa en la pantalla de entrada. La misma persona sí puede tener ese email
en el portal de clientes: son dos mundos distintos.

**3. Serilog va más afuera que el manejo de errores.** Al revés, una excepción
que el manejador convierte en 409 —o en un 499 porque el cliente cortó—
atraviesa primero a Serilog y queda escrita como un 500 que nunca ocurrió.
Revisar el log buscando 500 es como se encuentran los problemas de verdad, así
que el log tiene que decir la verdad. Este orden se corrigió después de ver
exactamente eso en la primera corrida.

---

## QA

```bash
PGCLIENTENCODING=UTF8 "/c/Program Files/PostgreSQL/18/bin/psql.exe" -h localhost -U postgres -d LubriTech -P pager=off -f qa/00_candados.sql
```

```bash
python qa/qa_nucleo.py
```

- `qa/00_candados.sql` — **13 candados**, todos muerden. Corre dentro de una
  transacción que termina en `ROLLBACK`: no deja ni una fila de prueba, ni
  siquiera en las tablas inmutables. Cierra con un cuadre de integridad que
  tiene que dar todo en cero.
- `qa/qa_nucleo.py` — **62 comprobaciones** contra la API viva: las dos
  puertas, permisos efectivos rol∩plan, el Técnico sin ningún permiso de
  dinero, las audiencias como frontera (token del portal rechazado en el ERP y
  al revés), la consola del SuperAdmin, el aislamiento entre las dos empresas,
  rotación y reuso del refresh, cierre de todas las sesiones, bloqueo por
  intentos y cuadre sin huérfanos. Desbloquea la cuenta que usó para la prueba
  de bloqueo: no deja rastro.

**Estado al cerrar la fase: 13 + 62 en verde, cero 500, cero `[ERR]` en el
log, cuadre en cero.**

Hallazgos reales de esta fase, corregidos:

1. El candado de cupo del plan se adelantaba al `CHECK` y rechazaba al
   SuperAdmin con el mensaje equivocado ("el plan no habilita el rol 0"). El
   rol 0 no pertenece a ninguna empresa, así que ese trigger ya no opina.
2. El orden de Serilog (punto 3 de arriba).
3. El latido podía lanzar excepción y tardar 3 s en frío. Ahora nunca lanza y
   responde a los 5 s con lo que sepa: es justo lo que se consulta cuando algo
   anda mal.

---

## Lo que sigue

Fase 1 (consola del SuperAdmin) y Fase 2 (clientes, vehículos, servicios,
almacén), según `01-HOJA-DE-RUTA.md`. Las órdenes de trabajo —el corazón del
taller— son la Fase 3, y recién ahí las apps móviles tienen algo que mostrar.
