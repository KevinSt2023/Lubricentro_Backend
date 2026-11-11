using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>Los vehiculos del taller. Empresa siempre desde el token.</summary>
[ApiController]
[Authorize]
[Route("api/vehiculos")]
public class VehiculosController(IVehiculoServicio vehiculos, ISesionActual sesion) : ControllerBase
{
    [HttpGet]
    [RequierePermiso("VEHICULOS.VER")]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] long? clienteId, [FromQuery] bool? activo, CancellationToken ct) =>
        Ok(await vehiculos.ListarAsync(sesion.EmpresaId, consulta, clienteId, activo, ct));

    [HttpGet("{id:long}")]
    [RequierePermiso("VEHICULOS.VER")]
    public async Task<IActionResult> Ver(long id, CancellationToken ct) =>
        Respuestas.De(await vehiculos.VerAsync(sesion.EmpresaId, id, ct));

    [HttpPost]
    [RequierePermiso("VEHICULOS.CREAR")]
    public async Task<IActionResult> Crear(GuardarVehiculoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await vehiculos.CrearAsync(sesion.EmpresaId, peticion, ct));

    [HttpPut("{id:long}")]
    [RequierePermiso("VEHICULOS.EDITAR")]
    public async Task<IActionResult> Actualizar(long id, GuardarVehiculoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await vehiculos.ActualizarAsync(sesion.EmpresaId, id, peticion, ct));

    [HttpPut("{id:long}/estado")]
    [RequierePermiso("VEHICULOS.EDITAR")]
    public async Task<IActionResult> CambiarEstado(long id, EstadoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await vehiculos.CambiarEstadoAsync(sesion.EmpresaId, id, peticion, ct));

    /// <summary>Anota la lectura del odometro. Nunca por debajo de la anterior.</summary>
    [HttpPut("{id:long}/kilometraje")]
    [RequierePermiso("VEHICULOS.EDITAR")]
    public async Task<IActionResult> RegistrarKilometraje(
        long id, KilometrajePeticion peticion, CancellationToken ct) =>
        Respuestas.De(await vehiculos.RegistrarKilometrajeAsync(sesion.EmpresaId, id, peticion, ct));
}
