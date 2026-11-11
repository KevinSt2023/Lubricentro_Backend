using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// Las dos puertas, separadas a proposito:
///   POST /api/auth/login          personal del taller  -> audiencia ERP
///   POST /api/auth/cliente/login  dueno del vehiculo   -> audiencia cliente
///
/// No es una puerta con un parametro "tipo": son rutas distintas, tokens
/// distintos y reglas distintas. Mezclarlas es como termina un cliente
/// viendo el panel de administracion.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AutenticacionController(
    IAutenticacionServicio auth,
    IBitacoraServicio bitacora) : ControllerBase
{
    // ----------------------------- Personal -----------------------------

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginPeticion peticion, CancellationToken ct)
    {
        var resultado = await auth.LoginPersonalAsync(peticion, ct);

        if (resultado.Exito)
            await bitacora.RegistrarAsync("AUTH.LOGIN", "usuario",
                resultado.Valor!.Perfil.UsuarioId?.ToString(), null, ct);

        return Respuestas.De(resultado);
    }

    [AllowAnonymous]
    [HttpPost("refrescar")]
    public async Task<IActionResult> Refrescar(RefrescarPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await auth.RefrescarPersonalAsync(peticion, ct));

    // ------------------------ Portal del cliente ------------------------

    [AllowAnonymous]
    [HttpPost("cliente/login")]
    public async Task<IActionResult> LoginCliente(LoginPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await auth.LoginClienteAsync(peticion, ct));

    [AllowAnonymous]
    [HttpPost("cliente/refrescar")]
    public async Task<IActionResult> RefrescarCliente(RefrescarPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await auth.RefrescarClienteAsync(peticion, ct));

    // --------------------------- Compartido -----------------------------

    /// <summary>
    /// El perfil de quien sea que este pidiendo. El personal recibe sus
    /// permisos efectivos; el cliente no recibe ninguno, porque no tiene.
    /// </summary>
    [Authorize]
    [HttpGet("perfil")]
    public async Task<IActionResult> Perfil(CancellationToken ct) =>
        Respuestas.De(await auth.PerfilAsync(ct));

    [Authorize]
    [HttpPost("cerrar")]
    public async Task<IActionResult> Cerrar(RefrescarPeticion? peticion, CancellationToken ct)
    {
        await bitacora.RegistrarAsync("AUTH.CERRAR", "sesion", null, null, ct);
        return Respuestas.De(await auth.CerrarSesionAsync(peticion?.RefreshToken, ct));
    }

    /// <summary>Cierra la sesion en todos los dispositivos.</summary>
    [Authorize]
    [HttpPost("cerrar-todo")]
    public async Task<IActionResult> CerrarTodo(CancellationToken ct)
    {
        var resultado = await auth.CerrarTodasLasSesionesAsync(ct);
        if (resultado.Exito)
            await bitacora.RegistrarAsync("AUTH.CERRAR_TODO", "sesion", null, null, ct);

        return Respuestas.De(resultado);
    }

    [Authorize]
    [HttpPost("cambiar-clave")]
    public async Task<IActionResult> CambiarClave(CambiarClavePeticion peticion, CancellationToken ct)
    {
        var resultado = await auth.CambiarClaveAsync(peticion, ct);
        if (resultado.Exito)
            await bitacora.RegistrarAsync("AUTH.CAMBIO_CLAVE", "sesion", null, null, ct);

        return Respuestas.De(resultado);
    }
}
