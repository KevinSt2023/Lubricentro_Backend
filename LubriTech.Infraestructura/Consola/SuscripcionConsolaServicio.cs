using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Consola;

/// <summary>
/// El cobro del servicio.
///
/// REGLA DE ORO: ningun dia de servicio se regala ni se cobra dos veces.
/// De ahi sale todo lo demas:
///   * un periodo nuevo arranca el dia SIGUIENTE al ultimo pagado, no hoy,
///     para no tirar a la basura los dias que la empresa ya pago;
///   * si el servicio ya habia vencido, arranca HOY, no retroactivo: no se
///     cobran dias que nadie uso;
///   * dos periodos pagados de la misma empresa no pueden solaparse, y eso
///     lo impide la base (ex_suscripcion_sin_solape), no solo este codigo.
/// </summary>
public class SuscripcionConsolaServicio(
    LubriTechContexto bd,
    IValidadorSesion validador,
    IBitacoraServicio bitacora) : ISuscripcionConsolaServicio
{
    private const int MesesMaximos = 36;

    public async Task<Pagina<SuscripcionItem>> ListarAsync(
        ConsultaPaginada consulta, int? empresaId = null, CancellationToken ct = default)
    {
        var origen = bd.Suscripciones.IgnoreQueryFilters().AsNoTracking();

        if (empresaId.HasValue) origen = origen.Where(s => s.EmpresaId == empresaId.Value);

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var texto = consulta.Buscar.Trim().ToLower();
            origen = origen.Where(s =>
                (s.Referencia != null && s.Referencia.ToLower().Contains(texto)) ||
                bd.Empresas.IgnoreQueryFilters().Any(e => e.Id == s.EmpresaId &&
                    (e.NombreComercial.ToLower().Contains(texto) || e.Ruc.Contains(texto))));
        }

        var total = await origen.CountAsync(ct);

        var items = await origen
            .OrderByDescending(s => s.RegistradoEn)
            .Skip(consulta.Saltar)
            .Take(consulta.Tamano)
            .Select(s => new SuscripcionItem
            {
                Id = s.Id,
                EmpresaId = s.EmpresaId,
                Empresa = bd.Empresas.IgnoreQueryFilters()
                            .Where(e => e.Id == s.EmpresaId)
                            .Select(e => e.NombreComercial).FirstOrDefault(),
                PlanId = s.PlanId,
                Plan = bd.Planes.Where(p => p.Id == s.PlanId).Select(p => p.Nombre).FirstOrDefault(),
                Desde = s.Desde,
                Hasta = s.Hasta,
                Monto = s.Monto,
                Estado = s.Estado,
                Referencia = s.Referencia,
                RegistradoEn = s.RegistradoEn
            })
            .ToListAsync(ct);

        foreach (var item in items) item.Dias = item.Hasta.DayNumber - item.Desde.DayNumber + 1;

        return Pagina<SuscripcionItem>.De(items, consulta, total);
    }

    public async Task<Resultado<SuscripcionItem>> RegistrarAsync(
        int empresaId, RegistrarSuscripcionPeticion peticion, CancellationToken ct = default)
    {
        if (peticion.Meses is < 1 or > MesesMaximos)
            return Resultado<SuscripcionItem>.Error(TipoFallo.Validacion,
                $"Los meses a pagar van de 1 a {MesesMaximos}.", "MESES_INVALIDOS");

        if (peticion.Monto is < 0)
            return Resultado<SuscripcionItem>.Error(TipoFallo.Validacion,
                "El monto no puede ser negativo.", "MONTO_INVALIDO");

        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .Include(e => e.Plan)
            .FirstOrDefaultAsync(e => e.Id == empresaId, ct);

        if (empresa is null)
            return Resultado<SuscripcionItem>.Error(TipoFallo.NoEncontrado,
                "La empresa no existe.", "NO_ENCONTRADO");

        var hoy = Tiempo.HoyLima();

        // Aqui vive la regla de oro.
        var desde = empresa.ServicioHasta >= hoy
            ? empresa.ServicioHasta.AddDays(1)   // encadena: no se pierde lo pagado
            : hoy;                               // vencido: arranca hoy, sin cobrar lo no usado

        var hasta = desde.AddMonths(peticion.Meses).AddDays(-1);

        var monto = peticion.Monto ?? (empresa.Plan?.PrecioMensual ?? 0) * peticion.Meses;

        var suscripcion = new Suscripcion
        {
            EmpresaId = empresa.Id,
            PlanId = empresa.PlanId,
            Desde = desde,
            Hasta = hasta,
            Monto = monto,
            Estado = "PAGADA",
            Referencia = string.IsNullOrWhiteSpace(peticion.Referencia)
                ? null
                : peticion.Referencia.Trim()
        };

        // El pago y la extension del servicio van juntos o no van: cobrar y
        // no extender es lo peor que puede pasarle a un cliente.
        await bd.EnTransaccionAsync(async token =>
        {
            bd.Suscripciones.Add(suscripcion);

            // El servicio llega hasta donde llega el ultimo periodo pagado.
            empresa.ServicioHasta = hasta;

            await bd.SaveChangesAsync(token);
        }, ct);

        // Si estaba vencida y el pago la pone al dia, el candado de sesion
        // tiene que enterarse ya: si no, el cliente paga y sigue sin entrar
        // hasta que expire el cache.

        validador.Invalidar(empresaId: empresaId);

        await bitacora.RegistrarAsync("SA.SUSCRIPCION.PAGO", "suscripcion", suscripcion.Id.ToString(), new
        {
            empresaId,
            empresa.Ruc,
            peticion.Meses,
            desde,
            hasta,
            monto,
            suscripcion.Referencia
        }, ct);

        return Resultado<SuscripcionItem>.Ok(new SuscripcionItem
        {
            Id = suscripcion.Id,
            EmpresaId = empresa.Id,
            Empresa = empresa.NombreComercial,
            PlanId = empresa.PlanId,
            Plan = empresa.Plan?.Nombre,
            Desde = desde,
            Hasta = hasta,
            Monto = monto,
            Estado = suscripcion.Estado,
            Referencia = suscripcion.Referencia,
            RegistradoEn = suscripcion.RegistradoEn,
            Dias = hasta.DayNumber - desde.DayNumber + 1
        });
    }

    public async Task<Resultado<SuscripcionItem>> AnularAsync(
        long suscripcionId, AnularSuscripcionPeticion peticion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(peticion.Motivo))
            return Resultado<SuscripcionItem>.Error(TipoFallo.Validacion,
                "Anular un pago exige un motivo: queda en la bitacora.", "MOTIVO_REQUERIDO");

        var suscripcion = await bd.Suscripciones
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == suscripcionId, ct);

        if (suscripcion is null)
            return Resultado<SuscripcionItem>.Error(TipoFallo.NoEncontrado,
                "La suscripcion no existe.", "NO_ENCONTRADO");

        if (suscripcion.Estado == "ANULADA")
            return Resultado<SuscripcionItem>.Error(TipoFallo.Conflicto,
                "Esa suscripcion ya estaba anulada.", "YA_ANULADA");

        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .Include(e => e.Plan)
            .FirstAsync(e => e.Id == suscripcion.EmpresaId, ct);

        await bd.EnTransaccionAsync(async token =>
        {
            suscripcion.Estado = "ANULADA";
            await bd.SaveChangesAsync(token);

            // El servicio se recalcula desde los pagos que QUEDAN. Restar los
            // dias del anulado no sirve: los periodos pueden no ser el ultimo.
            var ultimoPagado = await bd.Suscripciones
                .IgnoreQueryFilters()
                .Where(s => s.EmpresaId == empresa.Id && s.Estado == "PAGADA")
                .MaxAsync(s => (DateOnly?)s.Hasta, token);

            // Sin ningun pago vigente, el servicio queda vencido ayer: no se
            // regala el dia de hoy por haber anulado.
            empresa.ServicioHasta = ultimoPagado ?? Tiempo.HoyLima().AddDays(-1);

            await bd.SaveChangesAsync(token);
        }, ct);

        validador.Invalidar(empresaId: empresa.Id);

        await bitacora.RegistrarAsync("SA.SUSCRIPCION.ANULACION", "suscripcion",
            suscripcionId.ToString(), new
            {
                empresaId = empresa.Id,
                suscripcion.Desde,
                suscripcion.Hasta,
                suscripcion.Monto,
                peticion.Motivo,
                servicioHastaRecalculado = empresa.ServicioHasta
            }, ct);

        return Resultado<SuscripcionItem>.Ok(new SuscripcionItem
        {
            Id = suscripcion.Id,
            EmpresaId = empresa.Id,
            Empresa = empresa.NombreComercial,
            PlanId = suscripcion.PlanId,
            Plan = empresa.Plan?.Nombre,
            Desde = suscripcion.Desde,
            Hasta = suscripcion.Hasta,
            Monto = suscripcion.Monto,
            Estado = suscripcion.Estado,
            Referencia = suscripcion.Referencia,
            RegistradoEn = suscripcion.RegistradoEn,
            Dias = suscripcion.Hasta.DayNumber - suscripcion.Desde.DayNumber + 1
        });
    }
}
