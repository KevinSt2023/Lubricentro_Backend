using System.Text.Json;
using Npgsql;

namespace LubriTech.API.Seguridad;

/// <summary>
/// La red de seguridad contra los 500. Regla de la casa: cero 500.
///
/// Lo que llega aqui no deberia haber llegado, asi que se loguea completo.
/// Pero hay un caso que si es esperable y NO es un 500: los candados de la
/// base levantan excepciones P0001 con un mensaje escrito para una persona
/// ("el plan permite 2 tecnicos y ya hay 2"). Eso es un 409: un alto que un
/// Admin puede levantar, no un error del sistema. Traducirlo aqui evita que
/// cada servicio tenga que acordarse de atraparlo.
/// </summary>
public class ManejoDeErroresMiddleware(RequestDelegate siguiente, ILogger<ManejoDeErroresMiddleware> log)
{
    /// <summary>raise_exception de PL/pgSQL: nuestros candados.</summary>
    private const string CandadoDeLaBase = "P0001";

    public async Task InvokeAsync(HttpContext contexto)
    {
        try
        {
            await siguiente(contexto);
        }
        catch (OperationCanceledException)
        {
            // Casi siempre: el cliente corto la conexion (navego, recargo o el
            // refresh se adelanto). No es un error y no hay a quien responder.
            // Se atrapa SIN filtro `when` a proposito: con el filtro, el
            // depurador de Visual Studio no puede predecir que queda manejada y
            // se detiene en ella como si fuera un fallo. Sin filtro, VS ve que
            // esta controlada y no interrumpe la ejecucion.
            log.LogDebug("Peticion cancelada: {Ruta}", contexto.Request.Path);
        }
        catch (Exception ex)
        {
            // Cuando el candado salta a traves de EF (SaveChanges), la
            // PostgresException viene ENVUELTA en una DbUpdateException. Por eso
            // no basta con atrapar PostgresException: hay que buscarla en la
            // cadena de InnerException. Si esta, es un 409 esperable; si no,
            // es un 500 de verdad.
            var pg = BuscarPostgres(ex);

            if (pg is not null)
            {
                await ResponderPorCandado(contexto, pg);
                return;
            }

            log.LogError(ex, "Error no manejado en {Metodo} {Ruta}",
                contexto.Request.Method, contexto.Request.Path);

            // Hacia afuera, nada de detalles internos.
            await Responder(contexto, StatusCodes.Status500InternalServerError,
                "Ocurrio un error inesperado. Ya quedo registrado.", "ERROR_INTERNO");
        }
    }

    /// <summary>Recorre la cadena de excepciones buscando la de Postgres.</summary>
    private static PostgresException? BuscarPostgres(Exception? ex)
    {
        for (var actual = ex; actual is not null; actual = actual.InnerException)
            if (actual is PostgresException pg) return pg;
        return null;
    }

    private async Task ResponderPorCandado(HttpContext contexto, PostgresException pg)
    {
        switch (pg.SqlState)
        {
            case CandadoDeLaBase:   // P0001: nuestros RAISE con mensaje para una persona
                log.LogWarning(pg, "Candado de la base: {Mensaje}", pg.MessageText);
                await Responder(contexto, StatusCodes.Status409Conflict, pg.MessageText, "CANDADO");
                break;

            case PostgresErrorCodes.UniqueViolation:
                log.LogWarning(pg, "Violacion de unicidad: {Restriccion}", pg.ConstraintName);
                await Responder(contexto, StatusCodes.Status409Conflict, "Ese registro ya existe.", "DUPLICADO");
                break;

            case PostgresErrorCodes.ExclusionViolation:
                log.LogWarning(pg, "Violacion de exclusion: {Restriccion}", pg.ConstraintName);
                await Responder(contexto, StatusCodes.Status409Conflict,
                    "Ese periodo se cruza con otro ya registrado.", "PERIODO_SOLAPADO");
                break;

            case PostgresErrorCodes.CheckViolation:
                log.LogWarning(pg, "Violacion de CHECK: {Restriccion}", pg.ConstraintName);
                await Responder(contexto, StatusCodes.Status409Conflict,
                    "Los datos no cumplen una regla del sistema.", "REGLA");
                break;

            case PostgresErrorCodes.ForeignKeyViolation:
                log.LogWarning(pg, "Violacion de clave foranea: {Restriccion}", pg.ConstraintName);
                await Responder(contexto, StatusCodes.Status409Conflict,
                    "Ese dato depende de otro que no existe o que no se puede quitar.", "REFERENCIA");
                break;

            default:
                // Una PostgresException que no es un candado conocido SI es un
                // error del sistema: 500, pero registrado como tal.
                log.LogError(pg, "Error de base no esperado {Estado} en {Ruta}",
                    pg.SqlState, contexto.Request.Path);
                await Responder(contexto, StatusCodes.Status500InternalServerError,
                    "Ocurrio un error inesperado. Ya quedo registrado.", "ERROR_INTERNO");
                break;
        }
    }

    private static async Task Responder(HttpContext contexto, int codigo, string mensaje, string clave)
    {
        if (contexto.Response.HasStarted) return;

        contexto.Response.Clear();
        contexto.Response.StatusCode = codigo;
        contexto.Response.ContentType = "application/json; charset=utf-8";

        await contexto.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            mensaje,
            codigo = clave
        }));
    }
}
