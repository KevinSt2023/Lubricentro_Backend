using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Consola;

/// <summary>
/// El tablero del negocio del SaaS: cuantas empresas hay, cuantas estan por
/// vencer y cuanto entro este mes.
///
/// Se arma con conteos y sumas en la base, no trayendo las empresas a
/// memoria: esta pantalla es la primera que se abre cada manana y tiene que
/// seguir abriendo rapido cuando haya mil empresas.
/// </summary>
public class TableroSaaSServicio(LubriTechContexto bd) : ITableroSaaSServicio
{
    public async Task<TableroSaaS> VerAsync(CancellationToken ct = default)
    {
        var hoy = Tiempo.HoyLima();
        var aviso = hoy.AddDays(EstadoServicio.DiasDeAviso);

        var inicioMes = new DateOnly(hoy.Year, hoy.Month, 1);
        var inicioMesAnterior = inicioMes.AddMonths(-1);

        var empresas = bd.Empresas.IgnoreQueryFilters();

        var tablero = new TableroSaaS
        {
            Empresas = await empresas.CountAsync(ct),
            Activas = await empresas.CountAsync(e => e.Activo, ct),
            Inactivas = await empresas.CountAsync(e => !e.Activo, ct),
            Vencidas = await empresas.CountAsync(e => e.Activo && e.ServicioHasta < hoy, ct),
            PorVencer = await empresas.CountAsync(e => e.Activo
                && e.ServicioHasta >= hoy && e.ServicioHasta <= aviso, ct),
            AlDia = await empresas.CountAsync(e => e.Activo && e.ServicioHasta > aviso, ct)
        };

        // Ingresos por fecha de REGISTRO del pago, que es cuando entro la
        // plata, no por el periodo que cubre.
        var pagos = bd.Suscripciones.IgnoreQueryFilters().Where(s => s.Estado == "PAGADA");

        tablero.IngresoMesActual = await pagos
            .Where(s => s.RegistradoEn >= Inicio(inicioMes) && s.RegistradoEn < Inicio(inicioMes.AddMonths(1)))
            .SumAsync(s => (decimal?)s.Monto, ct) ?? 0;

        tablero.IngresoMesAnterior = await pagos
            .Where(s => s.RegistradoEn >= Inicio(inicioMesAnterior) && s.RegistradoEn < Inicio(inicioMes))
            .SumAsync(s => (decimal?)s.Monto, ct) ?? 0;

        // Lo que entraria en un mes si todas las empresas al dia renovaran:
        // el numero con el que se mira el negocio, no la caja del mes.
        tablero.IngresoRecurrenteMensual = await empresas
            .Where(e => e.Activo && e.ServicioHasta >= hoy)
            .SumAsync(e => (decimal?)e.Plan!.PrecioMensual, ct) ?? 0;

        tablero.PorPlan = await bd.Planes
            .AsNoTracking()
            .OrderBy(p => p.Orden)
            .Select(p => new ConteoPorPlan
            {
                PlanId = p.Id,
                Plan = p.Nombre,
                PrecioMensual = p.PrecioMensual,
                Empresas = bd.Empresas.IgnoreQueryFilters().Count(e => e.PlanId == p.Id)
            })
            .ToListAsync(ct);

        // A quien hay que llamar hoy: las que vencen primero, y las vencidas
        // antes que nada (dias restantes negativos ordenan primero).
        tablero.PorVencerPronto = await empresas
            .AsNoTracking()
            .Where(e => e.Activo && e.ServicioHasta <= aviso)
            .OrderBy(e => e.ServicioHasta)
            .Take(10)
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
                CreadoEn = e.CreadoEn
            })
            .ToListAsync(ct);

        foreach (var item in tablero.PorVencerPronto)
        {
            item.DiasRestantes = item.ServicioHasta.DayNumber - hoy.DayNumber;
            item.EstadoServicio = EstadoServicio.Calcular(item.Activo, item.ServicioHasta, hoy);
        }

        tablero.UltimosPagos = await bd.Suscripciones
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderByDescending(s => s.RegistradoEn)
            .Take(10)
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

        foreach (var pago in tablero.UltimosPagos)
            pago.Dias = pago.Hasta.DayNumber - pago.Desde.DayNumber + 1;

        return tablero;
    }

    /// <summary>
    /// El instante UTC en que empieza ese dia en Lima. Comparar un
    /// timestamptz contra una fecha pelada corre el corte cinco horas.
    /// </summary>
    private static DateTimeOffset Inicio(DateOnly dia) => Tiempo.InicioDelDiaLima(dia);
}
