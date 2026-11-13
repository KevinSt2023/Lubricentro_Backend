using System.Security.Cryptography;
using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LubriTech.API.Controllers;

/// <summary>
/// Las ordenes de trabajo. Empresa siempre desde el token. El reparto de
/// permisos sigue lo que cerramos: cada accion bajo su permiso, y los costos
/// solo para quien tiene ORDENES.COSTOS.
/// </summary>
[ApiController]
[Authorize]
[Route("api/ordenes")]
public class OrdenesController(
    IOrdenServicio ordenes,
    IPermisoServicio permisos,
    IEvidenciaAlmacen evidenciaAlmacen,
    ISesionActual sesion) : ControllerBase
{
    private int Empresa => sesion.EmpresaId;

    private async Task<bool> PuedeVerCostosAsync() =>
        sesion.EsSuperAdmin ||
        (sesion.RolId is int r && await permisos.TienePermisoAsync(Empresa, r, "ORDENES.COSTOS"));

    // ----------------------------- Catalogo --------------------------------

    [HttpGet("estados")]
    [RequierePermiso("ORDENES.VER")]
    public async Task<IActionResult> Estados(CancellationToken ct) => Ok(await ordenes.EstadosAsync(ct));

    // ------------------------------ Listado --------------------------------

    /// <summary>
    /// Lista las ordenes. Quien tiene ORDENES.VER ve todas; quien solo tiene
    /// ORDENES.VER_ASIGNADAS (un tecnico) ve las suyas MAS la cola sin asignar
    /// de su sede, que puede tomar.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] ConsultaPaginada consulta,
        [FromQuery] int? estadoId, [FromQuery] int? tecnicoId, [FromQuery] int? sedeId,
        [FromQuery] long? clienteId, [FromQuery] long? vehiculoId, CancellationToken ct)
    {
        var rol = sesion.RolId;
        var veTodas = sesion.EsSuperAdmin ||
            (rol is int r && await permisos.TienePermisoAsync(Empresa, r, "ORDENES.VER"));
        var veAsignadas = rol is int r2 && await permisos.TienePermisoAsync(Empresa, r2, "ORDENES.VER_ASIGNADAS");

        if (!veTodas && !veAsignadas)
            return Respuestas.De(TipoFallo.Prohibido, "No tienes permiso para ver ordenes.", "SIN_PERMISO");

        int? soloDelTecnico = (!veTodas && veAsignadas) ? sesion.UsuarioId : null;
        // El tecnico ve, ademas de las suyas, la cola de su sede sin asignar.
        int? poolSedeTecnico = soloDelTecnico.HasValue ? sesion.SedeId : null;

        return Ok(await ordenes.ListarAsync(Empresa, consulta, estadoId, tecnicoId, sedeId,
            clienteId, vehiculoId, soloDelTecnico, poolSedeTecnico, ct));
    }

    /// <summary>
    /// El detalle. Con ORDENES.VER se abre cualquiera; con solo
    /// ORDENES.VER_ASIGNADAS (un tecnico) solo la suya o una sin asignar de su sede.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Ver(long id, CancellationToken ct)
    {
        var rol = sesion.RolId;
        var veTodas = sesion.EsSuperAdmin ||
            (rol is int r && await permisos.TienePermisoAsync(Empresa, r, "ORDENES.VER"));
        var veAsignadas = rol is int r2 && await permisos.TienePermisoAsync(Empresa, r2, "ORDENES.VER_ASIGNADAS");

        if (!veTodas && !veAsignadas)
            return Respuestas.De(TipoFallo.Prohibido, "No tienes permiso para ver ordenes.", "SIN_PERMISO");

        var resultado = await ordenes.VerAsync(Empresa, id, await PuedeVerCostosAsync(), ct);

        // El tecnico solo abre lo que puede tener en su tablero: lo suyo o la
        // cola sin asignar de su sede. Cualquier otra, 404 (no se filtra que existe).
        if (resultado.Exito && !veTodas && veAsignadas && resultado.Valor is { } d)
        {
            var suya = d.TecnicoId == sesion.UsuarioId
                || (d.TecnicoId is null && d.SedeId == sesion.SedeId);
            if (!suya)
                return Respuestas.De(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");
        }

        return Respuestas.De(resultado);
    }

    // ------------------------------ Ciclo ----------------------------------

    [HttpPost]
    [RequierePermiso("ORDENES.CREAR")]
    public async Task<IActionResult> Crear(CrearOrdenPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.CrearAsync(Empresa, sesion.UsuarioId, peticion, await PuedeVerCostosAsync(), ct));

    [HttpPut("{id:long}")]
    [RequierePermiso("ORDENES.ESTADO")]
    public async Task<IActionResult> Actualizar(long id, ActualizarOrdenPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.ActualizarAsync(Empresa, id, peticion, await PuedeVerCostosAsync(), ct));

    [HttpPut("{id:long}/estado")]
    [RequierePermiso("ORDENES.ESTADO")]
    public async Task<IActionResult> CambiarEstado(long id, CambiarEstadoOrdenPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.CambiarEstadoAsync(Empresa, sesion.UsuarioId, id, peticion, await PuedeVerCostosAsync(), ct));

    [HttpPut("{id:long}/tecnico")]
    [RequierePermiso("ORDENES.ASIGNAR")]
    public async Task<IActionResult> Asignar(long id, AsignarTecnicoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.AsignarTecnicoAsync(Empresa, sesion.UsuarioId, id, peticion, await PuedeVerCostosAsync(), ct));

    /// <summary>
    /// El tecnico toma una orden de la cola de su sede (se auto-asigna). Solo
    /// prospera si la orden esta sin asignar, es de su sede y no esta cerrada.
    /// </summary>
    [HttpPost("{id:long}/tomar")]
    [RequierePermiso("ORDENES.VER_ASIGNADAS")]
    public async Task<IActionResult> Tomar(long id, CancellationToken ct) =>
        Respuestas.De(await ordenes.TomarAsync(Empresa, sesion.UsuarioId, sesion.SedeId, id, await PuedeVerCostosAsync(), ct));

    [HttpPost("{id:long}/anular")]
    [RequierePermiso("ORDENES.ANULAR")]
    public async Task<IActionResult> Anular(long id, AnularOrdenPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.AnularAsync(Empresa, sesion.UsuarioId, id, peticion, await PuedeVerCostosAsync(), ct));

    // ----------------------------- Servicios -------------------------------

    [HttpPost("{id:long}/servicios")]
    [RequierePermiso("ORDENES.ESTADO")]
    public async Task<IActionResult> AgregarServicio(long id, AgregarServicioPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.AgregarServicioAsync(Empresa, id, peticion, await PuedeVerCostosAsync(), ct));

    [HttpDelete("{id:long}/servicios/{lineaId:long}")]
    [RequierePermiso("ORDENES.ESTADO")]
    public async Task<IActionResult> QuitarServicio(long id, long lineaId, CancellationToken ct) =>
        Respuestas.De(await ordenes.QuitarServicioAsync(Empresa, id, lineaId, await PuedeVerCostosAsync(), ct));

    // ----------------------- Consumo de repuestos --------------------------

    [HttpPost("{id:long}/consumos")]
    [RequierePermiso("ALMACEN.CONSUMIR")]
    public async Task<IActionResult> Consumir(long id, ConsumirRepuestoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.ConsumirRepuestoAsync(Empresa, sesion.UsuarioId, id, peticion, await PuedeVerCostosAsync(), ct));

    // ------------------------------ Checklist ------------------------------

    [HttpPut("{id:long}/checklist")]
    [RequierePermiso("ORDENES.CHECKLIST")]
    public async Task<IActionResult> GuardarChecklist(long id, GuardarChecklistPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.GuardarChecklistAsync(Empresa, id, peticion, await PuedeVerCostosAsync(), ct));

    // ------------------------------ Evidencias -----------------------------

    /// <summary>
    /// Sube una foto o documento. Se valida POR BYTES que el contenido sea lo
    /// que dice ser (no se confia en la extension ni en el mime declarado):
    /// una foto tiene que empezar con la firma de un JPG o un PNG.
    /// </summary>
    [HttpPost("{id:long}/evidencias")]
    [RequierePermiso("ORDENES.EVIDENCIA")]
    public async Task<IActionResult> SubirEvidencia(long id, SubirEvidenciaPeticion peticion, CancellationToken ct)
    {
        byte[] datos;
        try { datos = Convert.FromBase64String(peticion.ContenidoBase64); }
        catch (FormatException)
        {
            return Respuestas.De(TipoFallo.Validacion, "El contenido no es base64 valido.", "BASE64_INVALIDO");
        }

        if (datos.Length == 0)
            return Respuestas.De(TipoFallo.Validacion, "El archivo esta vacio.", "ARCHIVO_VACIO");

        const int tope = 10 * 1024 * 1024;   // 10 MB
        if (datos.Length > tope)
            return Respuestas.De(TipoFallo.Validacion, "El archivo supera los 10 MB.", "ARCHIVO_GRANDE");

        var mime = TipoPorFirma(datos);
        if (peticion.Tipo == "FOTO" && mime is not ("image/jpeg" or "image/png"))
            return Respuestas.De(TipoFallo.Validacion,
                "La foto tiene que ser un JPG o un PNG real.", "NO_ES_IMAGEN");
        if (mime is null)
            return Respuestas.De(TipoFallo.Validacion,
                "No se reconoce el tipo de archivo por su contenido.", "TIPO_DESCONOCIDO");

        var hash = Convert.ToHexString(SHA256.HashData(datos)).ToLowerInvariant();
        var ext = mime switch { "image/jpeg" => "jpg", "image/png" => "png", "application/pdf" => "pdf", _ => "bin" };

        var rutaRelativa = await evidenciaAlmacen.GuardarAsync(Empresa, id, hash, ext, datos, ct);

        return Respuestas.De(await ordenes.RegistrarEvidenciaAsync(
            Empresa, sesion.UsuarioId, id, peticion.Tipo, rutaRelativa,
            peticion.Nombre, mime, datos.Length, hash, ct));
    }

    [HttpGet("{id:long}/evidencias/{evidenciaId:long}")]
    [RequierePermiso("ORDENES.VER")]
    public async Task<IActionResult> VerEvidencia(long id, long evidenciaId, CancellationToken ct)
    {
        var r = await ordenes.EvidenciaArchivoAsync(Empresa, id, evidenciaId, ct);
        if (!r.Exito) return Respuestas.De(r.Fallo, r.Mensaje!, r.Codigo);

        var contenido = await evidenciaAlmacen.LeerAsync(r.Valor.Archivo, ct);
        if (contenido is null) return NotFound();
        return File(contenido, r.Valor.Mime);
    }

    // ----------------------- Plantilla de checklist ------------------------

    [HttpGet("checklist-plantilla")]
    [RequierePermiso("ORDENES.VER")]
    public async Task<IActionResult> Plantilla([FromQuery] bool soloActivos, CancellationToken ct) =>
        Ok(await ordenes.PlantillaAsync(Empresa, soloActivos, ct));

    [HttpPost("checklist-plantilla")]
    [RequierePermiso("CONFIGURACION.EDITAR")]
    public async Task<IActionResult> CrearItem(GuardarChecklistItemPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.GuardarPlantillaItemAsync(Empresa, null, peticion, ct));

    [HttpPut("checklist-plantilla/{itemId:long}")]
    [RequierePermiso("CONFIGURACION.EDITAR")]
    public async Task<IActionResult> EditarItem(long itemId, GuardarChecklistItemPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.GuardarPlantillaItemAsync(Empresa, itemId, peticion, ct));

    [HttpPut("checklist-plantilla/{itemId:long}/estado")]
    [RequierePermiso("CONFIGURACION.EDITAR")]
    public async Task<IActionResult> EstadoItem(long itemId, EstadoPeticion peticion, CancellationToken ct) =>
        Respuestas.De(await ordenes.CambiarEstadoPlantillaItemAsync(Empresa, itemId, peticion.Activo, ct));

    // -----------------------------------------------------------------------

    /// <summary>
    /// Reconoce el tipo por los primeros bytes (magic numbers). No se confia en
    /// la extension ni en el mime que mande el cliente.
    /// </summary>
    private static string? TipoPorFirma(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "image/jpeg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "image/png";
        if (b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D)
            return "application/pdf";
        return null;
    }
}
