using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// El personal de la empresa, para su propio Admin. La empresa sale SIEMPRE de
/// <c>sesion.EmpresaId</c>: nadie puede listar ni tocar personal de otra
/// empresa porque no hay donde escribir el id de otra.
/// </summary>
[ApiController]
[Authorize]
[Route("api/personal")]
public class PersonalController(IPersonalServicio personal, ISesionActual sesion) : ControllerBase
{
    [HttpGet]
    [RequierePermiso("PERSONAL.VER")]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] int? rolId, [FromQuery] int? sedeId, [FromQuery] bool? activo,
        CancellationToken ct) =>
        Ok(await personal.ListarAsync(sesion.EmpresaId, consulta, rolId, sedeId, activo, ct));

    [HttpGet("{id:int}")]
    [RequierePermiso("PERSONAL.VER")]
    public async Task<IActionResult> Ver(int id, CancellationToken ct) =>
        Respuestas.De(await personal.VerAsync(sesion.EmpresaId, id, ct));

    [HttpPost]
    [RequierePermiso("PERSONAL.CREAR")]
    public async Task<IActionResult> Crear(GuardarPersonalPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await personal.CrearAsync(sesion.EmpresaId, peticion, ct));

    [HttpPut("{id:int}")]
    [RequierePermiso("PERSONAL.EDITAR")]
    public async Task<IActionResult> Actualizar(
        int id, ActualizarPersonalPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await personal.ActualizarAsync(sesion.EmpresaId, id, peticion, ct));

    [HttpPut("{id:int}/estado")]
    [RequierePermiso("PERSONAL.EDITAR")]
    public async Task<IActionResult> CambiarEstado(int id, EstadoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await personal.CambiarEstadoAsync(sesion.EmpresaId, id, peticion, ct));

    [HttpPost("{id:int}/clave")]
    [RequierePermiso("PERSONAL.EDITAR")]
    public async Task<IActionResult> ReiniciarClave(int id, CancellationToken ct)
    {
        var r = await personal.ReiniciarClaveAsync(sesion.EmpresaId, id, ct);
        return r.Exito
            ? Ok(new { claveTemporal = r.Valor })
            : Respuestas.De(r.Fallo, r.Mensaje!, r.Codigo);
    }
}
