using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>El catalogo de servicios del taller. Empresa siempre desde el token.</summary>
[ApiController]
[Authorize]
[Route("api/servicios")]
public class ServiciosController(IServicioCatalogoServicio servicios, ISesionActual sesion) : ControllerBase
{
    [HttpGet]
    [RequierePermiso("SERVICIOS.VER")]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] string? categoria, [FromQuery] bool? activo, CancellationToken ct) =>
        Ok(await servicios.ListarAsync(sesion.EmpresaId, consulta, categoria, activo, ct));

    [HttpGet("{id:long}")]
    [RequierePermiso("SERVICIOS.VER")]
    public async Task<IActionResult> Ver(long id, CancellationToken ct) =>
        Respuestas.De(await servicios.VerAsync(sesion.EmpresaId, id, ct));

    [HttpPost]
    [RequierePermiso("SERVICIOS.EDITAR")]
    public async Task<IActionResult> Crear(GuardarServicioPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await servicios.CrearAsync(sesion.EmpresaId, peticion, ct));

    [HttpPut("{id:long}")]
    [RequierePermiso("SERVICIOS.EDITAR")]
    public async Task<IActionResult> Actualizar(long id, GuardarServicioPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await servicios.ActualizarAsync(sesion.EmpresaId, id, peticion, ct));

    [HttpPut("{id:long}/estado")]
    [RequierePermiso("SERVICIOS.EDITAR")]
    public async Task<IActionResult> CambiarEstado(long id, EstadoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await servicios.CambiarEstadoAsync(sesion.EmpresaId, id, peticion, ct));
}
