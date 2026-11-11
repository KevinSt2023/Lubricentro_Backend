using LubriTech.Aplicacion.Servicios;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace LubriTech.Infraestructura.Seguridad;

/// <summary>
/// Permiso efectivo = lo que el rol puede, intersectado con lo que el plan
/// de la empresa habilita. Las dos mitades son datos editables, no codigo.
///
/// Se cachea 30 segundos por (empresa, rol) para no pegarle a la base en
/// cada peticion, y se tira el cache al instante cuando cambia el plan o la
/// matriz de permisos.
/// </summary>
public class PermisoServicio(LubriTechContexto bd, IMemoryCache cache) : IPermisoServicio
{
    private static readonly TimeSpan Vida = TimeSpan.FromSeconds(30);

    private static string ClavePermisos(int empresaId, int rolId) => $"perm:{empresaId}:{rolId}";
    private static string ClaveFunciones(int empresaId) => $"func:{empresaId}";

    public async Task<IReadOnlyCollection<string>> FuncionesDelPlanAsync(
        int empresaId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(ClaveFunciones(empresaId), out IReadOnlyCollection<string>? guardado)
            && guardado is not null)
            return guardado;

        var funciones = await bd.Empresas
            .IgnoreQueryFilters()                 // se consulta por id explicito
            .Where(e => e.Id == empresaId)
            .SelectMany(e => e.Plan!.Funciones.Select(f => f.Funcion))
            .ToListAsync(ct);

        IReadOnlyCollection<string> resultado = funciones.ToHashSet(StringComparer.Ordinal);
        cache.Set(ClaveFunciones(empresaId), resultado, Vida);
        return resultado;
    }

    public async Task<IReadOnlyCollection<string>> PermisosEfectivosAsync(
        int empresaId, int rolId, CancellationToken ct = default)
    {
        if (cache.TryGetValue(ClavePermisos(empresaId, rolId), out IReadOnlyCollection<string>? guardado)
            && guardado is not null)
            return guardado;

        var delRol = await bd.RolPermisos
            .IgnoreQueryFilters()
            .Where(rp => rp.RolId == rolId)
            .Select(rp => new { rp.PermisoCodigo, rp.Permiso!.FuncionRequerida })
            .ToListAsync(ct);

        var funciones = await FuncionesDelPlanAsync(empresaId, ct);

        // La interseccion: si el permiso exige una funcion y el plan no la
        // tiene, el permiso simplemente no existe para esta empresa.
        var efectivos = delRol
            .Where(p => p.FuncionRequerida is null || funciones.Contains(p.FuncionRequerida))
            .Select(p => p.PermisoCodigo)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyCollection<string> resultado = efectivos;
        cache.Set(ClavePermisos(empresaId, rolId), resultado, Vida);
        return resultado;
    }

    public async Task<bool> TienePermisoAsync(
        int empresaId, int rolId, string permiso, CancellationToken ct = default)
    {
        var efectivos = await PermisosEfectivosAsync(empresaId, rolId, ct);
        return efectivos.Contains(permiso);
    }

    public void Invalidar(int empresaId)
    {
        cache.Remove(ClaveFunciones(empresaId));

        // Los roles son nueve y fijos: tirarlos todos es mas barato y mas
        // seguro que llevar la cuenta de cuales estan cacheados.
        for (var rol = 0; rol <= 8; rol++)
            cache.Remove(ClavePermisos(empresaId, rol));
    }
}
