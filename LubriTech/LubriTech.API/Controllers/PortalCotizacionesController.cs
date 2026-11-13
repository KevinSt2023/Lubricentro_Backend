using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// El portal del cliente para las cotizaciones (Fase 4). Solo un token de
/// cliente entra aqui, y solo ve y decide sobre las cotizaciones de SUS
/// vehiculos. Una cotizacion ajena responde 404, nunca 403 con datos.
///
/// La decision que se guarda aqui es la unica forma de aprobar o rechazar:
/// queda inmutable y, por trigger, mueve el estado de la cotizacion.
/// </summary>
[ApiController]
[Authorize]
[SoloCliente]
[Route("api/portal/cotizaciones")]
public class PortalCotizacionesController(
    ICotizacionServicio cotizaciones,
    ISesionActual sesion) : ControllerBase
{
    private int Empresa => sesion.EmpresaId;

    [HttpGet]
    public async Task<IActionResult> Mias(CancellationToken ct)
    {
        if (sesion.ClienteId is not long cli)
            return Respuestas.De(TipoFallo.Prohibido, "La sesion no resuelve el cliente.", "SIN_CLIENTE");
        return Ok(await cotizaciones.PortalListarAsync(Empresa, cli, ct));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Ver(long id, CancellationToken ct)
    {
        if (sesion.ClienteId is not long cli)
            return Respuestas.De(TipoFallo.Prohibido, "La sesion no resuelve el cliente.", "SIN_CLIENTE");
        return Respuestas.De(await cotizaciones.PortalVerAsync(Empresa, cli, id, ct));
    }

    [HttpPost("{id:long}/decision")]
    public async Task<IActionResult> Decidir(long id, DecidirCotizacionPeticion peticion, CancellationToken ct)
    {
        if (sesion.ClienteId is not long cli)
            return Respuestas.De(TipoFallo.Prohibido, "La sesion no resuelve el cliente.", "SIN_CLIENTE");
        return Respuestas.De(await cotizaciones.DecidirAsync(Empresa, cli, id, peticion, sesion.Ip, ct));
    }
}
