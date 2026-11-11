using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LubriTech.API.Seguridad;

/// <summary>
/// Autorizacion de verdad: se comprueba aqui, en el servidor, en cada
/// llamada. Lo que la app muestre u oculte es cortesia visual, no seguridad.
///
/// El SuperAdmin pasa sin matriz: no opera empresas, las administra, y los
/// endpoints de la consola se marcan con <see cref="SoloSuperAdminAttribute"/>.
/// Un token del portal del cliente NUNCA pasa por aqui: lo detiene antes la
/// audiencia del token.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequierePermisoAttribute(string permiso) : Attribute, IAsyncAuthorizationFilter
{
    public string Permiso { get; } = permiso;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexto)
    {
        var servicios = contexto.HttpContext.RequestServices;
        var sesion = servicios.GetRequiredService<ISesionActual>();

        if (!sesion.Autenticado)
        {
            contexto.Result = Respuestas.De(TipoFallo.NoAutenticado,
                "Necesitas iniciar sesion.", "SESION_INVALIDA");
            return;
        }

        if (sesion.EsCliente)
        {
            contexto.Result = Respuestas.De(TipoFallo.Prohibido,
                "Esta operacion no esta disponible para cuentas de cliente.", "AUDIENCIA");
            return;
        }

        if (sesion.EsSuperAdmin) return;

        if (sesion.EmpresaId <= 0 || sesion.RolId is null)
        {
            contexto.Result = Respuestas.De(TipoFallo.Prohibido,
                "La sesion no tiene empresa resuelta.", "SIN_EMPRESA");
            return;
        }

        var permisos = servicios.GetRequiredService<IPermisoServicio>();

        if (!await permisos.TienePermisoAsync(sesion.EmpresaId, sesion.RolId.Value, Permiso))
        {
            contexto.Result = Respuestas.De(TipoFallo.Prohibido,
                "No tienes permiso para esta operacion.", "SIN_PERMISO");
        }
    }
}

/// <summary>Consola del SaaS. Solo el rol 0.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SoloSuperAdminAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext contexto)
    {
        var sesion = contexto.HttpContext.RequestServices.GetRequiredService<ISesionActual>();

        if (!sesion.Autenticado)
        {
            contexto.Result = Respuestas.De(TipoFallo.NoAutenticado,
                "Necesitas iniciar sesion.", "SESION_INVALIDA");
            return;
        }

        if (!sesion.EsSuperAdmin)
        {
            contexto.Result = Respuestas.De(TipoFallo.Prohibido,
                "Solo la consola del SuperAdmin puede hacer esto.", "SOLO_SUPERADMIN");
        }
    }
}

/// <summary>
/// Solo el portal del cliente. El espejo del filtro de arriba: un token del
/// ERP no entra a los endpoints del cliente.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class SoloClienteAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext contexto)
    {
        var sesion = contexto.HttpContext.RequestServices.GetRequiredService<ISesionActual>();

        if (!sesion.Autenticado)
        {
            contexto.Result = Respuestas.De(TipoFallo.NoAutenticado,
                "Necesitas iniciar sesion.", "SESION_INVALIDA");
            return;
        }

        if (!sesion.EsCliente)
        {
            contexto.Result = Respuestas.De(TipoFallo.Prohibido,
                "Esta operacion es solo del portal del cliente.", "AUDIENCIA");
        }
    }
}

/// <summary>
/// Un token firmado no basta: en cada peticion autenticada se comprueba que
/// la sesion siga valiendo (cuenta activa, sesion no cerrada a distancia,
/// empresa al dia). Va como filtro global.
/// </summary>
public class ValidacionDeSesionFiltro : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext contexto)
    {
        var servicios = contexto.HttpContext.RequestServices;
        var sesion = servicios.GetRequiredService<ISesionActual>();

        if (!sesion.Autenticado) return;

        var validador = servicios.GetRequiredService<IValidadorSesion>();
        var resultado = await validador.ValidarAsync(sesion);

        if (!resultado.Exito)
            contexto.Result = Respuestas.De(resultado.Fallo, resultado.Mensaje!, resultado.Codigo);
    }
}

/// <summary>
/// Un solo lugar que convierte un fallo de negocio en respuesta HTTP, para
/// que nada se escape como 500 y el formato del error sea siempre el mismo.
/// </summary>
public static class Respuestas
{
    public static IActionResult De(TipoFallo fallo, string mensaje, string? codigo = null)
    {
        var cuerpo = new { mensaje, codigo };
        return new ObjectResult(cuerpo) { StatusCode = (int)fallo };
    }

    public static IActionResult De(Resultado resultado) =>
        resultado.Exito
            ? new OkResult()
            : De(resultado.Fallo, resultado.Mensaje ?? "Operacion rechazada.", resultado.Codigo);

    public static IActionResult De<T>(Resultado<T> resultado) =>
        resultado.Exito
            ? new OkObjectResult(resultado.Valor)
            : De(resultado.Fallo, resultado.Mensaje ?? "Operacion rechazada.", resultado.Codigo);
}
