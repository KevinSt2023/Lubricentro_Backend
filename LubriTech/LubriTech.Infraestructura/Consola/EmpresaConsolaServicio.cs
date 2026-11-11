using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using LubriTech.Infraestructura.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Consola;

/// <summary>
/// La consola del SuperAdmin sobre las empresas.
///
/// Todo aqui cruza empresas a proposito, asi que cada consulta va con
/// IgnoreQueryFilters() de forma explicita. Los endpoints que lo exponen
/// estan cerrados al rol 0 con [SoloSuperAdmin]: el filtro de EF por si solo
/// no alcanza, porque aqui se desactiva.
/// </summary>
public class EmpresaConsolaServicio(
    LubriTechContexto bd,
    IClaves claves,
    IPermisoServicio permisos,
    IValidadorSesion validador,
    IBitacoraServicio bitacora) : IEmpresaConsolaServicio
{
    // -----------------------------------------------------------------------
    // Listado
    // -----------------------------------------------------------------------

    public async Task<Pagina<EmpresaListaItem>> ListarAsync(
        ConsultaPaginada consulta, string? estado = null, int? planId = null, CancellationToken ct = default)
    {
        var hoy = Tiempo.HoyLima();

        var origen = bd.Empresas.IgnoreQueryFilters().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var texto = consulta.Buscar.Trim().ToLower();
            origen = origen.Where(e =>
                e.Ruc.Contains(texto) ||
                e.NombreComercial.ToLower().Contains(texto) ||
                e.RazonSocial.ToLower().Contains(texto));
        }

        if (planId.HasValue) origen = origen.Where(e => e.PlanId == planId.Value);

        // El filtro de estado se traduce a condiciones sobre la base, no se
        // resuelve en memoria: paginar sobre una lista ya materializada seria
        // traer todas las empresas para mostrar veinte.
        origen = estado switch
        {
            EstadoServicio.Inactiva => origen.Where(e => !e.Activo),
            EstadoServicio.Vencido => origen.Where(e => e.Activo && e.ServicioHasta < hoy),
            EstadoServicio.PorVencer => origen.Where(e => e.Activo
                && e.ServicioHasta >= hoy
                && e.ServicioHasta <= hoy.AddDays(EstadoServicio.DiasDeAviso)),
            EstadoServicio.AlDia => origen.Where(e => e.Activo
                && e.ServicioHasta > hoy.AddDays(EstadoServicio.DiasDeAviso)),
            _ => origen
        };

        var total = await origen.CountAsync(ct);

        var items = await origen
            .OrderBy(e => e.NombreComercial)
            .Skip(consulta.Saltar)
            .Take(consulta.Tamano)
            .Select(e => new EmpresaListaItem
            {
                Id = e.Id,
                Ruc = e.Ruc,
                RazonSocial = e.RazonSocial,
                NombreComercial = e.NombreComercial,
                Telefono = e.Telefono,
                Email = e.Email,
                PlanId = e.PlanId,
                Plan = e.Plan!.Nombre,
                Activo = e.Activo,
                ServicioHasta = e.ServicioHasta,
                CreadoEn = e.CreadoEn,
                Sedes = bd.Sedes.IgnoreQueryFilters().Count(s => s.EmpresaId == e.Id && s.Activo),
                Usuarios = bd.Usuarios.IgnoreQueryFilters().Count(u => u.EmpresaId == e.Id && u.Activo)
            })
            .ToListAsync(ct);

        foreach (var item in items) Completar(item, hoy);

        return Pagina<EmpresaListaItem>.De(items, consulta, total);
    }

    private static void Completar(EmpresaListaItem item, DateOnly hoy)
    {
        item.DiasRestantes = item.ServicioHasta.DayNumber - hoy.DayNumber;
        item.EstadoServicio = EstadoServicio.Calcular(item.Activo, item.ServicioHasta, hoy);
    }

    // -----------------------------------------------------------------------
    // Detalle
    // -----------------------------------------------------------------------

    public async Task<Resultado<EmpresaDetalle>> VerAsync(int empresaId, CancellationToken ct = default)
    {
        var detalle = await ArmarDetalleAsync(empresaId, ct);

        return detalle is null
            ? Resultado<EmpresaDetalle>.Error(TipoFallo.NoEncontrado, "La empresa no existe.", "NO_ENCONTRADO")
            : Resultado<EmpresaDetalle>.Ok(detalle);
    }

    private async Task<EmpresaDetalle?> ArmarDetalleAsync(int empresaId, CancellationToken ct)
    {
        var hoy = Tiempo.HoyLima();

        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(e => e.Plan)
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

        if (empresa is null) return null;

        var detalle = new EmpresaDetalle
        {
            Id = empresa.Id,
            Ruc = empresa.Ruc,
            RazonSocial = empresa.RazonSocial,
            NombreComercial = empresa.NombreComercial,
            Direccion = empresa.Direccion,
            Telefono = empresa.Telefono,
            Email = empresa.Email,
            PlanId = empresa.PlanId,
            Plan = empresa.Plan?.Nombre ?? string.Empty,
            PrecioMensual = empresa.Plan?.PrecioMensual ?? 0,
            MaxSedes = empresa.Plan?.MaxSedes ?? 0,
            Activo = empresa.Activo,
            ServicioHasta = empresa.ServicioHasta,
            CreadoEn = empresa.CreadoEn
        };

        Completar(detalle, hoy);

        detalle.ListaSedes = await bd.Sedes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.EmpresaId == empresaId)
            .OrderByDescending(s => s.EsPrincipal).ThenBy(s => s.Nombre)
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
            })
            .ToListAsync(ct);

        detalle.Sedes = detalle.ListaSedes.Count(s => s.Activo);
        detalle.Usuarios = await bd.Usuarios
            .IgnoreQueryFilters().CountAsync(u => u.EmpresaId == empresaId && u.Activo, ct);

        detalle.Funciones = [.. (await permisos.FuncionesDelPlanAsync(empresaId, ct)).Order()];
        detalle.UsoPorRol = await UsoPorRolAsync(empresaId, empresa.PlanId, ct);

        detalle.Admin = await bd.Usuarios
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.EmpresaId == empresaId && u.RolId == Roles.Admin)
            .OrderByDescending(u => u.Activo)
            .Select(u => new AdminDeEmpresa
            {
                Id = u.Id,
                Nombre = u.Nombres + " " + u.Apellidos,
                Email = u.Email,
                Activo = u.Activo,
                DebeCambiarClave = u.DebeCambiarClave,
                UltimoAcceso = u.UltimoAcceso
            })
            .FirstOrDefaultAsync(ct);

        detalle.UltimaSuscripcion = await bd.Suscripciones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.EmpresaId == empresaId && s.Estado == "PAGADA")
            .OrderByDescending(s => s.Hasta)
            .Select(s => new SuscripcionItem
            {
                Id = s.Id,
                EmpresaId = s.EmpresaId,
                PlanId = s.PlanId,
                Desde = s.Desde,
                Hasta = s.Hasta,
                Monto = s.Monto,
                Estado = s.Estado,
                Referencia = s.Referencia,
                RegistradoEn = s.RegistradoEn
            })
            .FirstOrDefaultAsync(ct);

        if (detalle.UltimaSuscripcion is not null)
            detalle.UltimaSuscripcion.Dias =
                detalle.UltimaSuscripcion.Hasta.DayNumber - detalle.UltimaSuscripcion.Desde.DayNumber + 1;

        return detalle;
    }

    /// <summary>
    /// Cuanto usa la empresa de cada rol, contra lo que el plan le da.
    /// Cupo nulo = el plan no habilita ese rol; 0 = sin tope.
    /// </summary>
    private async Task<List<UsoDeRol>> UsoPorRolAsync(int empresaId, int planId, CancellationToken ct)
    {
        var roles = await bd.Roles
            .AsNoTracking()
            .Where(r => r.Id > Roles.SuperAdmin)
            .OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.Nombre })
            .ToListAsync(ct);

        var cupos = await bd.PlanRolCupos
            .AsNoTracking()
            .Where(c => c.PlanId == planId)
            .ToDictionaryAsync(c => c.RolId, c => c.Cupo, ct);

        var usados = await bd.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.EmpresaId == empresaId && u.Activo)
            .GroupBy(u => u.RolId)
            .Select(g => new { RolId = g.Key, Cuantos = g.Count() })
            .ToDictionaryAsync(x => x.RolId, x => x.Cuantos, ct);

        return [.. roles.Select(r =>
        {
            var cupo = cupos.TryGetValue(r.Id, out var c) ? c : (int?)null;
            var cuantos = usados.TryGetValue(r.Id, out var u) ? u : 0;

            return new UsoDeRol
            {
                RolId = r.Id,
                Rol = r.Nombre,
                Cupo = cupo,
                Usados = cuantos,
                // Excedido si el plan no habilita el rol y hay gente con el,
                // o si hay mas gente que cupo. Cupo 0 nunca se excede.
                Excedido = cuantos > 0 && (cupo is null || (cupo > 0 && cuantos > cupo))
            };
        })];
    }

    // -----------------------------------------------------------------------
    // Alta
    // -----------------------------------------------------------------------

    public async Task<Resultado<EmpresaCreada>> CrearAsync(
        CrearEmpresaPeticion peticion, CancellationToken ct = default)
    {
        var ruc = (peticion.Ruc ?? string.Empty).Trim();
        var emailAdmin = (peticion.Admin.Email ?? string.Empty).Trim().ToLowerInvariant();

        var plan = await bd.Planes
            .Include(p => p.Cupos)
            .FirstOrDefaultAsync(p => p.Codigo == peticion.PlanCodigo.Trim().ToUpperInvariant(), ct);

        if (plan is null)
            return Resultado<EmpresaCreada>.Error(TipoFallo.Validacion,
                "El plan indicado no existe.", "PLAN_INVALIDO");

        if (!plan.Activo)
            return Resultado<EmpresaCreada>.Error(TipoFallo.Validacion,
                "Ese plan ya no esta disponible para nuevas empresas.", "PLAN_INACTIVO");

        // El plan tiene que habilitar al Admin, o la empresa nace sin poder
        // entrar: el candado de cupo de la base rechazaria el alta despues.
        if (!plan.Cupos.Any(c => c.RolId == Roles.Admin))
            return Resultado<EmpresaCreada>.Error(TipoFallo.Conflicto,
                "Ese plan no habilita el rol Admin: no se puede dar de alta una empresa con el.",
                "PLAN_SIN_ADMIN");

        if (await bd.Empresas.IgnoreQueryFilters().AnyAsync(e => e.Ruc == ruc, ct))
            return Resultado<EmpresaCreada>.Error(TipoFallo.Conflicto,
                "Ya existe una empresa con ese RUC.", "RUC_DUPLICADO");

        if (await bd.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email.ToLower() == emailAdmin, ct))
            return Resultado<EmpresaCreada>.Error(TipoFallo.Conflicto,
                "Ese correo ya esta en uso por otra cuenta del sistema.", "EMAIL_DUPLICADO");

        var hoy = Tiempo.HoyLima();
        var claveTemporal = claves.NuevaClaveTemporal();

        // Una sola transaccion: una empresa sin sede o sin Admin no sirve
        // para nada, y a medio crear es peor que no estar. Va por
        // EnTransaccionAsync porque la conexion tiene reintentos y EF no
        // admite transacciones a mano con esa estrategia.
        var empresa = await bd.EnTransaccionAsync(async token =>
        {
            var nueva = new Empresa
            {
                Ruc = ruc,
                RazonSocial = peticion.RazonSocial.Trim(),
                NombreComercial = peticion.NombreComercial.Trim(),
                Direccion = peticion.Direccion?.Trim(),
                Telefono = peticion.Telefono?.Trim(),
                Email = peticion.Email?.Trim(),
                PlanId = plan.Id,
                Activo = true,
                ServicioHasta = hoy.AddDays(peticion.DiasDeCortesia)
            };

            bd.Empresas.Add(nueva);
            await bd.SaveChangesAsync(token);

            // La cortesia es servicio CONCEDIDO, y por eso queda registrada
            // como un periodo de monto cero. Si no se registrara, el dia que
            // alguien anule el primer pago real, servicio_hasta se
            // recalcularia desde los periodos que quedan, no encontraria
            // ninguno y la empresa perderia los dias de prueba que se le
            // habian regalado. Monto cero: no ensucia el ingreso del mes.
            if (peticion.DiasDeCortesia > 0)
            {
                bd.Suscripciones.Add(new Suscripcion
                {
                    EmpresaId = nueva.Id,
                    PlanId = plan.Id,
                    Desde = hoy,
                    Hasta = nueva.ServicioHasta,
                    Monto = 0,
                    Estado = "PAGADA",
                    Referencia = "CORTESIA"
                });

                await bd.SaveChangesAsync(token);
            }

            var sede = new Sede
            {
                EmpresaId = nueva.Id,
                Nombre = string.IsNullOrWhiteSpace(peticion.SedePrincipal.Nombre)
                    ? "Sede Principal"
                    : peticion.SedePrincipal.Nombre.Trim(),
                Direccion = peticion.SedePrincipal.Direccion?.Trim() ?? nueva.Direccion,
                Telefono = peticion.SedePrincipal.Telefono?.Trim() ?? nueva.Telefono,
                EsPrincipal = true,
                Activo = true
            };

            bd.Sedes.Add(sede);
            await bd.SaveChangesAsync(token);

            var admin = new Usuario
            {
                EmpresaId = nueva.Id,
                SedeId = sede.Id,
                RolId = Roles.Admin,
                Dni = string.IsNullOrWhiteSpace(peticion.Admin.Dni) ? null : peticion.Admin.Dni.Trim(),
                Nombres = peticion.Admin.Nombres.Trim(),
                Apellidos = peticion.Admin.Apellidos.Trim(),
                Email = emailAdmin,
                Telefono = peticion.Admin.Telefono?.Trim(),
                PasswordHash = claves.Hash(claveTemporal),
                DebeCambiarClave = true,
                Activo = true
            };

            bd.Usuarios.Add(admin);
            await bd.SaveChangesAsync(token);

            return nueva;
        }, ct);

        permisos.Invalidar(empresa.Id);

        await bitacora.RegistrarAsync("SA.EMPRESA.ALTA", "empresa", empresa.Id.ToString(), new
        {
            empresa.Ruc,
            empresa.NombreComercial,
            plan = plan.Codigo,
            diasDeCortesia = peticion.DiasDeCortesia,
            servicioHasta = empresa.ServicioHasta,
            adminEmail = emailAdmin
        }, ct);

        var detalle = await ArmarDetalleAsync(empresa.Id, ct);

        return Resultado<EmpresaCreada>.Ok(new EmpresaCreada
        {
            Empresa = detalle!,
            EmailAdmin = emailAdmin,
            ClaveTemporal = claveTemporal
        });
    }

    // -----------------------------------------------------------------------
    // Edicion
    // -----------------------------------------------------------------------

    public async Task<Resultado<EmpresaDetalle>> ActualizarAsync(
        int empresaId, ActualizarEmpresaPeticion peticion, CancellationToken ct = default)
    {
        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

        if (empresa is null)
            return Resultado<EmpresaDetalle>.Error(TipoFallo.NoEncontrado, "La empresa no existe.", "NO_ENCONTRADO");

        // El RUC no se edita: es la identidad tributaria de la empresa y
        // cuelgan de ella los comprobantes. Si esta mal, es otra empresa.
        empresa.RazonSocial = peticion.RazonSocial.Trim();
        empresa.NombreComercial = peticion.NombreComercial.Trim();
        empresa.Direccion = peticion.Direccion?.Trim();
        empresa.Telefono = peticion.Telefono?.Trim();
        empresa.Email = peticion.Email?.Trim();

        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("SA.EMPRESA.EDICION", "empresa", empresaId.ToString(), new
        {
            empresa.RazonSocial,
            empresa.NombreComercial
        }, ct);

        return Resultado<EmpresaDetalle>.Ok((await ArmarDetalleAsync(empresaId, ct))!);
    }

    public async Task<Resultado<EmpresaDetalle>> CambiarPlanAsync(
        int empresaId, CambiarPlanPeticion peticion, CancellationToken ct = default)
    {
        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

        if (empresa is null)
            return Resultado<EmpresaDetalle>.Error(TipoFallo.NoEncontrado, "La empresa no existe.", "NO_ENCONTRADO");

        var nuevo = await bd.Planes
            .Include(p => p.Cupos)
            .FirstOrDefaultAsync(p => p.Codigo == peticion.PlanCodigo.Trim().ToUpperInvariant(), ct);

        if (nuevo is null)
            return Resultado<EmpresaDetalle>.Error(TipoFallo.Validacion, "El plan indicado no existe.", "PLAN_INVALIDO");

        if (nuevo.Id == empresa.PlanId)
            return Resultado<EmpresaDetalle>.Ok((await ArmarDetalleAsync(empresaId, ct))!);

        // Bajar de plan no puede dejar a la empresa fuera de norma en
        // silencio. Si no entra, se dice exactamente que sobra y quien lo
        // tiene que resolver antes.
        var choques = await ChoquesAlCambiarDePlanAsync(empresaId, nuevo, ct);

        if (choques.Count > 0)
            return Resultado<EmpresaDetalle>.Error(TipoFallo.Conflicto,
                "El plan nuevo no alcanza para lo que la empresa ya tiene: " + string.Join("; ", choques),
                "PLAN_NO_ALCANZA");

        var anterior = empresa.PlanId;
        empresa.PlanId = nuevo.Id;
        await bd.SaveChangesAsync(ct);

        // El permiso efectivo depende del plan: si no se tira el cache, la
        // empresa sigue viendo lo del plan viejo hasta 30 segundos.
        permisos.Invalidar(empresaId);
        validador.Invalidar(empresaId: empresaId);

        await bitacora.RegistrarAsync("SA.EMPRESA.PLAN", "empresa", empresaId.ToString(), new
        {
            planAnterior = anterior,
            planNuevo = nuevo.Id,
            codigo = nuevo.Codigo,
            peticion.Motivo
        }, ct);

        return Resultado<EmpresaDetalle>.Ok((await ArmarDetalleAsync(empresaId, ct))!);
    }

    private async Task<List<string>> ChoquesAlCambiarDePlanAsync(
        int empresaId, Plan nuevo, CancellationToken ct)
    {
        var choques = new List<string>();

        var sedes = await bd.Sedes
            .IgnoreQueryFilters().CountAsync(s => s.EmpresaId == empresaId && s.Activo, ct);

        if (nuevo.MaxSedes > 0 && sedes > nuevo.MaxSedes)
            choques.Add($"tiene {sedes} sede(s) activa(s) y el plan permite {nuevo.MaxSedes}");

        var usados = await bd.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.EmpresaId == empresaId && u.Activo)
            .GroupBy(u => u.RolId)
            .Select(g => new { RolId = g.Key, Cuantos = g.Count() })
            .ToListAsync(ct);

        var nombres = await bd.Roles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Nombre, ct);

        foreach (var uso in usados.OrderBy(u => u.RolId))
        {
            var cupo = nuevo.Cupos.FirstOrDefault(c => c.RolId == uso.RolId);
            var rol = nombres.TryGetValue(uso.RolId, out var n) ? n : $"rol {uso.RolId}";

            if (cupo is null)
                choques.Add($"tiene {uso.Cuantos} {rol} y el plan no habilita ese rol");
            else if (cupo.Cupo > 0 && uso.Cuantos > cupo.Cupo)
                choques.Add($"tiene {uso.Cuantos} {rol} y el plan permite {cupo.Cupo}");
        }

        return choques;
    }

    public async Task<Resultado<EmpresaDetalle>> CambiarEstadoAsync(
        int empresaId, EstadoEmpresaPeticion peticion, CancellationToken ct = default)
    {
        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

        if (empresa is null)
            return Resultado<EmpresaDetalle>.Error(TipoFallo.NoEncontrado, "La empresa no existe.", "NO_ENCONTRADO");

        if (!peticion.Activo && string.IsNullOrWhiteSpace(peticion.Motivo))
            return Resultado<EmpresaDetalle>.Error(TipoFallo.Validacion,
                "Dar de baja una empresa exige un motivo: queda en la bitacora.", "MOTIVO_REQUERIDO");

        empresa.Activo = peticion.Activo;
        await bd.SaveChangesAsync(ct);

        // Dar de baja tiene que cortar YA. Sin esto, las sesiones abiertas
        // siguen trabajando hasta 30 segundos como si nada.
        validador.Invalidar(empresaId: empresaId);
        permisos.Invalidar(empresaId);

        await bitacora.RegistrarAsync(
            peticion.Activo ? "SA.EMPRESA.REACTIVACION" : "SA.EMPRESA.BAJA",
            "empresa", empresaId.ToString(), new { peticion.Motivo }, ct);

        return Resultado<EmpresaDetalle>.Ok((await ArmarDetalleAsync(empresaId, ct))!);
    }

    // -----------------------------------------------------------------------
    // Salud y cumplimiento
    // -----------------------------------------------------------------------

    public async Task<Resultado<SaludEmpresa>> SaludAsync(int empresaId, CancellationToken ct = default)
    {
        var hoy = Tiempo.HoyLima();

        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(e => e.Plan)
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

        if (empresa is null)
            return Resultado<SaludEmpresa>.Error(TipoFallo.NoEncontrado, "La empresa no existe.", "NO_ENCONTRADO");

        var sedes = await bd.Sedes
            .IgnoreQueryFilters().CountAsync(s => s.EmpresaId == empresaId && s.Activo, ct);

        var maxSedes = empresa.Plan?.MaxSedes ?? 0;

        var salud = new SaludEmpresa
        {
            EmpresaId = empresa.Id,
            Empresa = empresa.NombreComercial,
            ServicioHasta = empresa.ServicioHasta,
            DiasRestantes = empresa.ServicioHasta.DayNumber - hoy.DayNumber,
            EstadoServicio = EstadoServicio.Calcular(empresa.Activo, empresa.ServicioHasta, hoy),
            Plan = empresa.Plan?.Nombre ?? string.Empty,
            SedesUsadas = sedes,
            MaxSedes = maxSedes,
            SedesExcedidas = maxSedes > 0 && sedes > maxSedes,
            UsoPorRol = await UsoPorRolAsync(empresaId, empresa.PlanId, ct),
            Funciones = [.. (await permisos.FuncionesDelPlanAsync(empresaId, ct)).Order()]
        };

        salud.TieneAdmin = await bd.Usuarios
            .IgnoreQueryFilters()
            .AnyAsync(u => u.EmpresaId == empresaId && u.RolId == Roles.Admin && u.Activo, ct);

        salud.UltimoAccesoDeAlguien = await bd.Usuarios
            .IgnoreQueryFilters()
            .Where(u => u.EmpresaId == empresaId && u.UltimoAcceso != null)
            .MaxAsync(u => u.UltimoAcceso, ct);

        // Las alertas son frases, no codigos: esta pantalla la lee una
        // persona que tiene que decidir a quien llamar.
        if (!empresa.Activo)
            salud.Alertas.Add("La empresa esta dada de baja: nadie puede entrar.");

        if (empresa.Activo && salud.DiasRestantes < 0)
            salud.Alertas.Add($"El servicio vencio hace {-salud.DiasRestantes} dia(s): el acceso esta cortado.");
        else if (empresa.Activo && salud.DiasRestantes <= EstadoServicio.DiasDeAviso)
            salud.Alertas.Add($"El servicio vence en {salud.DiasRestantes} dia(s).");

        if (!salud.TieneAdmin)
            salud.Alertas.Add("No tiene ningun Admin activo: no hay quien administre la empresa.");

        if (salud.SedesExcedidas)
            salud.Alertas.Add($"Tiene {sedes} sedes activas y el plan permite {maxSedes}.");

        foreach (var uso in salud.UsoPorRol.Where(u => u.Excedido))
        {
            salud.Alertas.Add(uso.Cupo is null
                ? $"Tiene {uso.Usados} {uso.Rol} y el plan no habilita ese rol."
                : $"Tiene {uso.Usados} {uso.Rol} y el plan permite {uso.Cupo}.");
        }

        if (salud.UltimoAccesoDeAlguien is null)
            salud.Alertas.Add("Nadie de la empresa ha entrado nunca al sistema.");

        return Resultado<SaludEmpresa>.Ok(salud);
    }

    // -----------------------------------------------------------------------
    // Clave del Admin
    // -----------------------------------------------------------------------

    public async Task<Resultado<string>> ReiniciarClaveDelAdminAsync(
        int empresaId, CancellationToken ct = default)
    {
        var admin = await bd.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.EmpresaId == empresaId && u.RolId == Roles.Admin && u.Activo, ct);

        if (admin is null)
            return Resultado<string>.Error(TipoFallo.NoEncontrado,
                "La empresa no tiene un Admin activo.", "SIN_ADMIN");

        var clave = claves.NuevaClaveTemporal();

        admin.PasswordHash = claves.Hash(clave);
        admin.DebeCambiarClave = true;
        admin.IntentosFallidos = 0;
        admin.BloqueadoHasta = null;

        // Reiniciar la clave cierra las sesiones abiertas: si la pidieron
        // porque alguien perdio el acceso, lo que haya quedado abierto en
        // otro aparato tiene que caer tambien.
        admin.SesionVersion++;

        await bd.UsuarioRefrescos
            .IgnoreQueryFilters()
            .Where(r => r.UsuarioId == admin.Id && r.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, Tiempo.Ahora), ct);

        await bd.SaveChangesAsync(ct);
        validador.Invalidar(usuarioId: admin.Id);

        await bitacora.RegistrarAsync("SA.EMPRESA.CLAVE_ADMIN", "usuario", admin.Id.ToString(),
            new { empresaId, admin.Email }, ct);

        return Resultado<string>.Ok(clave);
    }
}
