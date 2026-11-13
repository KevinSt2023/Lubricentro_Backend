using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// El tablero operativo del taller para el ERP. Empresa desde el token. Lo ve
/// quien puede ver ordenes; los valores son el total de los trabajos
/// registrados, no caja cobrada.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tablero")]
public class TableroController(ITableroTallerServicio tablero) : ControllerBase
{
    [HttpGet]
    [RequierePermiso("ORDENES.VER")]
    public async Task<IActionResult> Ver(CancellationToken ct) => Ok(await tablero.VerAsync(ct));
}
