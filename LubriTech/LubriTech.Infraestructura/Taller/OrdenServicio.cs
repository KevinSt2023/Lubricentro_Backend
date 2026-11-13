using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// Las ordenes de trabajo. Lo que el codigo cuida: escribir el historial en
/// cada cambio, recalcular los totales, y consumir repuestos moviendo kardex
/// en una sola transaccion. Lo que cuida la base: que la orden avance de a un
/// estado, que una final no cambie, y que no se consuma mas stock del que hay.
/// </summary>
public class OrdenesServicio(
    LubriTechContexto bd,
    IBitacoraServicio bitacora) : IOrdenServicio
{
    private const string EnEjecucion = "EN_EJECUCION";
    private const string Recibido = "RECIBIDO";
    private const string Entregada = "ENTREGADA";
    private const string Anulada = "ANULADA";

    // Al entregar, se propone el próximo mantenimiento: +5000 km y +6 meses.
    private const int MantIntervaloKm = 5000;
    private const int MantIntervaloMeses = 6;

    public Task<List<EstadoItem>> EstadosAsync(CancellationToken ct = default) =>
        bd.OrdenEstados.AsNoTracking().OrderBy(e => e.Secuencia)
          .Select(e => new EstadoItem
          {
              Id = e.Id, Codigo = e.Codigo, Nombre = e.Nombre,
              Secuencia = e.Secuencia, EsFinal = e.EsFinal, EsAnulacion = e.EsAnulacion
          }).ToListAsync(ct);

    public async Task<Pagina<OrdenItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        int? estadoId = null, int? tecnicoId = null, int? sedeId = null,
        long? clienteId = null, long? vehiculoId = null,
        int? soloDelTecnico = null, int? poolSedeTecnico = null, CancellationToken ct = default)
    {
        var q = bd.Ordenes.AsNoTracking().AsQueryable();

        if (estadoId.HasValue) q = q.Where(o => o.EstadoId == estadoId.Value);
        if (tecnicoId.HasValue) q = q.Where(o => o.TecnicoId == tecnicoId.Value);
        if (sedeId.HasValue) q = q.Where(o => o.SedeId == sedeId.Value);
        if (clienteId.HasValue) q = q.Where(o => o.ClienteId == clienteId.Value);
        if (vehiculoId.HasValue) q = q.Where(o => o.VehiculoId == vehiculoId.Value);

        // El tecnico (ORDENES.VER_ASIGNADAS sin ORDENES.VER) ve sus ordenes
        // asignadas y, ademas, la cola sin asignar de su sede (que puede tomar).
        // Sin sede en el token, solo ve lo suyo.
        if (soloDelTecnico.HasValue)
        {
            var tec = soloDelTecnico.Value;
            if (poolSedeTecnico is int sedeTec)
                q = q.Where(o => o.TecnicoId == tec
                    || (o.TecnicoId == null && o.SedeId == sedeTec && !o.Estado!.EsFinal));
            else
                q = q.Where(o => o.TecnicoId == tec);
        }

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var b = consulta.Buscar.Trim().ToLower();
            q = q.Where(o => o.Vehiculo!.Placa.ToLower().Contains(b) ||
                             (o.Cliente!.RazonSocial != null && o.Cliente.RazonSocial.ToLower().Contains(b)) ||
                             (o.Cliente!.Nombres != null && o.Cliente.Nombres.ToLower().Contains(b)));
        }

        var total = await q.CountAsync(ct);
        var filas = await q
            .OrderByDescending(o => o.Id)
            .Skip(consulta.Saltar).Take(consulta.Tamano)
            .Select(o => new
            {
                O = o,
                Estado = o.Estado!.Nombre,
                EstadoFinal = o.Estado.EsFinal,
                Sede = o.Sede!.Nombre,
                Cliente = o.Cliente!.RazonSocial ?? (o.Cliente.Nombres + " " + o.Cliente.Apellidos),
                Placa = o.Vehiculo!.Placa,
                VehiculoDesc = o.Vehiculo.Marca + " " + o.Vehiculo.Modelo,
                Tecnico = o.Tecnico != null ? o.Tecnico.Nombres + " " + o.Tecnico.Apellidos : null
            })
            .ToListAsync(ct);

        var items = filas.Select(f => new OrdenItem
        {
            Id = f.O.Id, Numero = f.O.Numero, EstadoId = f.O.EstadoId, Estado = f.Estado,
            EstadoFinal = f.EstadoFinal, SedeId = f.O.SedeId, Sede = f.Sede,
            ClienteId = f.O.ClienteId, Cliente = (f.Cliente ?? "").Trim(),
            VehiculoId = f.O.VehiculoId, Placa = f.Placa, Vehiculo = f.VehiculoDesc,
            TecnicoId = f.O.TecnicoId, Tecnico = f.Tecnico == null ? null : f.Tecnico.Trim(),
            Total = f.O.Total, CreadoEn = f.O.CreadoEn
        }).ToList();

        return Pagina<OrdenItem>.De(items, consulta, total);
    }

    public async Task<Resultado<OrdenDetalle>> VerAsync(
        int empresaId, long ordenId, bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await CargarAsync(ordenId, ct);
        return orden is null
            ? Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO")
            : Resultado<OrdenDetalle>.Ok(await DetalleAsync(orden, puedeVerCostos, ct));
    }

    public async Task<Resultado<OrdenDetalle>> CrearAsync(
        int empresaId, int? usuarioId, CrearOrdenPeticion peticion, bool puedeVerCostos, CancellationToken ct = default)
    {
        var estado = await EstadoPorCodigoAsync(peticion.EstadoInicial, ct);
        if (estado is null || (estado.Codigo != "CITA_REGISTRADA" && estado.Codigo != Recibido))
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion,
                "Una orden arranca en Cita registrada o en Recibido.", "ESTADO_INICIAL");

        var vehiculo = await bd.Vehiculos.FirstOrDefaultAsync(v => v.Id == peticion.VehiculoId, ct);
        if (vehiculo is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "El vehiculo no existe.", "VEHICULO_INVALIDO");

        if (vehiculo.ClienteId != peticion.ClienteId)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion,
                "El vehiculo no pertenece a ese cliente.", "VEHICULO_DE_OTRO");

        if (!await bd.Sedes.AnyAsync(s => s.Id == peticion.SedeId && s.EmpresaId == empresaId && s.Activo, ct))
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "La sede no es de esta empresa.", "SEDE_INVALIDA");

        if (peticion.TecnicoId.HasValue && !await EsTecnicoDeLaEmpresaAsync(empresaId, peticion.TecnicoId.Value, ct))
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "El tecnico no es de esta empresa.", "TECNICO_INVALIDO");

        var ahora = Tiempo.Ahora;
        var orden = new Orden
        {
            EmpresaId = empresaId,
            SedeId = peticion.SedeId,
            ClienteId = peticion.ClienteId,
            VehiculoId = peticion.VehiculoId,
            EstadoId = estado.Id,
            AsesorId = usuarioId,
            TecnicoId = peticion.TecnicoId,
            KilometrajeIngreso = peticion.KilometrajeIngreso,
            FechaCita = peticion.FechaCita,
            Observaciones = Limpiar(peticion.Observaciones),
            RecibidoEn = estado.Codigo == Recibido ? ahora : null,
            CreadoPor = usuarioId
        };

        bd.Ordenes.Add(orden);
        await bd.SaveChangesAsync(ct);   // el trigger asigna el numero

        // El kilometraje de ingreso, si supera lo registrado, actualiza el odometro.
        if (peticion.KilometrajeIngreso is int km && km > vehiculo.Kilometraje)
        {
            vehiculo.Kilometraje = km;
            await bd.SaveChangesAsync(ct);
        }

        await EscribirHistorialAsync(empresaId, orden.Id, null, estado.Id, usuarioId, "Orden abierta", ct);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ORDEN.ALTA", "orden", orden.Id.ToString(),
            new { orden.Numero, estado = estado.Codigo, vehiculo = vehiculo.Placa }, ct);

        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(orden.Id, ct))!, puedeVerCostos, ct));
    }

    public async Task<Resultado<OrdenDetalle>> ActualizarAsync(
        int empresaId, long ordenId, ActualizarOrdenPeticion peticion, bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        if (orden.Estado!.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto,
                "Una orden cerrada ya no se edita.", "ORDEN_FINAL");

        orden.KilometrajeIngreso = peticion.KilometrajeIngreso ?? orden.KilometrajeIngreso;
        orden.Diagnostico = Limpiar(peticion.Diagnostico) ?? orden.Diagnostico;
        orden.Observaciones = Limpiar(peticion.Observaciones) ?? orden.Observaciones;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ORDEN.EDICION", "orden", ordenId.ToString(), new { orden.Numero }, ct);
        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    public async Task<Resultado<OrdenDetalle>> CambiarEstadoAsync(
        int empresaId, int? usuarioId, long ordenId, CambiarEstadoOrdenPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        var destino = await EstadoPorCodigoAsync(peticion.Estado, ct);
        if (destino is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "Ese estado no existe.", "ESTADO_INVALIDO");

        if (destino.EsAnulacion)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion,
                "Para anular usa la accion de anular: exige un motivo.", "USAR_ANULAR");

        var actual = orden.Estado!;
        if (actual.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto,
                "La orden ya esta cerrada y no cambia de estado.", "ORDEN_FINAL");

        if (destino.Secuencia != actual.Secuencia + 1)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto,
                $"La orden va de a un paso: de {actual.Nombre} sigue el estado que viene, no {destino.Nombre}.",
                "SALTO_DE_ESTADO");

        var anteriorId = orden.EstadoId;
        orden.EstadoId = destino.Id;
        if (destino.Codigo == Recibido && orden.RecibidoEn is null) orden.RecibidoEn = Tiempo.Ahora;
        if (destino.Codigo == Entregada)
        {
            orden.EntregadoEn = Tiempo.Ahora;

            // El vehiculo entregado agenda su proximo mantenimiento: sobre el
            // mayor km conocido + intervalo, y a 6 meses. El taller lo puede
            // ajustar a mano despues; esto solo evita que quede en blanco.
            var veh = await bd.Vehiculos.FirstOrDefaultAsync(v => v.Id == orden.VehiculoId, ct);
            if (veh is not null)
            {
                var kmBase = Math.Max(veh.Kilometraje, orden.KilometrajeIngreso ?? 0);
                veh.ProximoMantKm = kmBase + MantIntervaloKm;
                veh.ProximoMantFecha = Tiempo.HoyLima().AddMonths(MantIntervaloMeses);
            }
        }

        await bd.SaveChangesAsync(ct);   // el trigger es el respaldo de la transicion

        await EscribirHistorialAsync(empresaId, ordenId, anteriorId, destino.Id, usuarioId, peticion.Observacion, ct);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ORDEN.ESTADO", "orden", ordenId.ToString(),
            new { orden.Numero, de = actual.Codigo, a = destino.Codigo }, ct);

        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    public async Task<Resultado<OrdenDetalle>> AsignarTecnicoAsync(
        int empresaId, int? usuarioId, long ordenId, AsignarTecnicoPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        if (orden.Estado!.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto, "Una orden cerrada no se reasigna.", "ORDEN_FINAL");

        if (!await EsTecnicoDeLaEmpresaAsync(empresaId, peticion.TecnicoId, ct))
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "El tecnico no es de esta empresa.", "TECNICO_INVALIDO");

        orden.TecnicoId = peticion.TecnicoId;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ORDEN.ASIGNACION", "orden", ordenId.ToString(),
            new { orden.Numero, tecnicoId = peticion.TecnicoId }, ct);

        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    public async Task<Resultado<OrdenDetalle>> TomarAsync(
        int empresaId, int? usuarioId, int? sedeTecnico, long ordenId,
        bool puedeVerCostos, CancellationToken ct = default)
    {
        if (usuarioId is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Prohibido, "La sesion no resuelve el usuario.", "SIN_USUARIO");

        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        if (orden.Estado!.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto, "Una orden cerrada no se toma.", "ORDEN_FINAL");

        if (sedeTecnico is null || orden.SedeId != sedeTecnico.Value)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Prohibido, "Solo puedes tomar ordenes de tu sede.", "OTRA_SEDE");

        if (orden.TecnicoId is not null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto,
                orden.TecnicoId == usuarioId.Value ? "Ya tienes esta orden." : "La orden ya tiene tecnico.",
                "YA_ASIGNADA");

        orden.TecnicoId = usuarioId.Value;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ORDEN.TOMA", "orden", ordenId.ToString(),
            new { orden.Numero, tecnicoId = usuarioId.Value }, ct);

        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    public async Task<Resultado<OrdenDetalle>> AnularAsync(
        int empresaId, int? usuarioId, long ordenId, AnularOrdenPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(peticion.Motivo))
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "Anular una orden exige un motivo.", "MOTIVO_REQUERIDO");

        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        if (orden.Estado!.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto,
                "La orden ya esta cerrada: no se puede anular.", "ORDEN_FINAL");

        var anulada = await EstadoPorCodigoAsync(Anulada, ct);
        var anteriorId = orden.EstadoId;
        orden.EstadoId = anulada!.Id;
        orden.AnuladaMotivo = peticion.Motivo.Trim();
        await bd.SaveChangesAsync(ct);

        await EscribirHistorialAsync(empresaId, ordenId, anteriorId, anulada.Id, usuarioId, peticion.Motivo.Trim(), ct);
        await bd.SaveChangesAsync(ct);

        // Nota: los repuestos ya consumidos NO vuelven solos. Devolverlos al
        // stock es un ingreso explicito, porque fisicamente pueden haberse
        // usado. Esa decision es de quien anula, no automatica.
        await bitacora.RegistrarAsync("ORDEN.ANULACION", "orden", ordenId.ToString(),
            new { orden.Numero, peticion.Motivo }, ct);

        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    // ----------------------------- Servicios ------------------------------

    public async Task<Resultado<OrdenDetalle>> AgregarServicioAsync(
        int empresaId, long ordenId, AgregarServicioPeticion peticion, bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");
        if (orden.Estado!.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto, "Una orden cerrada no se modifica.", "ORDEN_FINAL");

        if (peticion.Cantidad <= 0)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "La cantidad tiene que ser mayor que cero.", "CANTIDAD_INVALIDA");

        string descripcion; decimal precio;
        if (peticion.ServicioId.HasValue)
        {
            var srv = await bd.Servicios.FirstOrDefaultAsync(s => s.Id == peticion.ServicioId.Value, ct);
            if (srv is null)
                return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "El servicio no existe.", "SERVICIO_INVALIDO");
            descripcion = srv.Nombre;
            precio = peticion.Precio ?? srv.Precio;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(peticion.Descripcion))
                return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "Un servicio suelto necesita descripcion.", "DESCRIPCION");
            descripcion = peticion.Descripcion.Trim();
            precio = peticion.Precio ?? 0;
        }

        bd.OrdenServicios.Add(new OrdenServicio
        {
            EmpresaId = empresaId, OrdenId = ordenId, ServicioId = peticion.ServicioId,
            Descripcion = descripcion, Precio = precio, Cantidad = peticion.Cantidad
        });
        await bd.SaveChangesAsync(ct);
        await RecalcularTotalesAsync(orden, ct);

        await bitacora.RegistrarAsync("ORDEN.SERVICIO", "orden", ordenId.ToString(), new { descripcion, precio }, ct);
        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    public async Task<Resultado<OrdenDetalle>> QuitarServicioAsync(
        int empresaId, long ordenId, long servicioLineaId, bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");
        if (orden.Estado!.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto, "Una orden cerrada no se modifica.", "ORDEN_FINAL");

        var linea = await bd.OrdenServicios.FirstOrDefaultAsync(s => s.Id == servicioLineaId && s.OrdenId == ordenId, ct);
        if (linea is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "Ese renglon no existe en la orden.", "NO_ENCONTRADO");

        bd.OrdenServicios.Remove(linea);
        await bd.SaveChangesAsync(ct);
        await RecalcularTotalesAsync(orden, ct);

        await bitacora.RegistrarAsync("ORDEN.SERVICIO_QUITA", "orden", ordenId.ToString(), new { servicioLineaId }, ct);
        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    // ------------------------- Consumo de repuestos ------------------------

    public async Task<Resultado<OrdenDetalle>> ConsumirRepuestoAsync(
        int empresaId, int? usuarioId, long ordenId, ConsumirRepuestoPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        // La regla que cerramos: solo se consume con la orden En ejecucion.
        if (orden.Estado!.Codigo != EnEjecucion)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto,
                "Solo se consume repuesto con la orden En ejecucion.", "ORDEN_NO_EN_EJECUCION");

        if (peticion.Cantidad <= 0)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "La cantidad tiene que ser mayor que cero.", "CANTIDAD_INVALIDA");

        var producto = await bd.Productos.FirstOrDefaultAsync(p => p.Id == peticion.ProductoId, ct);
        if (producto is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "El producto no existe.", "PRODUCTO_INVALIDO");

        if (!await bd.Sedes.AnyAsync(s => s.Id == peticion.SedeId && s.EmpresaId == empresaId, ct))
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion, "La sede no es de esta empresa.", "SEDE_INVALIDA");

        // El costo con el que sale es el promedio de la sede en este momento.
        var costo = await bd.ProductoStocks.AsNoTracking()
            .Where(s => s.ProductoId == peticion.ProductoId && s.SedeId == peticion.SedeId)
            .Select(s => (decimal?)s.CostoPromedio).FirstOrDefaultAsync(ct) ?? 0m;

        // La SALIDA de kardex y la linea de consumo, juntas o nada. Si no hay
        // stock, el trigger del kardex lanza y la transaccion no deja rastro.
        await bd.EnTransaccionAsync(async token =>
        {
            var movimiento = new Kardex
            {
                EmpresaId = empresaId,
                ProductoId = peticion.ProductoId,
                SedeId = peticion.SedeId,
                Tipo = "SALIDA",
                Cantidad = peticion.Cantidad,
                Referencia = $"Orden {orden.Numero}",
                Motivo = "Consumo en orden de trabajo",
                UsuarioId = usuarioId
            };
            bd.Kardex.Add(movimiento);
            await bd.SaveChangesAsync(token);

            bd.OrdenConsumos.Add(new OrdenConsumo
            {
                EmpresaId = empresaId,
                OrdenId = ordenId,
                ProductoId = peticion.ProductoId,
                SedeId = peticion.SedeId,
                Cantidad = peticion.Cantidad,
                CostoUnitario = costo,
                PrecioVenta = producto.PrecioVenta,
                KardexId = movimiento.Id
            });
            await bd.SaveChangesAsync(token);
        }, ct);

        await RecalcularTotalesAsync(orden, ct);

        await bitacora.RegistrarAsync("ORDEN.CONSUMO", "orden", ordenId.ToString(),
            new { orden.Numero, producto = producto.Codigo, peticion.Cantidad, peticion.SedeId }, ct);

        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    // ----------------------------- Checklist -------------------------------

    public async Task<Resultado<OrdenDetalle>> GuardarChecklistAsync(
        int empresaId, long ordenId, GuardarChecklistPeticion peticion, bool puedeVerCostos, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");
        if (orden.Estado!.EsFinal)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto, "Una orden cerrada no se modifica.", "ORDEN_FINAL");

        var validos = new[] { "OK", "OBSERVADO", "MALO", "NA" };
        if (peticion.Items.Any(i => string.IsNullOrWhiteSpace(i.Item) || !validos.Contains(i.Resultado)))
            return Resultado<OrdenDetalle>.Error(TipoFallo.Validacion,
                "Cada punto del checklist necesita nombre y un resultado valido (OK/OBSERVADO/MALO/NA).", "CHECKLIST");

        // Se reemplaza la inspeccion completa: llega la foto actual del checklist.
        var previas = bd.OrdenChecklists.Where(c => c.OrdenId == ordenId);
        bd.OrdenChecklists.RemoveRange(previas);
        foreach (var i in peticion.Items)
            bd.OrdenChecklists.Add(new OrdenChecklist
            {
                EmpresaId = empresaId, OrdenId = ordenId,
                Item = i.Item.Trim(), Resultado = i.Resultado, Nota = Limpiar(i.Nota)
            });

        await bd.SaveChangesAsync(ct);
        await bitacora.RegistrarAsync("ORDEN.CHECKLIST", "orden", ordenId.ToString(),
            new { orden.Numero, puntos = peticion.Items.Count }, ct);

        return Resultado<OrdenDetalle>.Ok(await DetalleAsync((await CargarAsync(ordenId, ct))!, puedeVerCostos, ct));
    }

    // ----------------------------- Evidencias ------------------------------

    public async Task<Resultado<OrdenEvidenciaItem>> RegistrarEvidenciaAsync(
        int empresaId, int? usuarioId, long ordenId,
        string tipo, string archivo, string? nombre, string mime, long bytes, string hash,
        CancellationToken ct = default)
    {
        if (!await bd.Ordenes.AnyAsync(o => o.Id == ordenId, ct))
            return Resultado<OrdenEvidenciaItem>.Error(TipoFallo.NoEncontrado, "La orden no existe.", "NO_ENCONTRADO");

        var ev = new OrdenEvidencia
        {
            EmpresaId = empresaId, OrdenId = ordenId, Tipo = tipo,
            Archivo = archivo, Nombre = nombre, Mime = mime, Bytes = bytes, Hash = hash,
            SubidoPor = usuarioId
        };
        bd.OrdenEvidencias.Add(ev);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("ORDEN.EVIDENCIA", "orden", ordenId.ToString(), new { ev.Id, mime, bytes }, ct);
        return Resultado<OrdenEvidenciaItem>.Ok(Evidencia(ev, null));
    }

    public async Task<Resultado<(string Archivo, string Mime)>> EvidenciaArchivoAsync(
        int empresaId, long ordenId, long evidenciaId, CancellationToken ct = default)
    {
        var ev = await bd.OrdenEvidencias.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == evidenciaId && e.OrdenId == ordenId, ct);
        return ev is null
            ? Resultado<(string, string)>.Error(TipoFallo.NoEncontrado, "La evidencia no existe.", "NO_ENCONTRADO")
            : Resultado<(string, string)>.Ok((ev.Archivo, ev.Mime));
    }

    // ----------------------- Plantilla de checklist ------------------------

    public Task<List<ChecklistPlantillaItem>> PlantillaAsync(int empresaId, bool soloActivos, CancellationToken ct = default)
    {
        var q = bd.ChecklistItems.AsNoTracking().AsQueryable();
        if (soloActivos) q = q.Where(i => i.Activo);
        return q.OrderBy(i => i.Orden).ThenBy(i => i.Nombre)
            .Select(i => new ChecklistPlantillaItem
            {
                Id = i.Id, Codigo = i.Codigo, Nombre = i.Nombre, Orden = i.Orden, Activo = i.Activo
            }).ToListAsync(ct);
    }

    public async Task<Resultado<ChecklistPlantillaItem>> GuardarPlantillaItemAsync(
        int empresaId, long? itemId, GuardarChecklistItemPeticion peticion, CancellationToken ct = default)
    {
        var codigo = peticion.Codigo.Trim();
        var choque = await bd.ChecklistItems
            .AnyAsync(i => i.Codigo.ToUpper() == codigo.ToUpper() && (itemId == null || i.Id != itemId), ct);
        if (choque)
            return Resultado<ChecklistPlantillaItem>.Error(TipoFallo.Conflicto,
                "Ya hay un punto con ese codigo.", "CODIGO_DUPLICADO");

        ChecklistItem item;
        if (itemId.HasValue)
        {
            item = (await bd.ChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId.Value, ct))!;
            if (item is null)
                return Resultado<ChecklistPlantillaItem>.Error(TipoFallo.NoEncontrado, "El punto no existe.", "NO_ENCONTRADO");
        }
        else
        {
            item = new ChecklistItem { EmpresaId = empresaId, Activo = true };
            bd.ChecklistItems.Add(item);
        }

        item.Codigo = codigo;
        item.Nombre = peticion.Nombre.Trim();
        item.Orden = peticion.Orden;
        await bd.SaveChangesAsync(ct);

        return Resultado<ChecklistPlantillaItem>.Ok(new ChecklistPlantillaItem
        {
            Id = item.Id, Codigo = item.Codigo, Nombre = item.Nombre, Orden = item.Orden, Activo = item.Activo
        });
    }

    public async Task<Resultado<ChecklistPlantillaItem>> CambiarEstadoPlantillaItemAsync(
        int empresaId, long itemId, bool activo, CancellationToken ct = default)
    {
        var item = await bd.ChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is null)
            return Resultado<ChecklistPlantillaItem>.Error(TipoFallo.NoEncontrado, "El punto no existe.", "NO_ENCONTRADO");

        item.Activo = activo;
        await bd.SaveChangesAsync(ct);
        return Resultado<ChecklistPlantillaItem>.Ok(new ChecklistPlantillaItem
        {
            Id = item.Id, Codigo = item.Codigo, Nombre = item.Nombre, Orden = item.Orden, Activo = item.Activo
        });
    }

    // ------------------------------ Helpers --------------------------------

    private Task<Orden?> CargarAsync(long ordenId, CancellationToken ct) =>
        bd.Ordenes
          .Include(o => o.Estado)
          .Include(o => o.Sede)
          .Include(o => o.Cliente)
          .Include(o => o.Vehiculo)
          .Include(o => o.Tecnico)
          .Include(o => o.Asesor)
          .FirstOrDefaultAsync(o => o.Id == ordenId, ct);

    private Task<OrdenEstado?> EstadoPorCodigoAsync(string codigo, CancellationToken ct) =>
        bd.OrdenEstados.FirstOrDefaultAsync(e => e.Codigo == codigo.Trim().ToUpper(), ct);

    private Task<bool> EsTecnicoDeLaEmpresaAsync(int empresaId, int tecnicoId, CancellationToken ct) =>
        bd.Usuarios.IgnoreQueryFilters()
          .AnyAsync(u => u.Id == tecnicoId && u.EmpresaId == empresaId && u.Activo && u.RolId > Roles.SuperAdmin, ct);

    private async Task EscribirHistorialAsync(
        int empresaId, long ordenId, int? anteriorId, int nuevoId, int? usuarioId, string? obs, CancellationToken ct)
    {
        bd.OrdenHistoriales.Add(new OrdenHistorial
        {
            EmpresaId = empresaId, OrdenId = ordenId,
            EstadoAnteriorId = anteriorId, EstadoNuevoId = nuevoId,
            UsuarioId = usuarioId, Observacion = Limpiar(obs)
        });
        await Task.CompletedTask;
    }

    private async Task RecalcularTotalesAsync(Orden orden, CancellationToken ct)
    {
        orden.TotalServicios = await bd.OrdenServicios
            .Where(s => s.OrdenId == orden.Id).SumAsync(s => (decimal?)(s.Precio * s.Cantidad), ct) ?? 0m;
        orden.TotalRepuestos = await bd.OrdenConsumos
            .Where(c => c.OrdenId == orden.Id).SumAsync(c => (decimal?)(c.PrecioVenta * c.Cantidad), ct) ?? 0m;
        orden.Total = orden.TotalServicios + orden.TotalRepuestos;
        await bd.SaveChangesAsync(ct);
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private async Task<OrdenDetalle> DetalleAsync(Orden o, bool costos, CancellationToken ct)
    {
        var servicios = await bd.OrdenServicios.AsNoTracking().Where(s => s.OrdenId == o.Id)
            .OrderBy(s => s.Id)
            .Select(s => new OrdenServicioItem
            {
                Id = s.Id, ServicioId = s.ServicioId, Descripcion = s.Descripcion,
                Precio = s.Precio, Cantidad = s.Cantidad, Subtotal = s.Precio * s.Cantidad
            }).ToListAsync(ct);

        var consumos = await bd.OrdenConsumos.AsNoTracking().Where(c => c.OrdenId == o.Id)
            .OrderBy(c => c.Id)
            .Select(c => new { c, c.Producto!.Nombre, c.Producto.Codigo }).ToListAsync(ct);

        var consumosItems = consumos.Select(x => new OrdenConsumoItem
        {
            Id = x.c.Id, ProductoId = x.c.ProductoId, Producto = x.Nombre, Codigo = x.Codigo,
            SedeId = x.c.SedeId, Cantidad = x.c.Cantidad, PrecioVenta = x.c.PrecioVenta,
            Subtotal = x.c.PrecioVenta * x.c.Cantidad,
            CostoUnitario = costos ? x.c.CostoUnitario : null,
            CreadoEn = x.c.CreadoEn
        }).ToList();

        var checklist = await bd.OrdenChecklists.AsNoTracking().Where(c => c.OrdenId == o.Id)
            .OrderBy(c => c.Id)
            .Select(c => new OrdenChecklistRespuesta { Id = c.Id, Item = c.Item, Resultado = c.Resultado, Nota = c.Nota })
            .ToListAsync(ct);

        var evidencias = (await bd.OrdenEvidencias.AsNoTracking().Where(e => e.OrdenId == o.Id)
            .OrderBy(e => e.Id)
            .Select(e => new { e, Usuario = e.SubidoPor == null ? null :
                bd.Usuarios.IgnoreQueryFilters().Where(u => u.Id == e.SubidoPor).Select(u => u.Nombres + " " + u.Apellidos).FirstOrDefault() })
            .ToListAsync(ct))
            .Select(x => Evidencia(x.e, x.Usuario?.Trim())).ToList();

        var historial = await bd.OrdenHistoriales.AsNoTracking().Where(h => h.OrdenId == o.Id)
            .OrderBy(h => h.Id)
            .Select(h => new OrdenHistorialItem
            {
                Id = h.Id,
                EstadoAnterior = h.EstadoAnterior != null ? h.EstadoAnterior.Nombre : null,
                EstadoNuevo = h.EstadoNuevo!.Nombre,
                Usuario = h.Usuario != null ? h.Usuario.Nombres + " " + h.Usuario.Apellidos : null,
                Observacion = h.Observacion, CreadoEn = h.CreadoEn
            }).ToListAsync(ct);

        var d = new OrdenDetalle
        {
            Id = o.Id, Numero = o.Numero, EstadoId = o.EstadoId, Estado = o.Estado!.Nombre,
            EstadoFinal = o.Estado.EsFinal, SedeId = o.SedeId, Sede = o.Sede?.Nombre,
            ClienteId = o.ClienteId, Cliente = o.Cliente?.NombreMostrado,
            VehiculoId = o.VehiculoId, Placa = o.Vehiculo?.Placa,
            Vehiculo = o.Vehiculo is null ? null : $"{o.Vehiculo.Marca} {o.Vehiculo.Modelo}",
            TecnicoId = o.TecnicoId, Tecnico = o.Tecnico is null ? null : $"{o.Tecnico.Nombres} {o.Tecnico.Apellidos}".Trim(),
            AsesorId = o.AsesorId, Asesor = o.Asesor is null ? null : $"{o.Asesor.Nombres} {o.Asesor.Apellidos}".Trim(),
            KilometrajeIngreso = o.KilometrajeIngreso, Diagnostico = o.Diagnostico, Observaciones = o.Observaciones,
            FechaCita = o.FechaCita, RecibidoEn = o.RecibidoEn, EntregadoEn = o.EntregadoEn,
            AnuladaMotivo = o.AnuladaMotivo,
            Total = o.Total, TotalServicios = o.TotalServicios, TotalRepuestos = o.TotalRepuestos,
            CreadoEn = o.CreadoEn,
            Servicios = servicios, Consumos = consumosItems, Checklist = checklist,
            Evidencias = evidencias, Historial = historial
        };

        if (costos)
        {
            d.CostoRepuestos = consumos.Sum(x => x.c.CostoUnitario * x.c.Cantidad);
            d.Margen = o.TotalRepuestos - d.CostoRepuestos;
        }

        return d;
    }

    private static OrdenEvidenciaItem Evidencia(OrdenEvidencia e, string? usuario) => new()
    {
        Id = e.Id, Tipo = e.Tipo, Nombre = e.Nombre, Mime = e.Mime, Bytes = e.Bytes,
        Url = $"/api/ordenes/{e.OrdenId}/evidencias/{e.Id}", SubidoPor = usuario, CreadoEn = e.CreadoEn
    };
}
