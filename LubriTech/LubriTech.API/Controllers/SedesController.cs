using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// Las sedes de la empresa, vistas desde dentro: esto lo usa el Admin, no el
/// SuperAdmin.
///
/// La empresa sale SIEMPRE del token (<c>sesion.EmpresaId</c>), nunca de la
/// ruta ni del cuerpo. Es la diferencia entre este controlador y el de la
/// consola: aqui nadie puede pedir la sede de otra empresa porque no hay
/// donde escribir el id de otra empresa.
///
/// El reparto de permisos:
///   * ver y editar lo que ya existe   -> CONFIGURACION.VER / CONFIGURACION.EDITAR
///   * abrir OTRA sede o mover la principal -> SEDES.GESTIONAR, que el plan
///     habilita solo con la funcion MULTISEDE.
/// Tener un local se configura; tener varios se paga.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sedes")]
public class SedesController(ISedeServicio sedes, ISesionActual sesion) : ControllerBase
{
    [HttpGet]
    [RequierePermiso("CONFIGURACION.VER")]
    public async Task<IActionResult> Listar(CancellationToken ct) =>
        Respuestas.De(await sedes.ListarAsync(sesion.EmpresaId, ct));

    [HttpPost]
    [RequierePermiso("SEDES.GESTIONAR")]
    public async Task<IActionResult> Crear(GuardarSedePeticion peticion, CancellationToken ct) =>
        Respuestas.De(await sedes.CrearAsync(sesion.EmpresaId, peticion, ct));

    [HttpPut("{sedeId:int}")]
    [RequierePermiso("CONFIGURACION.EDITAR")]
    public async Task<IActionResult> Actualizar(
        int sedeId, GuardarSedePeticion peticion, CancellationToken ct) =>
        Respuestas.De(await sedes.ActualizarAsync(sesion.EmpresaId, sedeId, peticion, ct));

    [HttpPut("{sedeId:int}/principal")]
    [RequierePermiso("SEDES.GESTIONAR")]
    public async Task<IActionResult> MarcarPrincipal(int sedeId, CancellationToken ct) =>
        Respuestas.De(await sedes.MarcarPrincipalAsync(sesion.EmpresaId, sedeId, ct));
}
