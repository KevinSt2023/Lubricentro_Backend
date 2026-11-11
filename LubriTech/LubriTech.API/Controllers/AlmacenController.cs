using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// El almacen: productos, stock por sede y kardex. Empresa siempre desde el
/// token; la sede de cada movimiento se valida contra la empresa.
///
/// Mantener el catalogo (crear/editar/estado) y mover stock van bajo
/// ALMACEN.INGRESAR; el ajuste de inventario bajo ALMACEN.AJUSTAR, que es un
/// permiso aparte porque corrige cantidades y no todo el mundo debe tocarlo.
/// </summary>
[ApiController]
[Authorize]
[Route("api/almacen/productos")]
public class AlmacenController(IAlmacenServicio almacen, ISesionActual sesion) : ControllerBase
{
    [HttpGet]
    [RequierePermiso("ALMACEN.VER")]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] string? tipo, [FromQuery] bool? activo, [FromQuery] bool bajoMinimo,
        CancellationToken ct) =>
        Ok(await almacen.ListarAsync(sesion.EmpresaId, consulta, tipo, activo, bajoMinimo, ct));

    [HttpGet("{id:long}")]
    [RequierePermiso("ALMACEN.VER")]
    public async Task<IActionResult> Ver(long id, CancellationToken ct) =>
        Respuestas.De(await almacen.VerAsync(sesion.EmpresaId, id, ct));

    /// <summary>Busca por codigo de barras. Para el lector del almacen.</summary>
    [HttpGet("barras/{codigo}")]
    [RequierePermiso("ALMACEN.VER")]
    public async Task<IActionResult> PorBarras(string codigo, CancellationToken ct) =>
        Respuestas.De(await almacen.PorCodigoBarrasAsync(sesion.EmpresaId, codigo, ct));

    [HttpPost]
    [RequierePermiso("ALMACEN.INGRESAR")]
    public async Task<IActionResult> Crear(GuardarProductoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await almacen.CrearAsync(sesion.EmpresaId, peticion, ct));

    [HttpPut("{id:long}")]
    [RequierePermiso("ALMACEN.INGRESAR")]
    public async Task<IActionResult> Actualizar(long id, GuardarProductoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await almacen.ActualizarAsync(sesion.EmpresaId, id, peticion, ct));

    [HttpPut("{id:long}/estado")]
    [RequierePermiso("ALMACEN.INGRESAR")]
    public async Task<IActionResult> CambiarEstado(long id, EstadoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await almacen.CambiarEstadoAsync(sesion.EmpresaId, id, peticion, ct));

    /// <summary>Ingresa mercaderia a una sede. Escribe kardex; el stock lo mueve la base.</summary>
    [HttpPost("{id:long}/ingresos")]
    [RequierePermiso("ALMACEN.INGRESAR")]
    public async Task<IActionResult> Ingresar(long id, IngresoStockPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await almacen.IngresarAsync(sesion.EmpresaId, id, sesion.UsuarioId, peticion, ct));

    /// <summary>Ajusta el stock de una sede al valor contado.</summary>
    [HttpPost("{id:long}/ajustes")]
    [RequierePermiso("ALMACEN.AJUSTAR")]
    public async Task<IActionResult> Ajustar(long id, AjusteStockPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await almacen.AjustarAsync(sesion.EmpresaId, id, sesion.UsuarioId, peticion, ct));

    /// <summary>El kardex del producto, lo mas reciente primero. Paginado.</summary>
    [HttpGet("{id:long}/kardex")]
    [RequierePermiso("ALMACEN.VER")]
    public async Task<IActionResult> Kardex(
        long id, [FromQuery] ConsultaPaginada consulta, [FromQuery] int? sedeId, CancellationToken ct) =>
        Respuestas.De(await almacen.KardexAsync(sesion.EmpresaId, id, consulta, sedeId, ct));
}
