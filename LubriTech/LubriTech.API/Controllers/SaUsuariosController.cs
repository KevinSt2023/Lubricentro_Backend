using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// Consola del SuperAdmin: el padrón de usuarios de TODAS las empresas.
///
/// [SoloSuperAdmin] en la clase: aquí se cruza el filtro de empresa (el
/// servicio consulta con IgnoreQueryFilters), así que el candado va arriba y
/// no hay acción que se escape.
/// </summary>
[ApiController]
[Authorize]
[SoloSuperAdmin]
[Route("api/sa/usuarios")]
public class SaUsuariosController(IEmpresaConsolaServicio empresas) : ControllerBase
{
    /// <summary>
    /// Todos los usuarios, paginado. Filtros opcionales: texto (nombre, correo
    /// o DNI), empresa, rol y estado.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] int? empresaId,
        [FromQuery] int? rolId,
        [FromQuery] bool? activo,
        CancellationToken ct) =>
        Ok(await empresas.ListarUsuariosAsync(consulta, empresaId, rolId, activo, ct));
}
