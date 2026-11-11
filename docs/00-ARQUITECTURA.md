# LubriTech — Arquitectura del ecosistema

> Documento fuente de verdad. Fecha de apertura: 2026-11-10.
> Si otro documento contradice a este sobre una decisión de arquitectura, manda este.

LubriTech es un **ecosistema multidispositivo para lubricentros y talleres automotrices**:
un ERP web, dos aplicaciones móviles nativas y un visor 3D del vehículo, todos contra
**una sola plataforma de negocio**.

Es el quinto producto de la casa. Se toman SophiTech, Pharmexa y ValexaFood como
**referencia, no como copia**: solución propia, base de datos propia, código reescrito.

---

## 1. Los tres productos

| Producto | Quién lo usa | Tecnología |
|---|---|---|
| **LubriTech ERP Web** | Propietario, gerente, administrador, contador, supervisor | Angular + Tailwind + Angular Material + ECharts |
| **LubriTech Operaciones** | Técnico, mecánico, operario, cajero, asesor de servicio, almacenero | Flutter nativo (Android + iOS) |
| **LubriTech Clientes** | Cliente particular, dueño de vehículo, representante de flota | Flutter nativo (Android + iOS) |

Las dos apps comparten paquetes internos (red, sesión, tema, modelos) en un
**workspace Flutter modular**, pero son **dos aplicaciones distintas**, con su propio
identificador, su propia navegación y su propia jerarquía de pantallas.

**Prohibido:** PWA en lugar de app nativa, y WebView que solo abra el Angular.

---

## 2. Stack

**Backend** — C# · ASP.NET Core 10 LTS · EF Core 10 (solo como ORM, ver 4.1) ·
PostgreSQL 18 · ASP.NET Core Identity para el hash y las políticas · JWT con refresh
rotativo · SignalR · REST · Serilog · FluentValidation.

**ERP web** — Angular (standalone components) · TypeScript · Angular Material ·
Tailwind CSS · Apache ECharts · Three.js para el visor 3D.

**Móvil** — Flutter · Dart · Material Design 3 con adaptación por plataforma ·
FCM (Android) · APNs (iOS) · secure storage · biometría opcional.

**3D** — GLB/GLTF sobre WebGL (Three.js en web, visor nativo en Flutter), con LOD,
carga progresiva y **alternativa 2D obligatoria** cuando el equipo no da.

---

## 3. Dónde vive cada cosa

```
K:\PROYECTO LUBRICENTRO\
  LubriTech\             backend .NET 10, 4 capas (API / Aplicación / Dominio / Infraestructura)
  LubriTech_Frontend\    ERP web Angular
  LubriTech_Movil\       workspace Flutter: paquetes compartidos + app_operaciones + app_clientes
  scripts\               SQL numerado (00_nucleo.sql, 01_...), aplicado con psql
  qa\                    arnés de QA/QC en Python
  docs\                  este documento y los que siguen
  archivos\              evidencias, fotos y modelos 3D en dev
```

**Entorno de dev**

- Base `LubriTech` en el Postgres 18 local — `Host=localhost;Port=5432;Username=postgres;Password=admin`
- API en **9301**, ERP web en **4500**. Para QA: API **9311**, web **4510**.
  (9001/4200 son de SophiTech, 9101/4300 de Pharmexa, 9201/4400 de ValexaFood.)
- Se levantan por `launch.json`, nunca con `dotnet run` ni `ng serve` a mano, y los
  puertos se devuelven libres al terminar.

---

## 4. Decisiones de arquitectura

### 4.1 EF Core sí, EF Migrations no

EF Core 10 es el ORM. El **esquema se escribe a mano**, en SQL numerado dentro de
`scripts\`, aplicado con `PGCLIENTENCODING=UTF8 psql -f`. Nunca
`dotnet ef migrations add`. Es regla de la casa en los cuatro productos anteriores y
aquí no cambia: el mismo script que corre en dev es el que se entrega para producción.

### 4.2 Dos mundos de identidad separados

Esta es la decisión que sostiene el criterio 11 ("ningún usuario puede consultar
información de otra empresa o vehículo sin autorización").

- `usuario` — **personal de la empresa**. Tiene `empresa_id`, `sede_id` y rol.
- `cliente_acceso` — **cuenta del portal del cliente**. Cuelga de `cliente`, no de
  `usuario`, y **no puede recibir ningún rol del ERP**.

Son dos tablas, dos flujos de login y **dos audiencias de JWT distintas**
(`lubritech-erp` y `lubritech-cliente`). Un token de cliente presentado a un endpoint
del ERP se rechaza por audiencia, antes de llegar a cualquier regla de permisos. Así
"no mostrar funciones administrativas a usuarios externos" deja de depender de que la
UI esconda un botón.

### 4.3 Roles del personal (IDs fijos)

```
0  SuperAdmin        (sin empresa, consola SaaS)
1  Admin             (propietario)
2  Gerente
3  Contador
4  Supervisor        (jefe de taller)
5  Asesor de servicio
6  Técnico           (mecánico / operario)
7  Cajero
8  Almacenero
```

Permiso efectivo = `rol_permiso` (matriz granular, editable) ∩ funciones que el plan
contratado habilita. Nada de `if (rol_id == 6)` desparramado en los controladores.

### 4.4 Multiempresa que falla cerrado

Filtro global de EF por reflexión sobre `EntidadEmpresa`. Sin sesión el filtro vale
**-1**, no 0: sin empresa resuelta **no se ve nada**. El SuperAdmin no tiene empresa
(`usuario.empresa_id NULL`, con CHECK) y usa `IgnoreQueryFilters()` explícito donde
corresponde.

### 4.5 El eje de tiempo

Todo instante se guarda en `timestamptz` **en UTC real**. "Hoy" se calcula con un
helper `DiaLima`. Nada de guardar hora de Lima disfrazada de UTC.

### 4.6 Convenciones de código

Entidades en PascalCase con `UseSnakeCaseNamingConvention()`; JSON en camelCase.
Angular standalone + Tailwind. Lo nuevo va en métodos propios: no se edita en línea
lógica que ya funciona.

---

## 5. Reglas de oro del dominio

Son invariantes. Donde se puede, con candado en la base, no solo en C#.

1. **El estado manda y queda escrito.** Todo cambio de estado de una orden escribe
   fila en `orden_historial` con orden, estado anterior, estado nuevo, usuario, fecha,
   observación y sede. `orden_historial` es **inmutable**: triggers
   `BEFORE UPDATE/DELETE` que lanzan excepción.
2. **El cliente ve lo autorizado, nunca lo interno.** `orden_estado.visible_cliente` y
   `orden_historial.visible_cliente` deciden qué sale al portal y al push. Costos de
   adquisición, márgenes, notas internas del técnico y comentarios entre áreas **no
   salen nunca** por el canal del cliente.
3. **Trabajo adicional sin aprobación registrada no se ejecuta.** La cotización se
   versiona (`cotizacion.version`) y la respuesta del cliente se guarda inmutable
   (`cotizacion_respuesta`: fecha, quién, versión, decisión). Sin fila de aprobación,
   el renglón adicional no pasa a ejecución.
4. **Un vehículo tiene un dueño y una empresa.** Ninguna consulta de cliente cruza a
   otro cliente ni a otra empresa. La vinculación de un vehículo al portal exige
   verificación y autorización del propietario (`vehiculo_vinculo` con código y
   caducidad).
5. **Nada se mueve sin dejar rastro.** Repuesto consumido en una orden mueve
   inventario y kardex en la misma transacción. No hay salida de almacén "suelta".
6. **Lo que llega del celular es idempotente.** Toda operación que viene de una app
   trae `clave_idempotencia`; se guarda con huella del cuerpo. Reintento con la misma
   clave y el mismo cuerpo devuelve el resultado anterior; con cuerpo distinto, 409.
7. **Plata solo con confirmación del servidor.** El modo sin conexión guarda y
   reintenta trabajo operativo. Cobros, cierres y movimientos de dinero **no se
   confirman offline**: quedan pendientes hasta que el servidor responde.
8. **La pantalla bloqueada no filtra.** El push lleva título neutro y el dato va
   dentro de la app. Ni montos, ni deuda, ni datos personales en la notificación
   visible.

---

## 6. Tiempo real

SignalR, un hub por público y grupos por alcance:

- `hub/erp` — grupos `empresa:{id}` y `sede:{id}`. Tablero y pantallas del ERP.
- `hub/operaciones` — grupos `sede:{id}` y `usuario:{id}`. Órdenes asignadas.
- `hub/clientes` — grupo `cliente:{id}`. Solo eventos con `visible_cliente`.

El hub de clientes **no publica el evento crudo**: publica una proyección ya filtrada.
Un evento interno no puede "escaparse" por olvidar un campo.

Estados de la orden (catálogo en base, no constantes en código):
cita registrada · vehículo recibido · inspección inicial · diagnóstico ·
cotización pendiente · esperando aprobación · trabajo aprobado · en mantenimiento ·
en control de calidad · listo para entrega · entregado.

---

## 7. Restricciones reales del entorno (2026-11-10)

Verificado en esta máquina:

- .NET SDK **10.0.401** — ok.
- Node **24.14.0**, Angular CLI presente — ok.
- PostgreSQL **18** en `C:\Program Files\PostgreSQL\18\bin` (`psql` no está en el PATH) — ok.
- **Flutter / Dart SDK: no instalado.** Hace falta Flutter SDK + Android Studio
  (SDK de Android, emulador) para compilar la app Android.
- **iOS no se puede compilar en Windows.** Xcode solo corre en macOS. Para el IPA y la
  subida a App Store hacen falta una Mac o un runner macOS en la nube (GitHub Actions
  macOS, Codemagic). El código Flutter sí se escribe aquí, y queda listo para firmar y
  compilar allá; la cuenta de Apple Developer (99 USD/año) es requisito aparte.

Esto no frena el arranque: el backend, el ERP web, los contratos REST, SignalR y la
lógica de negocio son lo que las apps consumen, y se construyen primero.

---

## 8. Criterios de aceptación

Los 11 criterios del encargo están desglosados, con su prueba, en
`01-HOJA-DE-RUTA.md`. Ninguno se da por cumplido sin correr el arnés de QA completo:
cero fallas, cero 500, ningún 400 que frene un flujo legítimo, y cuadre de integridad
en cero (sin huérfanos).
