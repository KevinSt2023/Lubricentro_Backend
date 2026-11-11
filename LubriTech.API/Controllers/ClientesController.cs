using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>Los clientes del taller. Empresa siempre desde el token.</summary>
[ApiController]
[Authorize]
[Route("api/clientes")]
public class ClientesController(IClienteServicio clientes, ISesionActual sesion) : ControllerBase
{
    [HttpGet]
    [RequierePermiso("CLIENTES.VER")]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] string? tipo, [FromQuery] bool? activo, CancellationToken ct) =>
        Ok(await clientes.ListarAsync(sesion.EmpresaId, consulta, tipo, activo, ct));

    [HttpGet("{id:long}")]
    [RequierePermiso("CLIENTES.VER")]
    public async Task<IActionResult> Ver(long id, CancellationToken ct) =>
        Respuestas.De(await clientes.VerAsync(sesion.EmpresaId, id, ct));

    [HttpPost]
    [RequierePermiso("CLIENTES.CREAR")]
    public async Task<IActionResult> Crear(GuardarClientePeticion peticion, CancellationToken ct) =>
        Respuestas.De(await clientes.CrearAsync(sesion.EmpresaId, peticion, ct));

    [HttpPut("{id:long}")]
    [RequierePermiso("CLIENTES.EDITAR")]
    public async Task<IActionResult> Actualizar(long id, GuardarClientePeticion peticion, CancellationToken ct) =>
        Respuestas.De(await clientes.ActualizarAsync(sesion.EmpresaId, id, peticion, ct));

    [HttpPut("{id:long}/estado")]
    [RequierePermiso("CLIENTES.EDITAR")]
    public async Task<IActionResult> CambiarEstado(long id, EstadoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await clientes.CambiarEstadoAsync(sesion.EmpresaId, id, peticion, ct));

    /// <summary>Abre la cuenta del portal del cliente. Devuelve la clave temporal una vez.</summary>
    [HttpPost("{id:long}/portal")]
    [RequierePermiso("CLIENTES.PORTAL")]
    public async Task<IActionResult> HabilitarPortal(long id, CancellationToken ct) =>
        Respuestas.De(await clientes.HabilitarPortalAsync(sesion.EmpresaId, id, ct));
}
