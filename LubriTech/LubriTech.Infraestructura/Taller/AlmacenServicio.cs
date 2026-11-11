using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// El almacen: productos (repuestos e insumos), stock por sede y kardex.
///
/// REGLA DE ORO de esta fase: el stock NO se escribe a mano. Todo movimiento
/// es una fila de kardex, y el trigger de la base (fn_kardex_aplica) actualiza
/// producto_stock y el costo promedio en la misma operacion. Este servicio
/// nunca toca producto_stock; solo inserta kardex y vuelve a leer la foto.
/// </summary>
public class AlmacenServicio(
    LubriTechContexto bd,
    IBitacoraServicio bitacora) : IAlmacenServicio
{
    public async Task<Pagina<ProductoItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        string? tipo = null, bool? activo = null, bool soloBajoMinimo = false, CancellationToken ct = default)
    {
        var q = bd.Productos.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(tipo)) q = q.Where(p => p.Tipo == tipo);
        if (activo.HasValue) q = q.Where(p => p.Activo == activo.Value);

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var b = consulta.Buscar.Trim().ToLower();
            q = q.Where(p => p.Nombre.ToLower().Contains(b) ||
                             p.Codigo.ToLower().Contains(b) ||
                             (p.CodigoBarras != null && p.CodigoBarras.ToLower().Contains(b)) ||
                             (p.Marca != null && p.Marca.ToLower().Contains(b)));
        }

        var proyectada = q.Select(p => new
        {
            P = p,
            Stock = bd.ProductoStocks.Where(s => s.ProductoId == p.Id).Sum(s => (decimal?)s.Cantidad) ?? 0m
        });

        // "Bajo minimo" solo tiene sentido en los que controlan stock.
        if (soloBajoMinimo)
            proyectada = proyectada.Where(x => x.P.ControlaStock && x.Stock < x.P.StockMinimo);

        var total = await proyectada.CountAsync(ct);
        var filas = await proyectada
            .OrderBy(x => x.P.Nombre)
            .Skip(consulta.Saltar).Take(consulta.Tamano)
            .ToListAsync(ct);

        var items = filas.Select(f => Item(f.P, f.Stock)).ToList();
        return Pagina<ProductoItem>.De(items, consulta, total);
    }

    public async Task<Resultado<ProductoDetalle>> VerAsync(int empresaId, long productoId, CancellationToken ct = default)
    {
        var p = await bd.Productos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productoId, ct);
        return p is null
            ? Resultado<ProductoDetalle>.Error(TipoFallo.NoEncontrado, "El producto no existe.", "NO_ENCONTRADO")
            : Resultado<ProductoDetalle>.Ok(await DetalleAsync(p, ct));
    }

    public async Task<Resultado<ProductoDetalle>> PorCodigoBarrasAsync(
        int empresaId, string codigoBarras, CancellationToken ct = default)
    {
        var codigo = codigoBarras.Trim();
        var p = await bd.Productos.AsNoTracking().FirstOrDefaultAsync(x => x.CodigoBarras == codigo, ct);
        return p is null
            ? Resultado<ProductoDetalle>.Error(TipoFallo.NoEncontrado,
                "Ningun producto tiene ese codigo de barras.", "NO_ENCONTRADO")
            : Resultado<ProductoDetalle>.Ok(await DetalleAsync(p, ct));
    }

    public async Task<Resultado<ProductoDetalle>> CrearAsync(
        int empresaId, GuardarProductoPeticion peticion, CancellationToken ct = default)
    {
        var codigo = peticion.Codigo.Trim();
        if (await bd.Productos.AnyAsync(p => p.Codigo.ToLower() == codigo.ToLower(), ct))
            return Resultado<ProductoDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay un producto con ese codigo.", "CODIGO_DUPLICADO");

        var barras = Limpiar(peticion.CodigoBarras);
        if (barras is not null && await bd.Productos.AnyAsync(p => p.CodigoBarras == barras, ct))
            return Resultado<ProductoDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay un producto con ese codigo de barras.", "BARRAS_DUPLICADO");

        var producto = new Producto { EmpresaId = empresaId, Activo = true };
        Volcar(producto, peticion, codigo, barras);

        bd.Productos.Add(producto);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("PRODUCTO.ALTA", "producto", producto.Id.ToString(),
            new { producto.Codigo, producto.Nombre }, ct);

        return Resultado<ProductoDetalle>.Ok(await DetalleAsync(producto, ct));
    }

    public async Task<Resultado<ProductoDetalle>> ActualizarAsync(
        int empresaId, long productoId, GuardarProductoPeticion peticion, CancellationToken ct = default)
    {
        var producto = await bd.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct);
        if (producto is null)
            return Resultado<ProductoDetalle>.Error(TipoFallo.NoEncontrado, "El producto no existe.", "NO_ENCONTRADO");

        var codigo = peticion.Codigo.Trim();
        if (await bd.Productos.AnyAsync(p => p.Id != productoId && p.Codigo.ToLower() == codigo.ToLower(), ct))
            return Resultado<ProductoDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay otro producto con ese codigo.", "CODIGO_DUPLICADO");

        var barras = Limpiar(peticion.CodigoBarras);
        if (barras is not null && await bd.Productos.AnyAsync(p => p.Id != productoId && p.CodigoBarras == barras, ct))
            return Resultado<ProductoDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay otro producto con ese codigo de barras.", "BARRAS_DUPLICADO");

        Volcar(producto, peticion, codigo, barras);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("PRODUCTO.EDICION", "producto", productoId.ToString(),
            new { producto.Codigo, producto.Nombre }, ct);

        return Resultado<ProductoDetalle>.Ok(await DetalleAsync(producto, ct));
    }

    public async Task<Resultado<ProductoDetalle>> CambiarEstadoAsync(
        int empresaId, long productoId, EstadoPeticion peticion, CancellationToken ct = default)
    {
        var producto = await bd.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct);
        if (producto is null)
            return Resultado<ProductoDetalle>.Error(TipoFallo.NoEncontrado, "El producto no existe.", "NO_ENCONTRADO");

        producto.Activo = peticion.Activo;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("PRODUCTO.ESTADO", "producto", productoId.ToString(),
            new { producto.Activo }, ct);

        return Resultado<ProductoDetalle>.Ok(await DetalleAsync(producto, ct));
    }

    public async Task<Resultado<ProductoDetalle>> IngresarAsync(
        int empresaId, long productoId, int? usuarioId, IngresoStockPeticion peticion, CancellationToken ct = default)
    {
        var producto = await bd.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct);
        if (producto is null)
            return Resultado<ProductoDetalle>.Error(TipoFallo.NoEncontrado, "El producto no existe.", "NO_ENCONTRADO");

        if (peticion.Cantidad <= 0)
            return Resultado<ProductoDetalle>.Error(TipoFallo.Validacion,
                "La cantidad a ingresar tiene que ser mayor que cero.", "CANTIDAD_INVALIDA");

        var sede = await SedeValidaAsync(empresaId, peticion.SedeId, ct);
        if (sede is null)
            return Resultado<ProductoDetalle>.Error(TipoFallo.Validacion,
                "La sede indicada no es de esta empresa.", "SEDE_INVALIDA");

        bd.Kardex.Add(new Kardex
        {
            EmpresaId = empresaId,
            ProductoId = productoId,
            SedeId = peticion.SedeId,
            Tipo = "INGRESO",
            Cantidad = peticion.Cantidad,
            CostoUnitario = peticion.CostoUnitario < 0 ? 0 : peticion.CostoUnitario,
            Referencia = Limpiar(peticion.Referencia),
            Motivo = Limpiar(peticion.Motivo),
            UsuarioId = usuarioId
        });

        // El trigger de la base valida y mueve el stock. Un fallo suyo llega
        // como P0001 y el middleware lo vuelve 409: no hay 500 que esconder.
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ALMACEN.INGRESO", "producto", productoId.ToString(),
            new { peticion.SedeId, peticion.Cantidad, peticion.CostoUnitario, peticion.Referencia }, ct);

        return Resultado<ProductoDetalle>.Ok(await DetalleAsync(producto, ct));
    }

    public async Task<Resultado<ProductoDetalle>> AjustarAsync(
        int empresaId, long productoId, int? usuarioId, AjusteStockPeticion peticion, CancellationToken ct = default)
    {
        var producto = await bd.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct);
        if (producto is null)
            return Resultado<ProductoDetalle>.Error(TipoFallo.NoEncontrado, "El producto no existe.", "NO_ENCONTRADO");

        if (peticion.CantidadReal < 0)
            return Resultado<ProductoDetalle>.Error(TipoFallo.Validacion,
                "La cantidad contada no puede ser negativa.", "CANTIDAD_INVALIDA");

        if (string.IsNullOrWhiteSpace(peticion.Motivo))
            return Resultado<ProductoDetalle>.Error(TipoFallo.Validacion,
                "Un ajuste de inventario exige un motivo.", "MOTIVO_REQUERIDO");

        var sede = await SedeValidaAsync(empresaId, peticion.SedeId, ct);
        if (sede is null)
            return Resultado<ProductoDetalle>.Error(TipoFallo.Validacion,
                "La sede indicada no es de esta empresa.", "SEDE_INVALIDA");

        var actual = await bd.ProductoStocks.AsNoTracking()
            .Where(s => s.ProductoId == productoId && s.SedeId == peticion.SedeId)
            .Select(s => (decimal?)s.Cantidad).FirstOrDefaultAsync(ct) ?? 0m;

        var diferencia = peticion.CantidadReal - actual;

        if (diferencia == 0)
            return Resultado<ProductoDetalle>.Ok(await DetalleAsync(producto, ct));   // ya cuadra

        var costoActual = await bd.ProductoStocks.AsNoTracking()
            .Where(s => s.ProductoId == productoId && s.SedeId == peticion.SedeId)
            .Select(s => (decimal?)s.CostoPromedio).FirstOrDefaultAsync(ct) ?? 0m;

        bd.Kardex.Add(new Kardex
        {
            EmpresaId = empresaId,
            ProductoId = productoId,
            SedeId = peticion.SedeId,
            Tipo = diferencia > 0 ? "AJUSTE_POS" : "AJUSTE_NEG",
            Cantidad = Math.Abs(diferencia),
            CostoUnitario = costoActual,       // el ajuste no inventa costo nuevo
            Referencia = "AJUSTE",
            Motivo = peticion.Motivo.Trim(),
            UsuarioId = usuarioId
        });

        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ALMACEN.AJUSTE", "producto", productoId.ToString(),
            new { peticion.SedeId, antes = actual, ahora = peticion.CantidadReal, peticion.Motivo }, ct);

        return Resultado<ProductoDetalle>.Ok(await DetalleAsync(producto, ct));
    }

    public async Task<Resultado<Pagina<MovimientoKardexItem>>> KardexAsync(
        int empresaId, long productoId, ConsultaPaginada consulta, int? sedeId = null, CancellationToken ct = default)
    {
        if (!await bd.Productos.AnyAsync(p => p.Id == productoId, ct))
            return Resultado<Pagina<MovimientoKardexItem>>.Error(
                TipoFallo.NoEncontrado, "El producto no existe.", "NO_ENCONTRADO");

        var q = bd.Kardex.AsNoTracking().Where(k => k.ProductoId == productoId);
        if (sedeId.HasValue) q = q.Where(k => k.SedeId == sedeId.Value);

        var total = await q.CountAsync(ct);
        var filas = await q
            .OrderByDescending(k => k.Id)
            .Skip(consulta.Saltar).Take(consulta.Tamano)
            .Select(k => new
            {
                K = k,
                Sede = k.Sede!.Nombre,
                Usuario = k.UsuarioId == null ? null
                    : bd.Usuarios.IgnoreQueryFilters().Where(u => u.Id == k.UsuarioId)
                        .Select(u => u.Nombres + " " + u.Apellidos).FirstOrDefault()
            })
            .ToListAsync(ct);

        var items = filas.Select(f => new MovimientoKardexItem
        {
            Id = f.K.Id,
            ProductoId = f.K.ProductoId,
            SedeId = f.K.SedeId,
            Sede = f.Sede,
            Tipo = f.K.Tipo,
            Cantidad = f.K.Cantidad,
            CostoUnitario = f.K.CostoUnitario,
            Saldo = f.K.Saldo,
            Referencia = f.K.Referencia,
            Motivo = f.K.Motivo,
            Usuario = f.Usuario == null ? null : f.Usuario.Trim(),
            CreadoEn = f.K.CreadoEn
        }).ToList();

        return Resultado<Pagina<MovimientoKardexItem>>.Ok(Pagina<MovimientoKardexItem>.De(items, consulta, total));
    }

    // ----------------------------------------------------------------------

    private Task<Sede?> SedeValidaAsync(int empresaId, int sedeId, CancellationToken ct) =>
        bd.Sedes.FirstOrDefaultAsync(s => s.Id == sedeId && s.EmpresaId == empresaId && s.Activo, ct);

    private static void Volcar(Producto p, GuardarProductoPeticion peticion, string codigo, string? barras)
    {
        p.Codigo = codigo;
        p.CodigoBarras = barras;
        p.Nombre = peticion.Nombre.Trim();
        p.Categoria = Limpiar(peticion.Categoria);
        p.Tipo = peticion.Tipo;
        p.Unidad = peticion.Unidad;
        p.Marca = Limpiar(peticion.Marca);
        p.PrecioVenta = peticion.PrecioVenta;
        p.StockMinimo = peticion.StockMinimo;
        p.ControlaStock = peticion.ControlaStock;
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private async Task<ProductoDetalle> DetalleAsync(Producto p, CancellationToken ct)
    {
        var stock = await bd.ProductoStocks.AsNoTracking()
            .Where(s => s.ProductoId == p.Id)
            .Select(s => new StockSedeItem
            {
                SedeId = s.SedeId,
                Sede = s.Sede!.Nombre,
                Cantidad = s.Cantidad,
                CostoPromedio = s.CostoPromedio
            })
            .OrderBy(s => s.Sede)
            .ToListAsync(ct);

        var totalStock = stock.Sum(s => s.Cantidad);

        var d = new ProductoDetalle { Marca = p.Marca, CreadoEn = p.CreadoEn, Stock = stock };
        Rellenar(d, p, totalStock);
        return d;
    }

    private static ProductoItem Item(Producto p, decimal stockTotal)
    {
        var i = new ProductoItem();
        Rellenar(i, p, stockTotal);
        return i;
    }

    private static void Rellenar(ProductoItem i, Producto p, decimal stockTotal)
    {
        i.Id = p.Id;
        i.Codigo = p.Codigo;
        i.CodigoBarras = p.CodigoBarras;
        i.Nombre = p.Nombre;
        i.Categoria = p.Categoria;
        i.Tipo = p.Tipo;
        i.Unidad = p.Unidad;
        i.PrecioVenta = p.PrecioVenta;
        i.StockMinimo = p.StockMinimo;
        i.ControlaStock = p.ControlaStock;
        i.Activo = p.Activo;
        i.StockTotal = stockTotal;
        i.BajoMinimo = p.ControlaStock && stockTotal < p.StockMinimo;
    }
}
