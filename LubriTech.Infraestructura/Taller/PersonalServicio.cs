using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using LubriTech.Infraestructura.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// El personal de la empresa, visto por su propio Admin. Trabaja sobre la
/// tabla usuario, pero SOLO dentro de la empresa del que llama y SOLO sobre
/// roles de empresa (1..8): el rol 0 es del SuperAdmin y aqui no existe.
///
/// El cupo por rol del plan lo vigila la base (fn_limite_plan_usuario). Aqui
/// se comprueba antes para dar un mensaje legible en vez de un 409 generico.
/// </summary>
public class PersonalServicio(
    LubriTechContexto bd,
    IClaves claves,
    IBitacoraServicio bitacora,
    IValidadorSesion validador) : IPersonalServicio
{
    public async Task<Pagina<PersonalItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        int? rolId = null, int? sedeId = null, bool? activo = null, CancellationToken ct = default)
    {
        // El filtro de empresa es opcional en usuario (el SuperAdmin no tiene
        // empresa), asi que se acota a mano a la empresa que llama.
        var q = bd.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.EmpresaId == empresaId && u.RolId > Roles.SuperAdmin);

        if (rolId.HasValue) q = q.Where(u => u.RolId == rolId.Value);
        if (sedeId.HasValue) q = q.Where(u => u.SedeId == sedeId.Value);
        if (activo.HasValue) q = q.Where(u => u.Activo == activo.Value);

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var b = consulta.Buscar.Trim().ToLower();
            q = q.Where(u => u.Nombres.ToLower().Contains(b) ||
                             u.Apellidos.ToLower().Contains(b) ||
                             u.Email.ToLower().Contains(b) ||
                             (u.Dni != null && u.Dni.Contains(b)));
        }

        var total = await q.CountAsync(ct);
        var filas = await q
            .OrderBy(u => u.Apellidos).ThenBy(u => u.Nombres)
            .Skip(consulta.Saltar).Take(consulta.Tamano)
            .Select(u => new
            {
                U = u,
                Rol = u.Rol!.Nombre,
                Sede = u.Sede != null ? u.Sede.Nombre : null
            })
            .ToListAsync(ct);

        var items = filas.Select(f => Item(f.U, f.Rol, f.Sede)).ToList();
        return Pagina<PersonalItem>.De(items, consulta, total);
    }

    public async Task<Resultado<PersonalItem>> VerAsync(int empresaId, int usuarioId, CancellationToken ct = default)
    {
        var u = await Cargar(empresaId, usuarioId, ct);
        return u is null
            ? Resultado<PersonalItem>.Error(TipoFallo.NoEncontrado, "La persona no existe.", "NO_ENCONTRADO")
            : Resultado<PersonalItem>.Ok(Item(u, u.Rol?.Nombre ?? "", u.Sede?.Nombre));
    }

    public async Task<Resultado<PersonalCreado>> CrearAsync(
        int empresaId, GuardarPersonalPeticion peticion, CancellationToken ct = default)
    {
        var falla = await ValidarRolYSedeAsync(empresaId, peticion.RolId, peticion.SedeId, ct);
        if (falla is not null)
            return Resultado<PersonalCreado>.Error(falla.Value.Tipo, falla.Value.Mensaje, falla.Value.Codigo);

        var email = peticion.Email.Trim().ToLower();
        if (await bd.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == email, ct))
            return Resultado<PersonalCreado>.Error(TipoFallo.Conflicto,
                "Ese correo ya esta en uso por otra cuenta del sistema.", "EMAIL_DUPLICADO");

        var cupo = await ChequeoDeCupoAsync(empresaId, peticion.RolId, ct);
        if (cupo is not null)
            return Resultado<PersonalCreado>.Error(TipoFallo.Conflicto, cupo, "TOPE_DE_ROL");

        var clave = claves.NuevaClaveTemporal();
        var usuario = new Usuario
        {
            EmpresaId = empresaId,
            SedeId = peticion.SedeId,
            RolId = peticion.RolId,
            Dni = Limpiar(peticion.Dni),
            Nombres = peticion.Nombres.Trim(),
            Apellidos = peticion.Apellidos.Trim(),
            Email = email,
            Telefono = Limpiar(peticion.Telefono),
            PasswordHash = claves.Hash(clave),
            DebeCambiarClave = true,
            Activo = true
        };

        bd.Usuarios.Add(usuario);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("PERSONAL.ALTA", "usuario", usuario.Id.ToString(),
            new { usuario.Email, usuario.RolId }, ct);

        var item = Item(usuario, await NombreRolAsync(peticion.RolId, ct), await NombreSedeAsync(peticion.SedeId, ct));
        return Resultado<PersonalCreado>.Ok(new PersonalCreado { Personal = item, ClaveTemporal = clave });
    }

    public async Task<Resultado<PersonalItem>> ActualizarAsync(
        int empresaId, int usuarioId, ActualizarPersonalPeticion peticion, CancellationToken ct = default)
    {
        var usuario = await Cargar(empresaId, usuarioId, ct);
        if (usuario is null)
            return Resultado<PersonalItem>.Error(TipoFallo.NoEncontrado, "La persona no existe.", "NO_ENCONTRADO");

        var falla = await ValidarRolYSedeAsync(empresaId, peticion.RolId, peticion.SedeId, ct);
        if (falla is not null)
            return Resultado<PersonalItem>.Error(falla.Value.Tipo, falla.Value.Mensaje, falla.Value.Codigo);

        // Si el cambio de rol lo mete en un rol que ya esta al tope, parar.
        if (peticion.RolId != usuario.RolId)
        {
            var cupo = await ChequeoDeCupoAsync(empresaId, peticion.RolId, ct);
            if (cupo is not null)
                return Resultado<PersonalItem>.Error(TipoFallo.Conflicto, cupo, "TOPE_DE_ROL");
        }

        usuario.Dni = Limpiar(peticion.Dni);
        usuario.Nombres = peticion.Nombres.Trim();
        usuario.Apellidos = peticion.Apellidos.Trim();
        usuario.Telefono = Limpiar(peticion.Telefono);
        usuario.RolId = peticion.RolId;
        usuario.SedeId = peticion.SedeId;

        await bd.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync("PERSONAL.EDICION", "usuario", usuarioId.ToString(),
            new { usuario.Email, usuario.RolId }, ct);

        return Resultado<PersonalItem>.Ok(Item(usuario,
            await NombreRolAsync(peticion.RolId, ct), await NombreSedeAsync(peticion.SedeId, ct)));
    }

    public async Task<Resultado<PersonalItem>> CambiarEstadoAsync(
        int empresaId, int usuarioId, EstadoPeticion peticion, CancellationToken ct = default)
    {
        var usuario = await Cargar(empresaId, usuarioId, ct);
        if (usuario is null)
            return Resultado<PersonalItem>.Error(TipoFallo.NoEncontrado, "La persona no existe.", "NO_ENCONTRADO");

        // No dejarse sin Admin: el ultimo Admin activo no se desactiva a si
        // mismo ni a otro. Una empresa sin Admin no se puede administrar.
        if (!peticion.Activo && usuario.RolId == Roles.Admin && usuario.Activo)
        {
            var otros = await bd.Usuarios.IgnoreQueryFilters()
                .CountAsync(u => u.EmpresaId == empresaId && u.RolId == Roles.Admin && u.Activo && u.Id != usuarioId, ct);
            if (otros == 0)
                return Resultado<PersonalItem>.Error(TipoFallo.Conflicto,
                    "No se puede desactivar al unico Admin activo de la empresa.", "ULTIMO_ADMIN");
        }

        usuario.Activo = peticion.Activo;

        // Desactivar corta el acceso al instante.
        if (!peticion.Activo)
        {
            usuario.SesionVersion++;
            await bd.UsuarioRefrescos.IgnoreQueryFilters()
                .Where(r => r.UsuarioId == usuarioId && r.RevocadoEn == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, Tiempo.Ahora), ct);
        }

        await bd.SaveChangesAsync(ct);
        validador.Invalidar(usuarioId: usuarioId);

        await bitacora.RegistrarAsync("PERSONAL.ESTADO", "usuario", usuarioId.ToString(),
            new { usuario.Activo }, ct);

        return Resultado<PersonalItem>.Ok(Item(usuario, usuario.Rol?.Nombre ?? "", usuario.Sede?.Nombre));
    }

    public async Task<Resultado<string>> ReiniciarClaveAsync(
        int empresaId, int usuarioId, CancellationToken ct = default)
    {
        var usuario = await Cargar(empresaId, usuarioId, ct);
        if (usuario is null)
            return Resultado<string>.Error(TipoFallo.NoEncontrado, "La persona no existe.", "NO_ENCONTRADO");

        var clave = claves.NuevaClaveTemporal();
        usuario.PasswordHash = claves.Hash(clave);
        usuario.DebeCambiarClave = true;
        usuario.IntentosFallidos = 0;
        usuario.BloqueadoHasta = null;
        usuario.SesionVersion++;

        await bd.UsuarioRefrescos.IgnoreQueryFilters()
            .Where(r => r.UsuarioId == usuarioId && r.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, Tiempo.Ahora), ct);

        await bd.SaveChangesAsync(ct);
        validador.Invalidar(usuarioId: usuarioId);

        await bitacora.RegistrarAsync("PERSONAL.CLAVE", "usuario", usuarioId.ToString(),
            new { usuario.Email }, ct);

        return Resultado<string>.Ok(clave);
    }

    // ----------------------------------------------------------------------

    private Task<Usuario?> Cargar(int empresaId, int usuarioId, CancellationToken ct) =>
        bd.Usuarios.IgnoreQueryFilters().Include(u => u.Rol).Include(u => u.Sede)
          .FirstOrDefaultAsync(u => u.Id == usuarioId && u.EmpresaId == empresaId && u.RolId > Roles.SuperAdmin, ct);

    private async Task<(TipoFallo Tipo, string Mensaje, string Codigo)?> ValidarRolYSedeAsync(
        int empresaId, int rolId, int? sedeId, CancellationToken ct)
    {
        if (rolId <= Roles.SuperAdmin || rolId > Roles.Almacenero)
            return (TipoFallo.Validacion, "El rol indicado no es un rol de empresa.", "ROL_INVALIDO");

        if (sedeId.HasValue &&
            !await bd.Sedes.AnyAsync(s => s.Id == sedeId.Value && s.EmpresaId == empresaId, ct))
            return (TipoFallo.Validacion, "La sede indicada no es de esta empresa.", "SEDE_INVALIDA");

        return null;
    }

    /// <summary>Devuelve el mensaje si el rol esta lleno; null si hay cupo.</summary>
    private async Task<string?> ChequeoDeCupoAsync(int empresaId, int rolId, CancellationToken ct)
    {
        var planId = await bd.Empresas.IgnoreQueryFilters()
            .Where(e => e.Id == empresaId).Select(e => e.PlanId).FirstAsync(ct);

        var cupo = await bd.PlanRolCupos.AsNoTracking()
            .Where(c => c.PlanId == planId && c.RolId == rolId)
            .Select(c => (int?)c.Cupo).FirstOrDefaultAsync(ct);

        if (cupo is null)
            return "El plan contratado no habilita ese rol.";

        if (cupo == 0) return null;   // sin tope

        var usados = await bd.Usuarios.IgnoreQueryFilters()
            .CountAsync(u => u.EmpresaId == empresaId && u.RolId == rolId && u.Activo, ct);

        return usados >= cupo
            ? $"El plan permite {cupo} de ese rol y ya hay {usados} activo(s)."
            : null;
    }

    private Task<string> NombreRolAsync(int rolId, CancellationToken ct) =>
        bd.Roles.AsNoTracking().Where(r => r.Id == rolId).Select(r => r.Nombre).FirstAsync(ct);

    private async Task<string?> NombreSedeAsync(int? sedeId, CancellationToken ct) =>
        sedeId is null ? null
        : await bd.Sedes.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.Id == sedeId.Value).Select(s => s.Nombre).FirstOrDefaultAsync(ct);

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static PersonalItem Item(Usuario u, string rol, string? sede) => new()
    {
        Id = u.Id,
        Dni = u.Dni,
        Nombre = u.NombreCompleto,
        Email = u.Email,
        Telefono = u.Telefono,
        RolId = u.RolId,
        Rol = rol,
        SedeId = u.SedeId,
        Sede = sede,
        Activo = u.Activo,
        DebeCambiarClave = u.DebeCambiarClave,
        UltimoAcceso = u.UltimoAcceso,
        CreadoEn = u.CreadoEn
    };
}
