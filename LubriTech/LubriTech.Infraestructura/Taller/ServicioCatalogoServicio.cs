using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// El catalogo de servicios del taller. Tenant-scoped: todo sale por el filtro
/// de empresa; las altas fijan EmpresaId con el que llega del token.
/// </summary>
public class ServicioCatalogoServicio(
    LubriTechContexto bd,
    IBitacoraServicio bitacora) : IServicioCatalogoServicio
{
    public async Task<Pagina<ServicioItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        string? categoria = null, bool? activo = null, CancellationToken ct = default)
    {
        var q = bd.Servicios.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(categoria))
            q = q.Where(s => s.Categoria == categoria);
        if (activo.HasValue)
            q = q.Where(s => s.Activo == activo.Value);

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var b = consulta.Buscar.Trim().ToLower();
            q = q.Where(s => s.Nombre.ToLower().Contains(b) || s.Codigo.ToLower().Contains(b));
        }

        var total = await q.CountAsync(ct);
        var filas = await q
            .OrderBy(s => s.Nombre)
            .Skip(consulta.Saltar).Take(consulta.Tamano)
            .ToListAsync(ct);

        return Pagina<ServicioItem>.De(filas.Select(Proyectar).ToList(), consulta, total);
    }

    public async Task<Resultado<ServicioItem>> VerAsync(int empresaId, long servicioId, CancellationToken ct = default)
    {
        var s = await bd.Servicios.AsNoTracking().FirstOrDefaultAsync(x => x.Id == servicioId, ct);
        return s is null
            ? Resultado<ServicioItem>.Error(TipoFallo.NoEncontrado, "El servicio no existe.", "NO_ENCONTRADO")
            : Resultado<ServicioItem>.Ok(Proyectar(s));
    }

    public async Task<Resultado<ServicioItem>> CrearAsync(
        int empresaId, GuardarServicioPeticion peticion, CancellationToken ct = default)
    {
        var codigo = peticion.Codigo.Trim();

        if (await bd.Servicios.AnyAsync(s => s.Codigo.ToLower() == codigo.ToLower(), ct))
            return Resultado<ServicioItem>.Error(TipoFallo.Conflicto,
                "Ya hay un servicio con ese codigo.", "CODIGO_DUPLICADO");

        var servicio = new Servicio
        {
            EmpresaId = empresaId,
            Codigo = codigo,
            Nombre = peticion.Nombre.Trim(),
            Categoria = Limpiar(peticion.Categoria),
            Descripcion = Limpiar(peticion.Descripcion),
            Precio = peticion.Precio,
            DuracionMin = peticion.DuracionMin,
            Activo = true
        };

        bd.Servicios.Add(servicio);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("SERVICIO.ALTA", "servicio", servicio.Id.ToString(),
            new { servicio.Codigo, servicio.Nombre }, ct);

        return Resultado<ServicioItem>.Ok(Proyectar(servicio));
    }

    public async Task<Resultado<ServicioItem>> ActualizarAsync(
        int empresaId, long servicioId, GuardarServicioPeticion peticion, CancellationToken ct = default)
    {
        var servicio = await bd.Servicios.FirstOrDefaultAsync(s => s.Id == servicioId, ct);
        if (servicio is null)
            return Resultado<ServicioItem>.Error(TipoFallo.NoEncontrado, "El servicio no existe.", "NO_ENCONTRADO");

        var codigo = peticion.Codigo.Trim();
        if (await bd.Servicios.AnyAsync(s => s.Id != servicioId && s.Codigo.ToLower() == codigo.ToLower(), ct))
            return Resultado<ServicioItem>.Error(TipoFallo.Conflicto,
                "Ya hay otro servicio con ese codigo.", "CODIGO_DUPLICADO");

        servicio.Codigo = codigo;
        servicio.Nombre = peticion.Nombre.Trim();
        servicio.Categoria = Limpiar(peticion.Categoria);
        servicio.Descripcion = Limpiar(peticion.Descripcion);
        servicio.Precio = peticion.Precio;
        servicio.DuracionMin = peticion.DuracionMin;

        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("SERVICIO.EDICION", "servicio", servicioId.ToString(),
            new { servicio.Codigo, servicio.Nombre }, ct);

        return Resultado<ServicioItem>.Ok(Proyectar(servicio));
    }

    public async Task<Resultado<ServicioItem>> CambiarEstadoAsync(
        int empresaId, long servicioId, EstadoPeticion peticion, CancellationToken ct = default)
    {
        var servicio = await bd.Servicios.FirstOrDefaultAsync(s => s.Id == servicioId, ct);
        if (servicio is null)
            return Resultado<ServicioItem>.Error(TipoFallo.NoEncontrado, "El servicio no existe.", "NO_ENCONTRADO");

        servicio.Activo = peticion.Activo;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("SERVICIO.ESTADO", "servicio", servicioId.ToString(),
            new { servicio.Activo }, ct);

        return Resultado<ServicioItem>.Ok(Proyectar(servicio));
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static ServicioItem Proyectar(Servicio s) => new()
    {
        Id = s.Id,
        Codigo = s.Codigo,
        Nombre = s.Nombre,
        Categoria = s.Categoria,
        Precio = s.Precio,
        DuracionMin = s.DuracionMin,
        Activo = s.Activo
    };
}
