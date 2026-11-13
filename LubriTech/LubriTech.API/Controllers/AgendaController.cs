using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// La agenda del taller (Fase 5). Una cita es una orden en CITA_REGISTRADA con
/// fecha. Ver la agenda pide AGENDA.VER; agendar, reprogramar, recibir y
/// cancelar piden AGENDA.AGENDAR. La empresa sale del token.
/// </summary>
[ApiController]
[Authorize]
[Route("api/agenda")]
public class AgendaController(
    IAgendaServicio agenda,
    IPermisoServicio permisos,
    ISesionActual sesion) : ControllerBase
{
    private int Empresa => sesion.EmpresaId;

    private async Task<bool> PuedeVerCostosAsync() =>
        sesion.EsSuperAdmin ||
        (sesion.RolId is int r && await permisos.TienePermisoAsync(Empresa, r, "ORDENES.COSTOS"));

    /// <summary>Las citas en un rango. Por defecto, de hoy a dos semanas.</summary>
    [HttpGet]
    [RequierePermiso("AGENDA.VER")]
    public async Task<IActionResult> Listar(
        [FromQuery] DateTimeOffset? desde, [FromQuery] DateTimeOffset? hasta,
        [FromQuery] int? sedeId, [FromQuery] int? tecnicoId, CancellationToken ct)
    {
        var d = desde ?? Tiempo.InicioDelDiaLima(Tiempo.HoyLima());
        var h = hasta ?? Tiempo.InicioDelDiaLima(Tiempo.HoyLima().AddDays(14)).AddDays(1).AddSeconds(-1);
        return Respuestas.De(await agenda.ListarAsync(Empresa, d, h, sedeId, tecnicoId, ct));
    }

    [HttpPost]
    [RequierePermiso("AGENDA.AGENDAR")]
    public async Task<IActionResult> Agendar(CrearCitaPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await agenda.CrearCitaAsync(Empresa, sesion.UsuarioId, peticion, await PuedeVerCostosAsync(), ct));

    [HttpPut("{id:long}")]
    [RequierePermiso("AGENDA.AGENDAR")]
    public async Task<IActionResult> Reprogramar(long id, ReprogramarCitaPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await agenda.ReprogramarAsync(Empresa, sesion.UsuarioId, id, peticion, ct));

    [HttpPost("{id:long}/recibir")]
    [RequierePermiso("AGENDA.AGENDAR")]
    public async Task<IActionResult> Recibir(long id, RecibirCitaPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await agenda.RecibirAsync(Empresa, sesion.UsuarioId, id, peticion, await PuedeVerCostosAsync(), ct));

    [HttpPost("{id:long}/cancelar")]
    [RequierePermiso("AGENDA.AGENDAR")]
    public async Task<IActionResult> Cancelar(long id, AnularOrdenPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await agenda.CancelarAsync(Empresa, sesion.UsuarioId, id, peticion, await PuedeVerCostosAsync(), ct));

    /// <summary>Vehículos con mantenimiento vencido o por vencer (por fecha o por km).</summary>
    [HttpGet("proximos-mantenimientos")]
    [RequierePermiso("AGENDA.VER")]
    public async Task<IActionResult> ProximosMantenimientos(
        [FromQuery] int? dias, [FromQuery] int? km, CancellationToken ct) =>
        Respuestas.De(await agenda.ProximosMantenimientosAsync(Empresa, dias ?? 60, km ?? 1000, ct));
}
