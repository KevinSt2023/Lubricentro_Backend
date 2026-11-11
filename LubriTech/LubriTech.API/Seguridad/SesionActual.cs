using System.Security.Claims;
using LubriTech.Aplicacion.Comun;
using LubriTech.Dominio.Comun;
using LubriTech.Infraestructura.Seguridad;

namespace LubriTech.API.Seguridad;

/// <summary>
/// Traduce el token de la peticion en curso a algo que el resto del sistema
/// pueda usar. No decide nada: solo lee.
/// </summary>
public class SesionActual(IHttpContextAccessor acceso) : ISesionActual
{
    private ClaimsPrincipal? Usuario => acceso.HttpContext?.User;

    public bool Autenticado => Usuario?.Identity?.IsAuthenticated == true;

    public string? Audiencia => Leer("aud");

    public bool EsCliente => Audiencia == Audiencias.Cliente;

    public bool EsSuperAdmin => !EsCliente && RolId == Roles.SuperAdmin;

    /// <summary>-1 cuando no hay empresa en el token. El filtro queda cerrado.</summary>
    public int EmpresaId => LeerEntero(Claims.Empresa) ?? -1;

    public int? UsuarioId => EsCliente ? null : LeerEntero("sub");

    public long? AccesoId => EsCliente ? LeerLargo(Claims.Acceso) : null;

    public long? ClienteId => EsCliente ? LeerLargo(Claims.Cliente) : null;

    public int? SedeId => LeerEntero(Claims.Sede);

    public int? RolId => EsCliente ? null : LeerEntero(Claims.Rol);

    public int SesionVersion => LeerEntero(Claims.SesionVersion) ?? 0;

    public string? Ip => acceso.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? Agente
    {
        get
        {
            var valor = acceso.HttpContext?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrWhiteSpace(valor) ? null : valor;
        }
    }

    /// <summary>
    /// De donde vino. Lo declara la app en la cabecera X-App; si no viene,
    /// se asume el ERP web. Es para la bitacora, no para autorizar nada.
    /// </summary>
    public string Origen
    {
        get
        {
            var declarado = acceso.HttpContext?.Request.Headers["X-App"].ToString();

            return declarado switch
            {
                "OPERACIONES" => Origenes.Operaciones,
                "CLIENTES" => Origenes.Clientes,
                "SISTEMA" => Origenes.Sistema,
                _ => EsCliente ? Origenes.Clientes : Origenes.ErpWeb
            };
        }
    }

    private string? Leer(string tipo) => Usuario?.FindFirst(tipo)?.Value;

    private int? LeerEntero(string tipo) =>
        int.TryParse(Leer(tipo), out var valor) ? valor : null;

    private long? LeerLargo(string tipo) =>
        long.TryParse(Leer(tipo), out var valor) ? valor : null;
}

/// <summary>
/// Lo unico que el contexto de EF necesita, y a proposito SINGLETON: el
/// filtro global de EF se compila una sola vez con una referencia a este
/// objeto dentro. Al leer el HttpContext de la peticion en curso en cada
/// llamada, la referencia fija da siempre el valor correcto.
///
/// Si esto fuera por peticion, el modelo cacheado quedaria pegado a la
/// primera peticion del proceso y el filtro mostraria datos de otra empresa.
/// Es el tipo de error que no se ve en una prueba manual y se ve en el
/// primer cliente.
/// </summary>
public class ProveedorEmpresaActual(IHttpContextAccessor acceso) : IProveedorEmpresaActual
{
    public int EmpresaId
    {
        get
        {
            var valor = acceso.HttpContext?.User.FindFirst(Claims.Empresa)?.Value;
            return int.TryParse(valor, out var empresa) ? empresa : -1;
        }
    }
}
