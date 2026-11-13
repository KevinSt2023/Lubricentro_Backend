using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// El lado del taller de las cotizaciones (Fase 4). El tecnico registra
/// hallazgos; el asesor arma la cotizacion, la envia al cliente y, una vez
/// aprobada, la pasa a la orden. Empresa siempre desde el token.
///
/// El flujo lo cierra el cliente desde su portal (PortalCotizacionesController);
/// aqui nadie aprueba nada: la base rechaza cualquier atajo.
/// </summary>
[ApiController]
[Authorize]
public class CotizacionesController(
    ICotizacionServicio cotizaciones,
    ISesionActual sesion) : ControllerBase
{
    private int Empresa => sesion.EmpresaId;

    // ------------------------------ Hallazgos ------------------------------

    [HttpGet("api/ordenes/{ordenId:long}/hallazgos")]
    [RequierePermiso("ORDENES.VER")]
    public async Task<IActionResult> Hallazgos(long ordenId, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.HallazgosAsync(Empresa, ordenId, ct));

    [HttpPost("api/ordenes/{ordenId:long}/hallazgos")]
    [RequierePermiso("COTIZACION.HALLAZGO")]
    public async Task<IActionResult> RegistrarHallazgo(long ordenId, RegistrarHallazgoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.RegistrarHallazgoAsync(Empresa, sesion.UsuarioId, ordenId, peticion, ct));

    [HttpDelete("api/ordenes/{ordenId:long}/hallazgos/{hallazgoId:long}")]
    [RequierePermiso("COTIZACION.HALLAZGO")]
    public async Task<IActionResult> QuitarHallazgo(long ordenId, long hallazgoId, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.QuitarHallazgoAsync(Empresa, ordenId, hallazgoId, ct));

    // ---------------------------- Cotizaciones -----------------------------

    [HttpGet("api/ordenes/{ordenId:long}/cotizaciones")]
    [RequierePermiso("COTIZACION.VER")]
    public async Task<IActionResult> PorOrden(long ordenId, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.ListarPorOrdenAsync(Empresa, ordenId, ct));

    [HttpPost("api/ordenes/{ordenId:long}/cotizaciones")]
    [RequierePermiso("COTIZACION.CREAR")]
    public async Task<IActionResult> Crear(long ordenId, GuardarCotizacionPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.CrearAsync(Empresa, sesion.UsuarioId, ordenId, peticion, ct));

    [HttpGet("api/cotizaciones/{id:long}")]
    [RequierePermiso("COTIZACION.VER")]
    public async Task<IActionResult> Ver(long id, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.VerAsync(Empresa, id, ct));

    [HttpPut("api/cotizaciones/{id:long}")]
    [RequierePermiso("COTIZACION.CREAR")]
    public async Task<IActionResult> Actualizar(long id, GuardarCotizacionPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.ActualizarAsync(Empresa, id, peticion, ct));

    [HttpPost("api/cotizaciones/{id:long}/enviar")]
    [RequierePermiso("COTIZACION.ENVIAR")]
    public async Task<IActionResult> Enviar(long id, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.EnviarAsync(Empresa, sesion.UsuarioId, id, ct));

    [HttpPost("api/cotizaciones/{id:long}/aplicar")]
    [RequierePermiso("COTIZACION.CREAR")]
    public async Task<IActionResult> Aplicar(long id, CancellationToken ct) =>
        Respuestas.De(await cotizaciones.AplicarAsync(Empresa, sesion.UsuarioId, id, ct));
}
