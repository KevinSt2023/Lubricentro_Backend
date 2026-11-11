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
        catch (PostgresException ex) when (ex.SqlState == CandadoDeLaBase)
        {
            log.LogWarning(ex, "Candado de la base: {Mensaje}", ex.MessageText);
            await Responder(contexto, StatusCodes.Status409Conflict, ex.MessageText, "CANDADO");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            log.LogWarning(ex, "Violacion de unicidad: {Restriccion}", ex.ConstraintName);
            await Responder(contexto, StatusCodes.Status409Conflict,
                "Ese registro ya existe.", "DUPLICADO");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ExclusionViolation)
        {
            // Un periodo que se solapa con otro ya pagado (ex_suscripcion_sin_solape).
            log.LogWarning(ex, "Violacion de exclusion: {Restriccion}", ex.ConstraintName);
            await Responder(contexto, StatusCodes.Status409Conflict,
                "Ese periodo se cruza con otro ya registrado.", "PERIODO_SOLAPADO");
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.CheckViolation)
        {
            log.LogWarning(ex, "Violacion de CHECK: {Restriccion}", ex.ConstraintName);
            await Responder(contexto, StatusCodes.Status409Conflict,
                "Los datos no cumplen una regla del sistema.", "REGLA");
        }
        catch (OperationCanceledException) when (contexto.RequestAborted.IsCancellationRequested)
        {
            // El cliente corto la conexion. No es un error: no hay a quien responder.
            log.LogDebug("Peticion cancelada por el cliente: {Ruta}", contexto.Request.Path);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error no manejado en {Metodo} {Ruta}",
                contexto.Request.Method, contexto.Request.Path);

            // Hacia afuera, nada de detalles internos.
            await Responder(contexto, StatusCodes.Status500InternalServerError,
                "Ocurrio un error inesperado. Ya quedo registrado.", "ERROR_INTERNO");
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
