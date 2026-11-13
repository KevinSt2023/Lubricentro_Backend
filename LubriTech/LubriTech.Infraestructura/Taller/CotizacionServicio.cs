using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// Cotizaciones y aprobaciones (Fase 4). Lo que cuida el codigo: armar la
/// cotizacion y sus renglones, enviarla reemplazando la version anterior, y
/// pasar los servicios aprobados a la orden. Lo que cuida la base: la version
/// correlativa, que una cotizacion enviada no se edite, que solo la decision
/// del cliente la apruebe, y que el renglon no pase a ejecucion sin aprobacion.
/// </summary>
public class CotizacionServicio(
    LubriTechContexto bd,
    IBitacoraServicio bitacora) : ICotizacionServicio
{
    private static readonly string[] Severidades = ["LEVE", "MODERADO", "CRITICO"];

    // ============================ Hallazgos ================================

    public async Task<Resultado<List<HallazgoItem>>> HallazgosAsync(int empresaId, long ordenId, CancellationToken ct = default)
    {
        if (!await bd.Ordenes.AnyAsync(o => o.Id == ordenId, ct))
            return Resultado<List<HallazgoItem>>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        var items = await bd.OrdenHallazgos.AsNoTracking()
            .Where(h => h.OrdenId == ordenId).OrderByDescending(h => h.Id)
            .Select(h => new HallazgoItem
            {
                Id = h.Id, Descripcion = h.Descripcion, Severidad = h.Severidad, Atendido = h.Atendido,
                Autor = h.Autor != null ? h.Autor.Nombres + " " + h.Autor.Apellidos : null,
                CreadoEn = h.CreadoEn
            }).ToListAsync(ct);

        return Resultado<List<HallazgoItem>>.Ok(items);
    }

    public async Task<Resultado<HallazgoItem>> RegistrarHallazgoAsync(
        int empresaId, int? usuarioId, long ordenId, RegistrarHallazgoPeticion peticion, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<HallazgoItem>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");
        if (orden.Estado!.EsFinal)
            return Resultado<HallazgoItem>.Error(TipoFallo.Conflicto, "Una orden cerrada no recibe hallazgos.", "ORDEN_FINAL");

        var severidad = (peticion.Severidad ?? "MODERADO").Trim().ToUpperInvariant();
        if (!Severidades.Contains(severidad))
            return Resultado<HallazgoItem>.Error(TipoFallo.Validacion, "La severidad va LEVE / MODERADO / CRITICO.", "SEVERIDAD");

        var h = new OrdenHallazgo
        {
            EmpresaId = empresaId, OrdenId = ordenId,
            Descripcion = peticion.Descripcion.Trim(), Severidad = severidad, CreadoPor = usuarioId
        };
        bd.OrdenHallazgos.Add(h);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("COTIZACION.HALLAZGO", "orden", ordenId.ToString(),
            new { orden.Numero, severidad }, ct);

        return Resultado<HallazgoItem>.Ok(new HallazgoItem
        {
            Id = h.Id, Descripcion = h.Descripcion, Severidad = h.Severidad, Atendido = h.Atendido, CreadoEn = h.CreadoEn
        });
    }

    public async Task<Resultado> QuitarHallazgoAsync(int empresaId, long ordenId, long hallazgoId, CancellationToken ct = default)
    {
        var h = await bd.OrdenHallazgos.FirstOrDefaultAsync(x => x.Id == hallazgoId && x.OrdenId == ordenId, ct);
        if (h is null)
            return Resultado.Error(TipoFallo.NoEncontrado, "El hallazgo no existe.", "NO_ENCONTRADO");
        if (h.Atendido)
            return Resultado.Error(TipoFallo.Conflicto, "Un hallazgo ya cotizado no se quita.", "HALLAZGO_ATENDIDO");

        bd.OrdenHallazgos.Remove(h);
        await bd.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    // ======================= Cotizacion (asesor) ===========================

    public async Task<Resultado<List<CotizacionItem>>> ListarPorOrdenAsync(int empresaId, long ordenId, CancellationToken ct = default)
    {
        if (!await bd.Ordenes.AnyAsync(o => o.Id == ordenId, ct))
            return Resultado<List<CotizacionItem>>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        var items = await bd.Cotizaciones.AsNoTracking()
            .Where(c => c.OrdenId == ordenId).OrderByDescending(c => c.Version)
            .Select(c => Resumen(c, c.Orden!.Numero)).ToListAsync(ct);

        return Resultado<List<CotizacionItem>>.Ok(items);
    }

    public async Task<Resultado<CotizacionDetalle>> VerAsync(int empresaId, long cotizacionId, CancellationToken ct = default)
    {
        var cot = await bd.Cotizaciones.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cotizacionId, ct);
        return cot is null
            ? Resultado<CotizacionDetalle>.Error(TipoFallo.NoEncontrado, "La cotizacion no existe.", "NO_ENCONTRADO")
            : Resultado<CotizacionDetalle>.Ok(await DetalleAsync(cotizacionId, ct));
    }

    public async Task<Resultado<CotizacionDetalle>> CrearAsync(
        int empresaId, int? usuarioId, long ordenId, GuardarCotizacionPeticion peticion, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<CotizacionDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");
        if (orden.Estado!.EsFinal)
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Conflicto, "Una orden cerrada no se cotiza.", "ORDEN_FINAL");

        var armado = await ArmarRenglonesAsync(empresaId, peticion.Renglones, ct);
        if (!armado.Exito)
            return Resultado<CotizacionDetalle>.Error(armado.Fallo, armado.Mensaje!, armado.Codigo);

        var cot = new Cotizacion
        {
            EmpresaId = empresaId, OrdenId = ordenId, Estado = "BORRADOR",
            Nota = Limpiar(peticion.Nota), VigenciaHasta = peticion.VigenciaHasta,
            CreadoPor = usuarioId, Total = armado.Valor!.Sum(r => Redondear(r.Cantidad * r.PrecioUnitario))
        };
        bd.Cotizaciones.Add(cot);
        await bd.SaveChangesAsync(ct);   // el trigger asigna la version

        foreach (var r in armado.Valor!) { r.CotizacionId = cot.Id; r.EmpresaId = empresaId; bd.CotizacionRenglones.Add(r); }
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("COTIZACION.ALTA", "cotizacion", cot.Id.ToString(),
            new { orden.Numero, version = cot.Version, cot.Total }, ct);

        return Resultado<CotizacionDetalle>.Ok(await DetalleAsync(cot.Id, ct));
    }

    public async Task<Resultado<CotizacionDetalle>> ActualizarAsync(
        int empresaId, long cotizacionId, GuardarCotizacionPeticion peticion, CancellationToken ct = default)
    {
        var cot = await bd.Cotizaciones.FirstOrDefaultAsync(c => c.Id == cotizacionId, ct);
        if (cot is null)
            return Resultado<CotizacionDetalle>.Error(TipoFallo.NoEncontrado, "La cotizacion no existe.", "NO_ENCONTRADO");
        if (cot.Estado != "BORRADOR")
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Conflicto,
                "Solo se edita una cotizacion en borrador. Arma una version nueva.", "COTIZACION_CONGELADA");

        var armado = await ArmarRenglonesAsync(empresaId, peticion.Renglones, ct);
        if (!armado.Exito)
            return Resultado<CotizacionDetalle>.Error(armado.Fallo, armado.Mensaje!, armado.Codigo);

        var previos = bd.CotizacionRenglones.Where(r => r.CotizacionId == cotizacionId);
        bd.CotizacionRenglones.RemoveRange(previos);
        foreach (var r in armado.Valor!) { r.CotizacionId = cotizacionId; r.EmpresaId = empresaId; bd.CotizacionRenglones.Add(r); }

        cot.Nota = Limpiar(peticion.Nota);
        cot.VigenciaHasta = peticion.VigenciaHasta;
        cot.Total = armado.Valor!.Sum(r => Redondear(r.Cantidad * r.PrecioUnitario));
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("COTIZACION.EDICION", "cotizacion", cotizacionId.ToString(),
            new { version = cot.Version, cot.Total }, ct);

        return Resultado<CotizacionDetalle>.Ok(await DetalleAsync(cotizacionId, ct));
    }

    public async Task<Resultado<CotizacionDetalle>> EnviarAsync(
        int empresaId, int? usuarioId, long cotizacionId, CancellationToken ct = default)
    {
        var cot = await bd.Cotizaciones.FirstOrDefaultAsync(c => c.Id == cotizacionId, ct);
        if (cot is null)
            return Resultado<CotizacionDetalle>.Error(TipoFallo.NoEncontrado, "La cotizacion no existe.", "NO_ENCONTRADO");
        if (cot.Estado != "BORRADOR")
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Conflicto, "Solo se envia una cotizacion en borrador.", "COTIZACION_CONGELADA");

        if (!await bd.CotizacionRenglones.AnyAsync(r => r.CotizacionId == cotizacionId, ct))
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Validacion, "Una cotizacion vacia no se envia.", "SIN_RENGLONES");

        // Reemplazar la version pendiente anterior, si la hay, antes de enviar
        // esta: el indice unico solo deja una ENVIADA por orden a la vez.
        await bd.EnTransaccionAsync(async token =>
        {
            var pendientes = await bd.Cotizaciones
                .Where(c => c.OrdenId == cot.OrdenId && c.Estado == "ENVIADA" && c.Id != cotizacionId)
                .ToListAsync(token);
            foreach (var p in pendientes) p.Estado = "REEMPLAZADA";
            if (pendientes.Count > 0) await bd.SaveChangesAsync(token);

            cot.Estado = "ENVIADA";
            cot.EnviadaEn = Tiempo.Ahora;
            await bd.SaveChangesAsync(token);
        }, ct);

        await bitacora.RegistrarAsync("COTIZACION.ENVIO", "cotizacion", cotizacionId.ToString(),
            new { version = cot.Version, cot.Total }, ct);

        return Resultado<CotizacionDetalle>.Ok(await DetalleAsync(cotizacionId, ct));
    }

    public async Task<Resultado<CotizacionDetalle>> AplicarAsync(
        int empresaId, int? usuarioId, long cotizacionId, CancellationToken ct = default)
    {
        var cot = await bd.Cotizaciones.FirstOrDefaultAsync(c => c.Id == cotizacionId, ct);
        if (cot is null)
            return Resultado<CotizacionDetalle>.Error(TipoFallo.NoEncontrado, "La cotizacion no existe.", "NO_ENCONTRADO");
        if (cot.Estado != "APROBADA")
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Conflicto,
                "Solo se aplica una cotizacion aprobada por el cliente.", "NO_APROBADA");

        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == cot.OrdenId, ct);
        if (orden is null || orden.Estado!.EsFinal)
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Conflicto, "La orden ya no admite renglones.", "ORDEN_FINAL");

        // Los renglones de servicio aprobados pasan a la orden como trabajo a
        // ejecutar. Los repuestos quedan autorizados: se consumen en ejecucion
        // por el flujo de kardex. Idempotente: no se vuelve a aplicar lo ya puesto.
        var yaAplicados = await bd.OrdenServicios
            .Where(s => s.OrdenId == cot.OrdenId && s.CotizacionRenglonId != null)
            .Select(s => s.CotizacionRenglonId!.Value).ToListAsync(ct);

        var renglones = await bd.CotizacionRenglones
            .Where(r => r.CotizacionId == cotizacionId && r.Tipo == "SERVICIO" && !yaAplicados.Contains(r.Id))
            .ToListAsync(ct);

        if (renglones.Count == 0)
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Conflicto,
                "No hay servicios cotizados pendientes de pasar a la orden.", "NADA_QUE_APLICAR");

        foreach (var r in renglones)
            bd.OrdenServicios.Add(new OrdenServicio
            {
                EmpresaId = empresaId, OrdenId = cot.OrdenId, ServicioId = r.ServicioId,
                Descripcion = r.Descripcion, Precio = r.PrecioUnitario, Cantidad = r.Cantidad,
                CotizacionRenglonId = r.Id
            });
        await bd.SaveChangesAsync(ct);

        await RecalcularTotalesAsync(cot.OrdenId, ct);

        await bitacora.RegistrarAsync("COTIZACION.APLICA", "cotizacion", cotizacionId.ToString(),
            new { orden.Numero, version = cot.Version, servicios = renglones.Count }, ct);

        return Resultado<CotizacionDetalle>.Ok(await DetalleAsync(cotizacionId, ct));
    }

    // ========================= Portal del cliente ==========================

    public Task<List<CotizacionItem>> PortalListarAsync(int empresaId, long clienteId, CancellationToken ct = default) =>
        bd.Cotizaciones.AsNoTracking()
          .Where(c => c.Orden!.Vehiculo!.ClienteId == clienteId
                      && (c.Estado == "ENVIADA" || c.Estado == "APROBADA" || c.Estado == "RECHAZADA"))
          .OrderByDescending(c => c.Id)
          .Select(c => Resumen(c, c.Orden!.Numero)).ToListAsync(ct);

    public async Task<Resultado<CotizacionDetalle>> PortalVerAsync(
        int empresaId, long clienteId, long cotizacionId, CancellationToken ct = default)
    {
        if (!await EsDelClienteAsync(cotizacionId, clienteId, ct))
            return Resultado<CotizacionDetalle>.Error(TipoFallo.NoEncontrado, "La cotizacion no existe.", "NO_ENCONTRADO");

        return Resultado<CotizacionDetalle>.Ok(await DetalleAsync(cotizacionId, ct));
    }

    public async Task<Resultado<CotizacionDetalle>> DecidirAsync(
        int empresaId, long clienteId, long cotizacionId, DecidirCotizacionPeticion peticion,
        string? ip, CancellationToken ct = default)
    {
        var decision = (peticion.Decision ?? "").Trim().ToUpperInvariant();
        if (decision is not ("APROBADA" or "RECHAZADA"))
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Validacion, "La decision va APROBADA o RECHAZADA.", "DECISION");

        var cot = await bd.Cotizaciones.FirstOrDefaultAsync(c => c.Id == cotizacionId, ct);
        if (cot is null || !await EsDelClienteAsync(cotizacionId, clienteId, ct))
            return Resultado<CotizacionDetalle>.Error(TipoFallo.NoEncontrado, "La cotizacion no existe.", "NO_ENCONTRADO");

        if (cot.Estado != "ENVIADA")
            return Resultado<CotizacionDetalle>.Error(TipoFallo.Conflicto,
                "Esta cotizacion ya no espera una decision.", "NO_PENDIENTE");

        // La fila de decision es la que, por trigger, pone el estado de la
        // cotizacion. Es inmutable: aqui solo se inserta.
        bd.CotizacionDecisiones.Add(new CotizacionDecision
        {
            EmpresaId = empresaId, CotizacionId = cotizacionId, Decision = decision,
            ClienteId = clienteId, Comentario = Limpiar(peticion.Comentario), Canal = "PORTAL", Ip = ip
        });
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("COTIZACION.DECISION", "cotizacion", cotizacionId.ToString(),
            new { version = cot.Version, decision }, ct);

        return Resultado<CotizacionDetalle>.Ok(await DetalleAsync(cotizacionId, ct));
    }

    // ============================== Helpers ================================

    private Task<bool> EsDelClienteAsync(long cotizacionId, long clienteId, CancellationToken ct) =>
        bd.Cotizaciones.AnyAsync(c => c.Id == cotizacionId && c.Orden!.Vehiculo!.ClienteId == clienteId, ct);

    /// <summary>
    /// Valida y arma los renglones a partir de la peticion. Un renglon de
    /// catalogo (servicio o producto) toma su descripcion y precio de ahi si
    /// no vienen; uno suelto necesita descripcion y precio.
    /// </summary>
    private async Task<Resultado<List<CotizacionRenglon>>> ArmarRenglonesAsync(
        int empresaId, List<RenglonCotizacionPeticion> peticiones, CancellationToken ct)
    {
        if (peticiones.Count == 0)
            return Resultado<List<CotizacionRenglon>>.Error(TipoFallo.Validacion, "La cotizacion necesita al menos un renglon.", "SIN_RENGLONES");

        var lista = new List<CotizacionRenglon>();
        foreach (var p in peticiones)
        {
            var tipo = (p.Tipo ?? "").Trim().ToUpperInvariant();
            if (tipo is not ("SERVICIO" or "REPUESTO"))
                return Resultado<List<CotizacionRenglon>>.Error(TipoFallo.Validacion, "El tipo de renglon va SERVICIO o REPUESTO.", "TIPO_RENGLON");
            if (p.Cantidad <= 0)
                return Resultado<List<CotizacionRenglon>>.Error(TipoFallo.Validacion, "La cantidad tiene que ser mayor que cero.", "CANTIDAD_INVALIDA");

            string descripcion = Limpiar(p.Descripcion) ?? "";
            decimal precio = p.PrecioUnitario ?? -1;

            if (tipo == "SERVICIO" && p.ServicioId.HasValue)
            {
                var srv = await bd.Servicios.FirstOrDefaultAsync(s => s.Id == p.ServicioId.Value, ct);
                if (srv is null)
                    return Resultado<List<CotizacionRenglon>>.Error(TipoFallo.Validacion, "El servicio no existe.", "SERVICIO_INVALIDO");
                if (descripcion == "") descripcion = srv.Nombre;
                if (precio < 0) precio = srv.Precio;
            }
            else if (tipo == "REPUESTO" && p.ProductoId.HasValue)
            {
                var prod = await bd.Productos.FirstOrDefaultAsync(x => x.Id == p.ProductoId.Value, ct);
                if (prod is null)
                    return Resultado<List<CotizacionRenglon>>.Error(TipoFallo.Validacion, "El producto no existe.", "PRODUCTO_INVALIDO");
                if (descripcion == "") descripcion = prod.Nombre;
                if (precio < 0) precio = prod.PrecioVenta;
            }

            if (descripcion == "")
                return Resultado<List<CotizacionRenglon>>.Error(TipoFallo.Validacion, "Cada renglon necesita una descripcion.", "DESCRIPCION");
            if (precio < 0)
                return Resultado<List<CotizacionRenglon>>.Error(TipoFallo.Validacion, "Cada renglon necesita un precio.", "PRECIO");

            lista.Add(new CotizacionRenglon
            {
                Tipo = tipo,
                ServicioId = tipo == "SERVICIO" ? p.ServicioId : null,
                ProductoId = tipo == "REPUESTO" ? p.ProductoId : null,
                Descripcion = descripcion, Cantidad = p.Cantidad, PrecioUnitario = precio
            });
        }

        return Resultado<List<CotizacionRenglon>>.Ok(lista);
    }

    private async Task RecalcularTotalesAsync(long ordenId, CancellationToken ct)
    {
        var orden = await bd.Ordenes.FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null) return;
        orden.TotalServicios = await bd.OrdenServicios
            .Where(s => s.OrdenId == ordenId).SumAsync(s => (decimal?)(s.Precio * s.Cantidad), ct) ?? 0m;
        orden.TotalRepuestos = await bd.OrdenConsumos
            .Where(c => c.OrdenId == ordenId).SumAsync(c => (decimal?)(c.PrecioVenta * c.Cantidad), ct) ?? 0m;
        orden.Total = orden.TotalServicios + orden.TotalRepuestos;
        await bd.SaveChangesAsync(ct);
    }

    private async Task<CotizacionDetalle> DetalleAsync(long cotizacionId, CancellationToken ct)
    {
        var c = await bd.Cotizaciones.AsNoTracking()
            .Where(x => x.Id == cotizacionId)
            .Select(x => new
            {
                Cot = x,
                NumeroOrden = x.Orden!.Numero,
                Placa = x.Orden.Vehiculo!.Placa,
                Vehiculo = x.Orden.Vehiculo.Marca + " " + x.Orden.Vehiculo.Modelo,
                Cliente = x.Orden.Cliente!.RazonSocial ?? (x.Orden.Cliente.Nombres + " " + x.Orden.Cliente.Apellidos),
                Autor = x.Autor != null ? x.Autor.Nombres + " " + x.Autor.Apellidos : null
            }).FirstAsync(ct);

        var renglones = await bd.CotizacionRenglones.AsNoTracking()
            .Where(r => r.CotizacionId == cotizacionId).OrderBy(r => r.Id)
            .Select(r => new CotizacionRenglonItem
            {
                Id = r.Id, Tipo = r.Tipo, ServicioId = r.ServicioId, ProductoId = r.ProductoId,
                Descripcion = r.Descripcion, Cantidad = r.Cantidad, PrecioUnitario = r.PrecioUnitario, Subtotal = r.Subtotal
            }).ToListAsync(ct);

        var decision = await bd.CotizacionDecisiones.AsNoTracking()
            .Where(d => d.CotizacionId == cotizacionId)
            .Select(d => new CotizacionDecisionItem
            {
                Decision = d.Decision, Comentario = d.Comentario, Canal = d.Canal, CreadoEn = d.CreadoEn
            }).FirstOrDefaultAsync(ct);

        var d = new CotizacionDetalle
        {
            Id = c.Cot.Id, OrdenId = c.Cot.OrdenId, NumeroOrden = c.NumeroOrden, Version = c.Cot.Version,
            Estado = c.Cot.Estado, Total = c.Cot.Total, Moneda = c.Cot.Moneda, VigenciaHasta = c.Cot.VigenciaHasta,
            EnviadaEn = c.Cot.EnviadaEn, DecididaEn = c.Cot.DecididaEn, CreadoEn = c.Cot.CreadoEn,
            Nota = c.Cot.Nota, Autor = c.Autor?.Trim(),
            Placa = c.Placa, Vehiculo = c.Vehiculo, Cliente = (c.Cliente ?? "").Trim(),
            Renglones = renglones, Decision = decision
        };
        return d;
    }

    private static CotizacionItem Resumen(Cotizacion c, int numeroOrden) => new()
    {
        Id = c.Id, OrdenId = c.OrdenId, NumeroOrden = numeroOrden, Version = c.Version,
        Estado = c.Estado, Total = c.Total, Moneda = c.Moneda, VigenciaHasta = c.VigenciaHasta,
        EnviadaEn = c.EnviadaEn, DecididaEn = c.DecididaEn, CreadoEn = c.CreadoEn
    };

    private static decimal Redondear(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
