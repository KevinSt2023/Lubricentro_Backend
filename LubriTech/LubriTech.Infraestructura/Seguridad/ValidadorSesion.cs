using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LubriTech.Infraestructura.Seguridad;

/// <summary>
/// Un token firmado no alcanza. En cada peticion se comprueba que la sesion
/// SIGA valiendo:
///
///   * el usuario (o el acceso del cliente) existe y esta activo,
///   * su sesion_version es la misma que viaja en el token
///     — asi el Admin cierra sesiones a distancia al instante,
///   * la empresa esta activa y con el servicio al dia
///     — vencido, se corta aunque el token no haya expirado.
///
/// Con cache de 30 segundos para no consultar la base en cada llamada, y
/// con invalidacion inmediata cuando algo de eso cambia.
/// </summary>
public class ValidadorSesion(LubriTechContexto bd, IMemoryCache cache) : IValidadorSesion
{
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(30);

    private sealed record EstadoPersonal(bool Activo, int SesionVersion, int? EmpresaId);
    private sealed record EstadoCliente(bool Activo, int SesionVersion, int EmpresaId);
    private sealed record EstadoEmpresa(bool Activa, DateOnly ServicioHasta);

    private static string ClaveUsuario(int id) => $"ses:u:{id}";
    private static string ClaveAcceso(long id) => $"ses:c:{id}";
    private static string ClaveEmpresa(int id) => $"ses:e:{id}";

    public async Task<Resultado> ValidarAsync(ISesionActual sesion, CancellationToken ct = default)
    {
        if (!sesion.Autenticado) return Resultado.Ok();   // endpoint publico

        return sesion.EsCliente
            ? await ValidarClienteAsync(sesion, ct)
            : await ValidarPersonalAsync(sesion, ct);
    }

    private async Task<Resultado> ValidarPersonalAsync(ISesionActual sesion, CancellationToken ct)
    {
        if (sesion.UsuarioId is null)
            return Resultado.Error(TipoFallo.NoAutenticado, "Sesion sin usuario.", "SESION_INVALIDA");

        var id = sesion.UsuarioId.Value;

        if (!cache.TryGetValue(ClaveUsuario(id), out EstadoPersonal? estado) || estado is null)
        {
            estado = await bd.Usuarios
                .IgnoreQueryFilters()
                .Where(u => u.Id == id)
                .Select(u => new EstadoPersonal(u.Activo, u.SesionVersion, u.EmpresaId))
                .FirstOrDefaultAsync(ct);

            if (estado is null)
                return Resultado.Error(TipoFallo.NoAutenticado, "La cuenta no existe.", "SESION_INVALIDA");

            cache.Set(ClaveUsuario(id), estado, Vida);
        }

        if (!estado.Activo)
            return Resultado.Error(TipoFallo.Prohibido, "La cuenta esta desactivada.", "CUENTA_INACTIVA");

        if (estado.SesionVersion != sesion.SesionVersion)
            return Resultado.Error(TipoFallo.NoAutenticado,
                "La sesion fue cerrada. Vuelve a entrar.", "SESION_CERRADA");

        // El SuperAdmin no tiene empresa: nada mas que comprobar.
        if (sesion.RolId == Roles.SuperAdmin || estado.EmpresaId is null) return Resultado.Ok();

        return await ValidarEmpresaAsync(estado.EmpresaId.Value, ct);
    }

    private async Task<Resultado> ValidarClienteAsync(ISesionActual sesion, CancellationToken ct)
    {
        if (sesion.AccesoId is null)
            return Resultado.Error(TipoFallo.NoAutenticado, "Sesion sin cuenta.", "SESION_INVALIDA");

        var id = sesion.AccesoId.Value;

        if (!cache.TryGetValue(ClaveAcceso(id), out EstadoCliente? estado) || estado is null)
        {
            estado = await bd.ClienteAccesos
                .IgnoreQueryFilters()
                .Where(a => a.Id == id)
                .Select(a => new EstadoCliente(
                    a.Activo && a.Cliente!.Activo, a.SesionVersion, a.Cliente!.EmpresaId))
                .FirstOrDefaultAsync(ct);

            if (estado is null)
                return Resultado.Error(TipoFallo.NoAutenticado, "La cuenta no existe.", "SESION_INVALIDA");

            cache.Set(ClaveAcceso(id), estado, Vida);
        }

        if (!estado.Activo)
            return Resultado.Error(TipoFallo.Prohibido, "La cuenta esta desactivada.", "CUENTA_INACTIVA");

        if (estado.SesionVersion != sesion.SesionVersion)
            return Resultado.Error(TipoFallo.NoAutenticado,
                "La sesion fue cerrada. Vuelve a entrar.", "SESION_CERRADA");

        return await ValidarEmpresaAsync(estado.EmpresaId, ct);
    }

    private async Task<Resultado> ValidarEmpresaAsync(int empresaId, CancellationToken ct)
    {
        if (!cache.TryGetValue(ClaveEmpresa(empresaId), out EstadoEmpresa? empresa) || empresa is null)
        {
            empresa = await bd.Empresas
                .IgnoreQueryFilters()
                .Where(e => e.Id == empresaId)
                .Select(e => new EstadoEmpresa(e.Activo, e.ServicioHasta))
                .FirstOrDefaultAsync(ct);

            if (empresa is null)
                return Resultado.Error(TipoFallo.Prohibido, "La empresa no existe.", "EMPRESA_INVALIDA");

            cache.Set(ClaveEmpresa(empresaId), empresa, Vida);
        }

        if (!empresa.Activa)
            return Resultado.Error(TipoFallo.Prohibido,
                "La empresa esta desactivada.", "EMPRESA_INACTIVA");

        if (empresa.ServicioHasta < Tiempo.HoyLima())
            return Resultado.Error(TipoFallo.Prohibido,
                "El servicio esta vencido. Renueva para seguir usando el sistema.",
                "SERVICIO_VENCIDO");

        return Resultado.Ok();
    }

    public void Invalidar(int? usuarioId = null, long? accesoId = null, int? empresaId = null)
    {
        if (usuarioId.HasValue) cache.Remove(ClaveUsuario(usuarioId.Value));
        if (accesoId.HasValue) cache.Remove(ClaveAcceso(accesoId.Value));
        if (empresaId.HasValue) cache.Remove(ClaveEmpresa(empresaId.Value));
    }
}
