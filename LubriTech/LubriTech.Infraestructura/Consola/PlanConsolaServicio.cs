using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Consola;

/// <summary>
/// Los planes son datos: que funciones habilitan y cuanta gente de cada rol
/// admiten se edita desde aqui, no en el codigo.
///
/// La regla que cuida esta clase: guardar un plan NUNCA deja empresas fuera
/// de norma en silencio. Si bajar un cupo dejaria a alguien excedido, se
/// rechaza con 409 y se dice a quien.
/// </summary>
public class PlanConsolaServicio(
    LubriTechContexto bd,
    IPermisoServicio permisos,
    IValidadorSesion validador,
    IBitacoraServicio bitacora) : IPlanConsolaServicio
{
    public async Task<List<FuncionItem>> FuncionesAsync(CancellationToken ct = default) =>
        await bd.Funciones
            .AsNoTracking()
            .OrderBy(f => f.Orden).ThenBy(f => f.Codigo)
            .Select(f => new FuncionItem
            {
                Codigo = f.Codigo,
                Nombre = f.Nombre,
                Descripcion = f.Descripcion,
                Grupo = f.Grupo,
                Orden = f.Orden
            })
            .ToListAsync(ct);

    /// <summary>
    /// Los planes son un punado y los lee la pantalla de armado completa, asi
    /// que este listado no se pagina a proposito: ver [[paginar lo que crece]]
    /// es para lo que crece, y la cantidad de planes no crece.
    /// </summary>
    public async Task<List<PlanDetalle>> ListarAsync(CancellationToken ct = default)
    {
        var planes = await bd.Planes
            .AsNoTracking()
            .Include(p => p.Funciones)
            .Include(p => p.Cupos)
            .OrderBy(p => p.Orden).ThenBy(p => p.Nombre)
            .ToListAsync(ct);

        var nombres = await bd.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);

        var empresasPorPlan = await bd.Empresas
            .IgnoreQueryFilters()
            .GroupBy(e => e.PlanId)
            .Select(g => new { PlanId = g.Key, Cuantas = g.Count() })
            .ToDictionaryAsync(x => x.PlanId, x => x.Cuantas, ct);

        return [.. planes.Select(p => Armar(p, nombres, empresasPorPlan))];
    }

    public async Task<Resultado<PlanDetalle>> VerAsync(int planId, CancellationToken ct = default)
    {
        var detalle = await ArmarDetalleAsync(planId, ct);

        return detalle is null
            ? Resultado<PlanDetalle>.Error(TipoFallo.NoEncontrado, "El plan no existe.", "NO_ENCONTRADO")
            : Resultado<PlanDetalle>.Ok(detalle);
    }

    private async Task<PlanDetalle?> ArmarDetalleAsync(int planId, CancellationToken ct)
    {
        var plan = await bd.Planes
            .AsNoTracking()
            .Include(p => p.Funciones)
            .Include(p => p.Cupos)
            .FirstOrDefaultAsync(p => p.Id == planId, ct);

        if (plan is null) return null;

        var nombres = await bd.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);
        var cuantas = await bd.Empresas.IgnoreQueryFilters().CountAsync(e => e.PlanId == planId, ct);

        return Armar(plan, nombres, new Dictionary<int, int> { [planId] = cuantas });
    }

    private static PlanDetalle Armar(
        Plan plan, Dictionary<int, string> nombresDeRol, Dictionary<int, int> empresasPorPlan) => new()
    {
        Id = plan.Id,
        Codigo = plan.Codigo,
        Nombre = plan.Nombre,
        PrecioMensual = plan.PrecioMensual,
        MaxSedes = plan.MaxSedes,
        Orden = plan.Orden,
        Activo = plan.Activo,
        Funciones = [.. plan.Funciones.Select(f => f.Funcion).Order()],
        Cupos =
        [
            .. plan.Cupos.OrderBy(c => c.RolId).Select(c => new CupoDeRol
            {
                RolId = c.RolId,
                Rol = nombresDeRol.TryGetValue(c.RolId, out var n) ? n : null,
                Cupo = c.Cupo
            })
        ],
        Empresas = empresasPorPlan.TryGetValue(plan.Id, out var cuantas) ? cuantas : 0
    };

    // -----------------------------------------------------------------------
    // Alta
    // -----------------------------------------------------------------------

    public async Task<Resultado<PlanDetalle>> CrearAsync(
        GuardarPlanPeticion peticion, CancellationToken ct = default)
    {
        var codigo = (peticion.Codigo ?? string.Empty).Trim().ToUpperInvariant();

        if (await bd.Planes.AnyAsync(p => p.Codigo == codigo, ct))
            return Resultado<PlanDetalle>.Error(TipoFallo.Conflicto,
                "Ya existe un plan con ese codigo.", "CODIGO_DUPLICADO");

        var revision = await RevisarCatalogosAsync(peticion, ct);
        if (revision is not null) return revision;

        var plan = new Plan
        {
            Codigo = codigo,
            Nombre = peticion.Nombre.Trim(),
            PrecioMensual = peticion.PrecioMensual,
            MaxSedes = peticion.MaxSedes,
            Orden = peticion.Orden,
            Activo = peticion.Activo
        };

        bd.Planes.Add(plan);
        await bd.SaveChangesAsync(ct);

        await GuardarFuncionesYCuposAsync(plan.Id, peticion, ct);

        await bitacora.RegistrarAsync("SA.PLAN.ALTA", "plan", plan.Id.ToString(), new
        {
            plan.Codigo,
            plan.PrecioMensual,
            peticion.Funciones,
            cupos = peticion.Cupos
        }, ct);

        return Resultado<PlanDetalle>.Ok((await ArmarDetalleAsync(plan.Id, ct))!);
    }

    // -----------------------------------------------------------------------
    // Edicion
    // -----------------------------------------------------------------------

    public async Task<Resultado<PlanDetalle>> ActualizarAsync(
        int planId, GuardarPlanPeticion peticion, CancellationToken ct = default)
    {
        var plan = await bd.Planes
            .Include(p => p.Funciones)
            .Include(p => p.Cupos)
            .FirstOrDefaultAsync(p => p.Id == planId, ct);

        if (plan is null)
            return Resultado<PlanDetalle>.Error(TipoFallo.NoEncontrado, "El plan no existe.", "NO_ENCONTRADO");

        var revision = await RevisarCatalogosAsync(peticion, ct);
        if (revision is not null) return revision;

        // Lo importante: que el plan nuevo le siga alcanzando a las empresas
        // que ya lo tienen contratado.
        var choques = await ChoquesConLasEmpresasAsync(planId, peticion, ct);

        if (choques.Count > 0)
            return Resultado<PlanDetalle>.Error(TipoFallo.Conflicto,
                "El cambio dejaria empresas fuera de norma: " + string.Join("; ", choques),
                "EMPRESAS_EXCEDIDAS");

        // El codigo no se edita: es la referencia estable del plan.
        plan.Nombre = peticion.Nombre.Trim();
        plan.PrecioMensual = peticion.PrecioMensual;
        plan.MaxSedes = peticion.MaxSedes;
        plan.Orden = peticion.Orden;
        plan.Activo = peticion.Activo;

        await bd.SaveChangesAsync(ct);
        await GuardarFuncionesYCuposAsync(planId, peticion, ct);

        // El permiso efectivo de TODA empresa en este plan acaba de cambiar.
        var afectadas = await bd.Empresas
            .IgnoreQueryFilters()
            .Where(e => e.PlanId == planId)
            .Select(e => e.Id)
            .ToListAsync(ct);

        foreach (var empresaId in afectadas)
        {
            permisos.Invalidar(empresaId);
            validador.Invalidar(empresaId: empresaId);
        }

        await bitacora.RegistrarAsync("SA.PLAN.EDICION", "plan", planId.ToString(), new
        {
            plan.Codigo,
            plan.PrecioMensual,
            plan.MaxSedes,
            peticion.Funciones,
            cupos = peticion.Cupos,
            empresasAfectadas = afectadas.Count
        }, ct);

        return Resultado<PlanDetalle>.Ok((await ArmarDetalleAsync(planId, ct))!);
    }

    // -----------------------------------------------------------------------
    // Piezas
    // -----------------------------------------------------------------------

    /// <summary>
    /// Que lo que piden exista de verdad: funciones del catalogo y roles
    /// reales, sin el rol 0 (el SuperAdmin no es de ninguna empresa).
    /// </summary>
    private async Task<Resultado<PlanDetalle>?> RevisarCatalogosAsync(
        GuardarPlanPeticion peticion, CancellationToken ct)
    {
        if (peticion.MaxSedes < 0)
            return Resultado<PlanDetalle>.Error(TipoFallo.Validacion,
                "El maximo de sedes no puede ser negativo (0 = sin tope).", "MAX_SEDES");

        var funciones = peticion.Funciones
            .Select(f => f.Trim().ToUpperInvariant())
            .Where(f => f.Length > 0)
            .Distinct()
            .ToList();

        var existentes = await bd.Funciones.Select(f => f.Codigo).ToListAsync(ct);
        var inventadas = funciones.Except(existentes, StringComparer.Ordinal).ToList();

        if (inventadas.Count > 0)
            return Resultado<PlanDetalle>.Error(TipoFallo.Validacion,
                "Estas funciones no existen en el catalogo: " + string.Join(", ", inventadas),
                "FUNCION_INVALIDA");

        var roles = await bd.Roles.Select(r => r.Id).ToListAsync(ct);

        foreach (var cupo in peticion.Cupos)
        {
            if (cupo.RolId == Roles.SuperAdmin)
                return Resultado<PlanDetalle>.Error(TipoFallo.Validacion,
                    "El SuperAdmin no pertenece a ninguna empresa: no lleva cupo.", "CUPO_SUPERADMIN");

            if (!roles.Contains(cupo.RolId))
                return Resultado<PlanDetalle>.Error(TipoFallo.Validacion,
                    $"El rol {cupo.RolId} no existe.", "ROL_INVALIDO");

            if (cupo.Cupo < 0)
                return Resultado<PlanDetalle>.Error(TipoFallo.Validacion,
                    "Un cupo no puede ser negativo (0 = sin tope).", "CUPO_NEGATIVO");
        }

        if (peticion.Cupos.GroupBy(c => c.RolId).Any(g => g.Count() > 1))
            return Resultado<PlanDetalle>.Error(TipoFallo.Validacion,
                "Hay un rol repetido en los cupos.", "CUPO_REPETIDO");

        return null;
    }

    private async Task<List<string>> ChoquesConLasEmpresasAsync(
        int planId, GuardarPlanPeticion peticion, CancellationToken ct)
    {
        var choques = new List<string>();

        var empresas = await bd.Empresas
            .IgnoreQueryFilters()
            .Where(e => e.PlanId == planId && e.Activo)
            .Select(e => new { e.Id, e.NombreComercial })
            .ToListAsync(ct);

        if (empresas.Count == 0) return choques;

        var ids = empresas.Select(e => e.Id).ToList();

        var sedesPorEmpresa = await bd.Sedes
            .IgnoreQueryFilters()
            .Where(s => ids.Contains(s.EmpresaId) && s.Activo)
            .GroupBy(s => s.EmpresaId)
            .Select(g => new { EmpresaId = g.Key, Cuantas = g.Count() })
            .ToDictionaryAsync(x => x.EmpresaId, x => x.Cuantas, ct);

        var usoPorEmpresaYRol = await bd.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.EmpresaId != null && ids.Contains(u.EmpresaId!.Value) && u.Activo)
            .GroupBy(u => new { EmpresaId = u.EmpresaId!.Value, u.RolId })
            .Select(g => new { g.Key.EmpresaId, g.Key.RolId, Cuantos = g.Count() })
            .ToListAsync(ct);

        var nombresDeRol = await bd.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);

        foreach (var empresa in empresas)
        {
            if (peticion.MaxSedes > 0
                && sedesPorEmpresa.TryGetValue(empresa.Id, out var sedes)
                && sedes > peticion.MaxSedes)
            {
                choques.Add($"{empresa.NombreComercial} tiene {sedes} sede(s) y quedaria en {peticion.MaxSedes}");
            }

            foreach (var uso in usoPorEmpresaYRol.Where(u => u.EmpresaId == empresa.Id).OrderBy(u => u.RolId))
            {
                var rol = nombresDeRol.TryGetValue(uso.RolId, out var n) ? n : $"rol {uso.RolId}";
                var cupo = peticion.Cupos.FirstOrDefault(c => c.RolId == uso.RolId);

                if (cupo is null)
                    choques.Add($"{empresa.NombreComercial} tiene {uso.Cuantos} {rol} y el plan dejaria de habilitar ese rol");
                else if (cupo.Cupo > 0 && uso.Cuantos > cupo.Cupo)
                    choques.Add($"{empresa.NombreComercial} tiene {uso.Cuantos} {rol} y quedaria en {cupo.Cupo}");
            }
        }

        return choques;
    }

    /// <summary>
    /// Reemplaza funciones y cupos por lo que llego. Se borra y se vuelve a
    /// insertar: son dos tablas de pocas filas y asi no queda nada viejo
    /// colgado por un olvido en la comparacion.
    /// </summary>
    private async Task GuardarFuncionesYCuposAsync(
        int planId, GuardarPlanPeticion peticion, CancellationToken ct)
    {
        await bd.PlanFunciones.Where(f => f.PlanId == planId).ExecuteDeleteAsync(ct);
        await bd.PlanRolCupos.Where(c => c.PlanId == planId).ExecuteDeleteAsync(ct);

        var funciones = peticion.Funciones
            .Select(f => f.Trim().ToUpperInvariant())
            .Where(f => f.Length > 0)
            .Distinct()
            .Select(f => new PlanFuncion { PlanId = planId, Funcion = f });

        bd.PlanFunciones.AddRange(funciones);

        bd.PlanRolCupos.AddRange(peticion.Cupos.Select(c => new PlanRolCupo
        {
            PlanId = planId,
            RolId = c.RolId,
            Cupo = c.Cupo
        }));

        await bd.SaveChangesAsync(ct);
    }
}
