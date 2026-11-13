using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// El tablero del taller. Se arma con conteos y sumas en la base, no trayendo
/// las ordenes a memoria: es la primera pantalla que abre el Admin cada dia.
/// El filtro de empresa de EF ya acota todo al token en curso.
/// </summary>
public class TableroTallerServicio(LubriTechContexto bd) : ITableroTallerServicio
{
    public async Task<TableroTaller> VerAsync(CancellationToken ct = default)
    {
        var inicioHoy = Tiempo.InicioDelDiaLima(Tiempo.HoyLima());
        var hoy = Tiempo.HoyLima();
        var inicioMes = Tiempo.InicioDelDiaLima(new DateOnly(hoy.Year, hoy.Month, 1));

        var ordenes = bd.Ordenes.AsNoTracking();

        var tablero = new TableroTaller
        {
            OrdenesAbiertas = await ordenes.CountAsync(o => !o.Estado!.EsFinal, ct),
            OrdenesHoy = await ordenes.CountAsync(o => o.CreadoEn >= inicioHoy, ct),
            CotizacionesPendientes = await bd.Cotizaciones.CountAsync(c => c.Estado == "ENVIADA", ct),
            ProductosBajoMinimo = await bd.Productos.CountAsync(p => p.ControlaStock
                && bd.ProductoStocks.Where(s => s.ProductoId == p.Id).Sum(s => (decimal?)s.Cantidad) < p.StockMinimo, ct),
            ValorTrabajosMes = await ordenes
                .Where(o => o.CreadoEn >= inicioMes && !o.Estado!.EsAnulacion)
                .SumAsync(o => (decimal?)o.Total, ct) ?? 0m,
        };

        // Ordenes por estado del flujo (en orden de secuencia), para la barra.
        var estados = await bd.OrdenEstados.AsNoTracking()
            .Where(e => !e.EsFinal && !e.EsAnulacion)
            .OrderBy(e => e.Secuencia).ToListAsync(ct);

        var conteos = (await ordenes.GroupBy(o => o.EstadoId)
            .Select(g => new { EstadoId = g.Key, Cantidad = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.EstadoId, x => x.Cantidad);

        tablero.PorEstado = estados.Select(e => new ConteoEstado
        {
            EstadoId = e.Id, Estado = e.Nombre, Secuencia = e.Secuencia,
            Cantidad = conteos.TryGetValue(e.Id, out var n) ? n : 0
        }).ToList();

        // Carga por tecnico: ordenes abiertas asignadas, de mayor a menor.
        var carga = await ordenes
            .Where(o => !o.Estado!.EsFinal && o.TecnicoId != null)
            .GroupBy(o => o.TecnicoId)
            .Select(g => new { TecnicoId = g.Key!.Value, Cantidad = g.Count() })
            .ToListAsync(ct);

        if (carga.Count > 0)
        {
            var ids = carga.Select(c => c.TecnicoId).ToList();
            var nombres = (await bd.Usuarios.AsNoTracking()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, Nombre = (u.Nombres + " " + u.Apellidos).Trim() })
                .ToListAsync(ct))
                .ToDictionary(x => x.Id, x => x.Nombre);

            tablero.CargaPorTecnico = carga
                .OrderByDescending(c => c.Cantidad)
                .Select(c => new CargaTecnico
                {
                    Tecnico = nombres.TryGetValue(c.TecnicoId, out var nom) ? nom : $"#{c.TecnicoId}",
                    Cantidad = c.Cantidad
                }).ToList();
        }

        tablero.UltimasOrdenes = await ordenes
            .OrderByDescending(o => o.Id).Take(6)
            .Select(o => new OrdenMini
            {
                Id = o.Id,
                Numero = o.Numero,
                Placa = o.Vehiculo!.Placa,
                Cliente = o.Cliente!.RazonSocial ?? (o.Cliente.Nombres + " " + o.Cliente.Apellidos),
                Estado = o.Estado!.Nombre,
                EstadoFinal = o.Estado.EsFinal,
                Total = o.Total,
                CreadoEn = o.CreadoEn
            }).ToListAsync(ct);

        foreach (var o in tablero.UltimasOrdenes) o.Cliente = (o.Cliente ?? "").Trim();

        return tablero;
    }
}
