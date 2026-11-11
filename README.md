# LubriTech — Backend

Sistema de gestión para lubricentros y talleres mecánicos. Este repositorio es el
**backend**: API REST en ASP.NET Core 10 (.NET 10) sobre PostgreSQL. El frontend
Angular vive en su propio repositorio.

## Estructura

```
LubriTech/     Solución .NET (4 capas: API · Aplicacion · Dominio · Infraestructura)
scripts/       Esquema y datos, en SQL numerado (NO se usan EF Migrations)
qa/            Candados SQL + arneses de prueba contra la API viva
docs/          Arquitectura, hoja de ruta y el detalle de cada fase cerrada
```

## Puesta en marcha (desarrollo)

1. **PostgreSQL 18** corriendo en local. Crear la base `LubriTech`.
2. **Configuración local** — copiar la plantilla y completar:
   ```
   cp LubriTech/LubriTech.API/appsettings.Development.example.json \
      LubriTech/LubriTech.API/appsettings.Development.json
   ```
   Este archivo está ignorado por git (lleva la cadena de conexión y la llave JWT).
3. **Crear el esquema y los datos** — en orden:
   ```
   psql -d LubriTech -f scripts/00_nucleo.sql
   psql -d LubriTech -f scripts/01_seed.sql
   psql -d LubriTech -f scripts/02_consola_saas.sql
   psql -d LubriTech -f scripts/03_cortesia_registrada.sql
   psql -d LubriTech -f scripts/04_maestros.sql
   ```
4. **Correr la API**:
   ```
   dotnet run --project LubriTech/LubriTech.API --launch-profile http
   ```
   Responde en `http://localhost:9301`. Salud: `GET /api/salud`.

## QA

```
psql -d LubriTech -f qa/00_candados.sql           # candados Fase 0
psql -d LubriTech -f qa/01_candados_consola.sql   # candados Fase 1
psql -d LubriTech -f qa/02_candados_maestros.sql  # candados Fase 2
python qa/qa_nucleo.py        # 62 comprobaciones contra la API (Fase 0)
python qa/qa_consola.py       # 135 comprobaciones (Fase 1)
python qa/qa_maestros.py      # 74 comprobaciones (Fase 2)
```

## Estado

Backend cerrado hasta la **Fase 2** (maestros del taller). Ver `docs/01-HOJA-DE-RUTA.md`
para el avance completo y lo que sigue.

## Reglas de la casa

- El esquema se escribe a mano en `scripts/`. **Nunca** `dotnet ef migrations`.
- Todo movimiento de stock pasa por el kardex (lo garantiza la base).
- Cero errores 500; ningún 400 que frene un flujo legítimo.
- Los secretos de entorno no se versionan.
