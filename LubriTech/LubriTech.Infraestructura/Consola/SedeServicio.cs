using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Consola;

/// <summary>
/// Sedes. Lo usan dos publicos con la MISMA logica: el SuperAdmin sobre
/// cualquier empresa y el Admin sobre la suya.
///
/// Este servicio recibe siempre el empresaId y trabaja sobre esa empresa.
/// Quien tiene derecho a pasar ese id lo deciden los atributos del
/// controlador: el del SuperAdmin lo toma de la ruta, y el del Admin lo toma
/// de SU PROPIO token, nunca de lo que el cliente mande. Asi la misma logica
/// sirve a los dos sin que el Admin pueda tocar sedes ajenas.
/// </summary>
public class SedeServicio(
    LubriTechContexto bd,
    IBitacoraServicio bitacora) : ISedeServicio
{
    public async Task<Resultado<List<SedeItem>>> ListarAsync(
        int empresaId, CancellationToken ct = default)
    {
        if (!await ExisteEmpresaAsync(empresaId, ct))
            return Resultado<List<SedeItem>>.Error(TipoFallo.NoEncontrado,
                "La empresa no existe.", "NO_ENCONTRADO");

        var sedes = await Consultar(empresaId)
            .OrderByDescending(s => s.EsPrincipal).ThenBy(s => s.Nombre)
            .ToListAsync(ct);

        return Resultado<List<SedeItem>>.Ok(sedes);
    }

    private IQueryable<SedeItem> Consultar(int empresaId) =>
        bd.Sedes
          .IgnoreQueryFilters()
          .AsNoTracking()
          .Where(s => s.EmpresaId == empresaId)
          .Select(s => new SedeItem
          {
              Id = s.Id,
              EmpresaId = s.EmpresaId,
              Nombre = s.Nombre,
              Direccion = s.Direccion,
              Telefono = s.Telefono,
              EsPrincipal = s.EsPrincipal,
              Activo = s.Activo,
              Usuarios = bd.Usuarios.IgnoreQueryFilters().Count(u => u.SedeId == s.Id && u.Activo)
          });

    public async Task<Resultado<SedeItem>> CrearAsync(
        int empresaId, GuardarSedePeticion peticion, CancellationToken ct = default)
    {
        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .Include(e => e.Plan)
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

        if (empresa is null)
            return Resultado<SedeItem>.Error(TipoFallo.NoEncontrado, "La empresa no existe.", "NO_ENCONTRADO");

        var nombre = peticion.Nombre.Trim();

        if (await bd.Sedes.IgnoreQueryFilters()
                .AnyAsync(s => s.EmpresaId == empresaId && s.Nombre == nombre, ct))
            return Resultado<SedeItem>.Error(TipoFallo.Conflicto,
                "Ya hay una sede con ese nombre en esta empresa.", "NOMBRE_DUPLICADO");

        // El tope del plan tambien lo vigila la base (fn_limite_plan_sede).
        // Comprobarlo aqui es para dar un mensaje claro en vez de un 409
        // generico desde el candado.
        var max = empresa.Plan?.MaxSedes ?? 0;
        if (max > 0)
        {
            var activas = await bd.Sedes
                .IgnoreQueryFilters().CountAsync(s => s.EmpresaId == empresaId && s.Activo, ct);

            if (peticion.Activo && activas >= max)
                return Resultado<SedeItem>.Error(TipoFallo.Conflicto,
                    $"El plan {empresa.Plan?.Nombre} permite {max} sede(s) activa(s) y ya hay {activas}.",
                    "TOPE_DE_SEDES");
        }

        // La primera sede de una empresa es la principal, sin preguntar:
        // una empresa sin sede principal es una empresa a medio crear.
        var esLaPrimera = !await bd.Sedes.IgnoreQueryFilters().AnyAsync(s => s.EmpresaId == empresaId, ct);

        var sede = new Sede
        {
            EmpresaId = empresaId,
            Nombre = nombre,
            Direccion = peticion.Direccion?.Trim(),
            Telefono = peticion.Telefono?.Trim(),
            EsPrincipal = esLaPrimera,
            Activo = peticion.Activo
        };

        bd.Sedes.Add(sede);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("SEDE.ALTA", "sede", sede.Id.ToString(),
            new { empresaId, sede.Nombre }, ct);

        return Resultado<SedeItem>.Ok((await Consultar(empresaId).FirstAsync(s => s.Id == sede.Id, ct)));
    }

    public async Task<Resultado<SedeItem>> ActualizarAsync(
        int empresaId, int sedeId, GuardarSedePeticion peticion, CancellationToken ct = default)
    {
        var sede = await bd.Sedes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sedeId && s.EmpresaId == empresaId, ct);

        if (sede is null)
            return Resultado<SedeItem>.Error(TipoFallo.NoEncontrado, "La sede no existe.", "NO_ENCONTRADO");

        var nombre = peticion.Nombre.Trim();

        if (await bd.Sedes.IgnoreQueryFilters()
                .AnyAsync(s => s.EmpresaId == empresaId && s.Nombre == nombre && s.Id != sedeId, ct))
            return Resultado<SedeItem>.Error(TipoFallo.Conflicto,
                "Ya hay otra sede con ese nombre en esta empresa.", "NOMBRE_DUPLICADO");

        // La sede principal no se desactiva: dejaria a la empresa sin la
        // sede de la que cuelgan sus usuarios. Primero se nombra otra.
        if (sede.EsPrincipal && !peticion.Activo)
            return Resultado<SedeItem>.Error(TipoFallo.Conflicto,
                "No se puede desactivar la sede principal: marca otra como principal primero.",
                "SEDE_PRINCIPAL");

        if (sede.Activo && !peticion.Activo)
        {
            var conGente = await bd.Usuarios
                .IgnoreQueryFilters().CountAsync(u => u.SedeId == sedeId && u.Activo, ct);

            if (conGente > 0)
                return Resultado<SedeItem>.Error(TipoFallo.Conflicto,
                    $"Esa sede tiene {conGente} persona(s) asignada(s): muevelas antes de desactivarla.",
                    "SEDE_CON_PERSONAL");
        }

        sede.Nombre = nombre;
        sede.Direccion = peticion.Direccion?.Trim();
        sede.Telefono = peticion.Telefono?.Trim();
        sede.Activo = peticion.Activo;

        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("SEDE.EDICION", "sede", sedeId.ToString(),
            new { empresaId, sede.Nombre, sede.Activo }, ct);

        return Resultado<SedeItem>.Ok(await Consultar(empresaId).FirstAsync(s => s.Id == sedeId, ct));
    }

    public async Task<Resultado<SedeItem>> MarcarPrincipalAsync(
        int empresaId, int sedeId, CancellationToken ct = default)
    {
        var sede = await bd.Sedes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == sedeId && s.EmpresaId == empresaId, ct);

        if (sede is null)
            return Resultado<SedeItem>.Error(TipoFallo.NoEncontrado, "La sede no existe.", "NO_ENCONTRADO");

        if (!sede.Activo)
            return Resultado<SedeItem>.Error(TipoFallo.Conflicto,
                "Una sede desactivada no puede ser la principal.", "SEDE_INACTIVA");

        if (sede.EsPrincipal)
            return Resultado<SedeItem>.Ok(await Consultar(empresaId).FirstAsync(s => s.Id == sedeId, ct));

        // Hay un indice unico parcial de una sola principal por empresa, asi
        // que primero se baja la anterior y despues se sube esta: las dos
        // cosas en la misma transaccion o el indice rechaza el paso.
        await bd.EnTransaccionAsync(async token =>
        {
            await bd.Sedes
                .IgnoreQueryFilters()
                .Where(s => s.EmpresaId == empresaId && s.EsPrincipal)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EsPrincipal, false), token);

            await bd.Sedes
                .IgnoreQueryFilters()
                .Where(s => s.Id == sedeId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.EsPrincipal, true), token);
        }, ct);

        await bitacora.RegistrarAsync("SEDE.PRINCIPAL", "sede", sedeId.ToString(),
            new { empresaId, sede.Nombre }, ct);

        return Resultado<SedeItem>.Ok(await Consultar(empresaId).FirstAsync(s => s.Id == sedeId, ct));
    }

    private Task<bool> ExisteEmpresaAsync(int empresaId, CancellationToken ct) =>
        bd.Empresas.IgnoreQueryFilters().AnyAsync(e => e.Id == empresaId, ct);
}
