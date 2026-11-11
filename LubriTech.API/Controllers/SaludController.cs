using LubriTech.API.Seguridad;
using LubriTech.Aplicacion.Comun;
using LubriTech.Dominio.Comun;
using LubriTech.Infraestructura.Datos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.API.Controllers;

[ApiController]
[Route("api/salud")]
public class SaludController(LubriTechContexto bd, ISesionActual sesion) : ControllerBase
{
    /// <summary>Latido. Sirve para saber si la API esta arriba y si llega a la base.</summary>
    [AllowAnonymous]
    [HttpGet]
    [HttpHead]
    public async Task<IActionResult> Latido(CancellationToken ct)
    {
        // El latido NUNCA lanza: es justo lo que se consulta cuando algo anda
        // mal, y un latido que explota no sirve para diagnosticar nada.
        // Tampoco se queda esperando: 5 segundos y contesta lo que sepa.
        string baseDeDatos;
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(5));

            baseDeDatos = await bd.Database.CanConnectAsync(limite.Token)
                ? "arriba"
                : "sin conexion";
        }
        catch (OperationCanceledException)
        {
            baseDeDatos = "sin respuesta";
        }
        catch (Exception)
        {
            baseDeDatos = "sin conexion";
        }

        return Ok(new
        {
            servicio = "LubriTech API",
            version = typeof(SaludController).Assembly.GetName().Version?.ToString(),
            baseDeDatos,
            utc = Tiempo.Ahora,
            hoyLima = Tiempo.HoyLima()
        });
    }

    /// <summary>
    /// Prueba el filtro de empresa contra la base, no contra la teoria.
    ///
    /// Devuelve, para la sesion en curso, cuantas filas VE con el filtro
    /// puesto y cuantas hay en total. Para un Admin de una empresa las
    /// primeras tienen que ser solo las suyas; el total solo se usa para
    /// comprobar que el filtro realmente esta recortando algo.
    ///
    /// Es la prueba del criterio 11, y se deja en el codigo porque el arnes
    /// de QA la llama en cada barrido.
    /// </summary>
    [Authorize]
    [HttpGet("aislamiento")]
    public async Task<IActionResult> Aislamiento(CancellationToken ct)
    {
        var sedesVisibles = await bd.Sedes.CountAsync(ct);
        var clientesVisibles = await bd.Clientes.CountAsync(ct);

        var sedesTotales = await bd.Sedes.IgnoreQueryFilters().CountAsync(ct);
        var clientesTotales = await bd.Clientes.IgnoreQueryFilters().CountAsync(ct);

        var empresasDeLasSedesVisibles = await bd.Sedes
            .Select(s => s.EmpresaId)
            .Distinct()
            .ToListAsync(ct);

        return Ok(new
        {
            empresaDeLaSesion = sesion.EmpresaId,
            esCliente = sesion.EsCliente,
            esSuperAdmin = sesion.EsSuperAdmin,
            sedesVisibles,
            sedesTotales,
            clientesVisibles,
            clientesTotales,
            // Si esto trae mas de una empresa, el filtro esta roto.
            empresasDeLasSedesVisibles
        });
    }

    /// <summary>
    /// Endpoint del ERP, con permiso de verdad. Existe para que el arnes
    /// pueda comprobar dos cosas sin esperar a la Fase 2:
    ///   * un token del portal del cliente rebota aqui (403, por audiencia),
    ///   * un rol sin el permiso rebota aqui (403), y con el permiso pasa.
    /// </summary>
    [Authorize]
    [RequierePermiso("CLIENTES.VER")]
    [HttpGet("solo-erp")]
    public IActionResult SoloErp() => Ok(new
    {
        permiso = "CLIENTES.VER",
        rolId = sesion.RolId,
        empresaId = sesion.EmpresaId
    });

    /// <summary>El espejo: endpoint del portal. Un token del ERP rebota aqui.</summary>
    [Authorize]
    [SoloCliente]
    [HttpGet("solo-cliente")]
    public IActionResult SoloCliente() => Ok(new
    {
        clienteId = sesion.ClienteId,
        empresaId = sesion.EmpresaId
    });

    /// <summary>Endpoint de la consola del SaaS. Solo el rol 0.</summary>
    [Authorize]
    [SoloSuperAdmin]
    [HttpGet("solo-superadmin")]
    public IActionResult SoloSuperAdmin() => Ok(new { esSuperAdmin = sesion.EsSuperAdmin });
}
