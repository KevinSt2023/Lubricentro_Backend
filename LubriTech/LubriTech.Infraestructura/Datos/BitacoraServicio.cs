using System.Text.Json;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Entidades;
using Microsoft.Extensions.Logging;

namespace LubriTech.Infraestructura.Datos;

/// <summary>
/// Escribe el rastro. La tabla es inmutable en la base, asi que lo que entra
/// aqui no se puede retocar despues ni desde la aplicacion ni desde psql.
///
/// No lanza: que falle la auditoria no debe tumbar la operacion del usuario.
/// Pero queda en el log con nivel de error, porque un rastro que se pierde
/// en silencio es peor que uno que no existe.
/// </summary>
public class BitacoraServicio(
    LubriTechContexto bd,
    ISesionActual sesion,
    ILogger<BitacoraServicio> log) : IBitacoraServicio
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public async Task RegistrarAsync(string accion, string entidad, string? entidadId = null,
                                     object? detalle = null, CancellationToken ct = default)
    {
        try
        {
            bd.Bitacoras.Add(new Bitacora
            {
                EmpresaId = sesion.EmpresaId > 0 ? sesion.EmpresaId : null,
                UsuarioId = sesion.UsuarioId,
                AccesoId = sesion.AccesoId,
                SedeId = sesion.SedeId,
                Accion = accion,
                Entidad = entidad,
                EntidadId = entidadId,
                Detalle = detalle is null ? null : JsonSerializer.Serialize(detalle, Json),
                Ip = sesion.Ip,
                Agente = Recortar(sesion.Agente, 500),
                Origen = sesion.Origen
            });

            await bd.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "No se pudo escribir la bitacora: {Accion} {Entidad} {EntidadId}",
                accion, entidad, entidadId);
        }
    }

    private static string? Recortar(string? valor, int largo) =>
        valor is null || valor.Length <= largo ? valor : valor[..largo];
}
