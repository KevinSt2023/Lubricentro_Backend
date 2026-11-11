using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// Consola del SuperAdmin: las empresas del SaaS.
///
/// [SoloSuperAdmin] va en la clase, no en cada accion: aqui dentro se
/// desactiva el filtro de empresa para poder ver todas, asi que una accion
/// que se olvide del atributo seria una fuga. Puesto arriba, no hay forma de
/// olvidarlo.
/// </summary>
[ApiController]
[Authorize]
[SoloSuperAdmin]
[Route("api/sa/empresas")]
public class SaEmpresasController(
    IEmpresaConsolaServicio empresas,
    ISedeServicio sedes,
    ISuscripcionConsolaServicio suscripciones) : ControllerBase
{
    /// <summary>
    /// Listado paginado. <paramref name="estado"/> acepta AL_DIA, POR_VENCER,
    /// VENCIDO e INACTIVA.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] string? estado,
        [FromQuery] int? planId,
        CancellationToken ct) =>
        Ok(await empresas.ListarAsync(consulta, estado, planId, ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Ver(int id, CancellationToken ct) =>
        Respuestas.De(await empresas.VerAsync(id, ct));

    /// <summary>
    /// Alta completa: empresa + sede principal + Admin, en una sola
    /// transaccion. Devuelve la clave temporal del Admin UNA vez.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Crear(CrearEmpresaPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await empresas.CrearAsync(peticion, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Actualizar(
        int id, ActualizarEmpresaPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await empresas.ActualizarAsync(id, peticion, ct));

    [HttpPut("{id:int}/plan")]
    public async Task<IActionResult> CambiarPlan(
        int id, CambiarPlanPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await empresas.CambiarPlanAsync(id, peticion, ct));

    /// <summary>Dar de baja o reactivar. La baja corta las sesiones al instante.</summary>
    [HttpPut("{id:int}/estado")]
    public async Task<IActionResult> CambiarEstado(
        int id, EstadoEmpresaPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await empresas.CambiarEstadoAsync(id, peticion, ct));

    /// <summary>Salud y cumplimiento: que hay que mirar de esta empresa.</summary>
    [HttpGet("{id:int}/salud")]
    public async Task<IActionResult> Salud(int id, CancellationToken ct) =>
        Respuestas.De(await empresas.SaludAsync(id, ct));

    /// <summary>Clave temporal nueva para el Admin. Cierra sus sesiones abiertas.</summary>
    [HttpPost("{id:int}/clave-admin")]
    public async Task<IActionResult> ReiniciarClaveDelAdmin(int id, CancellationToken ct)
    {
        var resultado = await empresas.ReiniciarClaveDelAdminAsync(id, ct);

        return resultado.Exito
            ? Ok(new { claveTemporal = resultado.Valor })
            : Respuestas.De(resultado.Fallo, resultado.Mensaje!, resultado.Codigo);
    }

    // ---------------------------- Sedes --------------------------------

    [HttpGet("{id:int}/sedes")]
    public async Task<IActionResult> Sedes(int id, CancellationToken ct) =>
        Respuestas.De(await sedes.ListarAsync(id, ct));

    [HttpPost("{id:int}/sedes")]
    public async Task<IActionResult> CrearSede(
        int id, GuardarSedePeticion peticion, CancellationToken ct) =>
        Respuestas.De(await sedes.CrearAsync(id, peticion, ct));

    [HttpPut("{id:int}/sedes/{sedeId:int}")]
    public async Task<IActionResult> ActualizarSede(
        int id, int sedeId, GuardarSedePeticion peticion, CancellationToken ct) =>
        Respuestas.De(await sedes.ActualizarAsync(id, sedeId, peticion, ct));

    [HttpPut("{id:int}/sedes/{sedeId:int}/principal")]
    public async Task<IActionResult> MarcarSedePrincipal(
        int id, int sedeId, CancellationToken ct) =>
        Respuestas.De(await sedes.MarcarPrincipalAsync(id, sedeId, ct));

    // ------------------------ Suscripciones ----------------------------

    [HttpGet("{id:int}/suscripciones")]
    public async Task<IActionResult> Suscripciones(
        int id, [FromQuery] ConsultaPaginada consulta, CancellationToken ct) =>
        Ok(await suscripciones.ListarAsync(consulta, id, ct));

    /// <summary>
    /// Registra el pago y extiende el servicio. El periodo encadena con el
    /// ultimo pagado; si estaba vencido, arranca hoy.
    /// </summary>
    [HttpPost("{id:int}/suscripciones")]
    public async Task<IActionResult> RegistrarPago(
        int id, RegistrarSuscripcionPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await suscripciones.RegistrarAsync(id, peticion, ct));
}
