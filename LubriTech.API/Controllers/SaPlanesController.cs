using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>Consola del SuperAdmin: planes, funciones y cupos.</summary>
[ApiController]
[Authorize]
[SoloSuperAdmin]
[Route("api/sa/planes")]
public class SaPlanesController(IPlanConsolaServicio planes) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Ok(await planes.ListarAsync(ct));

    /// <summary>El catalogo de funciones vendibles, para armar la pantalla.</summary>
    [HttpGet("funciones")]
    public async Task<IActionResult> Funciones(CancellationToken ct) =>
        Ok(await planes.FuncionesAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Ver(int id, CancellationToken ct) =>
        Respuestas.De(await planes.VerAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Crear(GuardarPlanPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await planes.CrearAsync(peticion, ct));

    /// <summary>
    /// Si el cambio dejaria empresas fuera de norma, responde 409 diciendo
    /// cuales y por que, en vez de dejarlas excedidas en silencio.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(
        int id, GuardarPlanPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await planes.ActualizarAsync(id, peticion, ct));
}

/// <summary>Consola del SuperAdmin: el tablero del negocio.</summary>
[ApiController]
[Authorize]
[SoloSuperAdmin]
[Route("api/sa/tablero")]
public class SaTableroController(ITableroSaaSServicio tablero) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Ver(CancellationToken ct) =>
        Ok(await tablero.VerAsync(ct));
}

/// <summary>Consola del SuperAdmin: todos los pagos, de todas las empresas.</summary>
[ApiController]
[Authorize]
[SoloSuperAdmin]
[Route("api/sa/suscripciones")]
public class SaSuscripcionesController(ISuscripcionConsolaServicio suscripciones) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] int? empresaId,
        CancellationToken ct) =>
        Ok(await suscripciones.ListarAsync(consulta, empresaId, ct));

    /// <summary>
    /// Anula un pago y recalcula hasta cuando llega el servicio a partir de
    /// los pagos que quedan vigentes.
    /// </summary>
    [HttpPost("{id:long}/anular")]
    public async Task<IActionResult> Anular(
        long id, AnularSuscripcionPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await suscripciones.AnularAsync(id, peticion, ct));
}
