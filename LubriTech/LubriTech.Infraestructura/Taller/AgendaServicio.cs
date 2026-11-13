using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// La agenda. Una cita es una orden en CITA_REGISTRADA con fecha_cita. El alta,
/// la recepción y la cancelación se apoyan en <see cref="IOrdenServicio"/> para
/// reusar su historial y su bitácora; aquí solo viven la consulta del calendario
/// y la reprogramación.
/// </summary>
public class AgendaServicio(
    LubriTechContexto bd,
    IOrdenServicio ordenes,
    IBitacoraServicio bitacora) : IAgendaServicio
{
    private const string Cita = "CITA_REGISTRADA";

    public async Task<Resultado<List<AgendaCitaItem>>> ListarAsync(
        int empresaId, DateTimeOffset desde, DateTimeOffset hasta,
        int? sedeId, int? tecnicoId, CancellationToken ct = default)
    {
        // timestamptz solo acepta instantes en UTC. El eje de tiempo se guarda
        // en UTC real; aqui se normaliza el rango que llega con offset de Lima.
        desde = desde.ToUniversalTime();
        hasta = hasta.ToUniversalTime();

        var q = bd.Ordenes.AsNoTracking()
            .Where(o => o.Estado!.Codigo == Cita
                && o.FechaCita != null && o.FechaCita >= desde && o.FechaCita <= hasta);

        if (sedeId.HasValue) q = q.Where(o => o.SedeId == sedeId.Value);
        if (tecnicoId.HasValue) q = q.Where(o => o.TecnicoId == tecnicoId.Value);

        var filas = await q
            .OrderBy(o => o.FechaCita)
            .Select(o => new
            {
                o.Id, o.Numero, o.FechaCita, o.SedeId, Sede = o.Sede!.Nombre,
                o.ClienteId,
                Cliente = o.Cliente!.RazonSocial ?? (o.Cliente.Nombres + " " + o.Cliente.Apellidos),
                o.Cliente!.Telefono,
                o.VehiculoId, o.Vehiculo!.Placa,
                Vehiculo = o.Vehiculo.Marca + " " + o.Vehiculo.Modelo,
                o.TecnicoId,
                Tecnico = o.Tecnico != null ? o.Tecnico.Nombres + " " + o.Tecnico.Apellidos : null,
                o.Observaciones, o.CreadoEn
            })
            .ToListAsync(ct);

        var items = filas.Select(f => new AgendaCitaItem
        {
            Id = f.Id, Numero = f.Numero, FechaCita = f.FechaCita!.Value,
            SedeId = f.SedeId, Sede = f.Sede,
            ClienteId = f.ClienteId, Cliente = (f.Cliente ?? "").Trim(), Telefono = f.Telefono,
            VehiculoId = f.VehiculoId, Placa = f.Placa, Vehiculo = f.Vehiculo.Trim(),
            TecnicoId = f.TecnicoId, Tecnico = f.Tecnico?.Trim(),
            Motivo = f.Observaciones, CreadoEn = f.CreadoEn
        }).ToList();

        return Resultado<List<AgendaCitaItem>>.Ok(items);
    }

    public Task<Resultado<OrdenDetalle>> CrearCitaAsync(
        int empresaId, int? usuarioId, CrearCitaPeticion peticion, bool puedeVerCostos, CancellationToken ct = default) =>
        ordenes.CrearAsync(empresaId, usuarioId, new CrearOrdenPeticion
        {
            ClienteId = peticion.ClienteId,
            VehiculoId = peticion.VehiculoId,
            SedeId = peticion.SedeId,
            EstadoInicial = Cita,
            FechaCita = peticion.FechaCita.ToUniversalTime(),
            TecnicoId = peticion.TecnicoId,
            Observaciones = peticion.Motivo,
        }, puedeVerCostos, ct);

    public async Task<Resultado<AgendaCitaItem>> ReprogramarAsync(
        int empresaId, int? usuarioId, long ordenId, ReprogramarCitaPeticion peticion, CancellationToken ct = default)
    {
        var orden = await bd.Ordenes.Include(o => o.Estado).FirstOrDefaultAsync(o => o.Id == ordenId, ct);
        if (orden is null)
            return Resultado<AgendaCitaItem>.Error(TipoFallo.NoEncontrado, "La cita no existe.", "NO_ENCONTRADO");

        if (orden.Estado!.Codigo != Cita)
            return Resultado<AgendaCitaItem>.Error(TipoFallo.Conflicto,
                "Solo se reprograma una cita que todavía no ha sido recibida.", "NO_ES_CITA");

        if (peticion.TecnicoId is int tec
            && !await bd.Usuarios.AnyAsync(u => u.Id == tec && u.EmpresaId == empresaId && u.Activo && u.RolId > 0, ct))
            return Resultado<AgendaCitaItem>.Error(TipoFallo.Validacion, "El técnico no es de esta empresa.", "TECNICO_INVALIDO");

        orden.FechaCita = peticion.FechaCita.ToUniversalTime();
        if (peticion.TecnicoId.HasValue) orden.TecnicoId = peticion.TecnicoId;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("AGENDA.REPROGRAMA", "orden", ordenId.ToString(),
            new { orden.Numero, fecha = peticion.FechaCita }, ct);

        var citas = await ListarAsync(empresaId, orden.FechaCita!.Value, orden.FechaCita!.Value, null, null, ct);
        var item = citas.Valor?.FirstOrDefault(c => c.Id == ordenId);
        return item is null
            ? Resultado<AgendaCitaItem>.Error(TipoFallo.NoEncontrado, "No se pudo releer la cita.", "NO_ENCONTRADO")
            : Resultado<AgendaCitaItem>.Ok(item);
    }

    public async Task<Resultado<OrdenDetalle>> RecibirAsync(
        int empresaId, int? usuarioId, long ordenId, RecibirCitaPeticion peticion, bool puedeVerCostos, CancellationToken ct = default)
    {
        var estado = await bd.Ordenes.AsNoTracking()
            .Where(o => o.Id == ordenId).Select(o => o.Estado!.Codigo).FirstOrDefaultAsync(ct);
        if (estado is null)
            return Resultado<OrdenDetalle>.Error(TipoFallo.NoEncontrado, "La cita no existe.", "NO_ENCONTRADO");
        if (estado != Cita)
            return Resultado<OrdenDetalle>.Error(TipoFallo.Conflicto, "Esta orden no es una cita por recibir.", "NO_ES_CITA");

        if (peticion.KilometrajeIngreso.HasValue)
        {
            var km = await ordenes.ActualizarAsync(empresaId, ordenId,
                new ActualizarOrdenPeticion { KilometrajeIngreso = peticion.KilometrajeIngreso }, puedeVerCostos, ct);
            if (!km.Exito) return km;
        }

        return await ordenes.CambiarEstadoAsync(empresaId, usuarioId, ordenId,
            new CambiarEstadoOrdenPeticion { Estado = "RECIBIDO", Observacion = "Vehículo recibido (check-in de la cita)" },
            puedeVerCostos, ct);
    }

    public Task<Resultado<OrdenDetalle>> CancelarAsync(
        int empresaId, int? usuarioId, long ordenId, AnularOrdenPeticion peticion, bool puedeVerCostos, CancellationToken ct = default) =>
        ordenes.AnularAsync(empresaId, usuarioId, ordenId, peticion, puedeVerCostos, ct);

    public async Task<Resultado<List<ProximoMantenimientoItem>>> ProximosMantenimientosAsync(
        int empresaId, int dias, int km, CancellationToken ct = default)
    {
        var hoy = Tiempo.HoyLima();
        var limiteFecha = hoy.AddDays(dias);

        var filas = await bd.Vehiculos.AsNoTracking()
            .Where(v => v.Activo
                && (v.ProximoMantFecha != null || v.ProximoMantKm != null)
                && ((v.ProximoMantFecha != null && v.ProximoMantFecha <= limiteFecha)
                    || (v.ProximoMantKm != null && v.ProximoMantKm <= v.Kilometraje + km)))
            .Select(v => new
            {
                v.Id, v.Placa, Vehiculo = v.Marca + " " + v.Modelo, v.ClienteId,
                Cliente = v.Cliente!.RazonSocial ?? (v.Cliente.Nombres + " " + v.Cliente.Apellidos),
                v.Cliente!.Telefono, v.Kilometraje, v.ProximoMantFecha, v.ProximoMantKm
            })
            .ToListAsync(ct);

        var items = filas.Select(f =>
        {
            int? dr = f.ProximoMantFecha is DateOnly pf ? pf.DayNumber - hoy.DayNumber : null;
            int? kr = f.ProximoMantKm is int pk ? pk - f.Kilometraje : null;
            return new ProximoMantenimientoItem
            {
                VehiculoId = f.Id, Placa = f.Placa, Vehiculo = f.Vehiculo.Trim(),
                ClienteId = f.ClienteId, Cliente = (f.Cliente ?? "").Trim(), Telefono = f.Telefono,
                Kilometraje = f.Kilometraje,
                ProximoMantFecha = f.ProximoMantFecha, ProximoMantKm = f.ProximoMantKm,
                DiasRestantes = dr, KmRestantes = kr,
                Vencido = (dr is <= 0) || (kr is <= 0),
            };
        })
        .OrderByDescending(x => x.Vencido)
        .ThenBy(x => x.DiasRestantes ?? int.MaxValue)
        .ThenBy(x => x.KmRestantes ?? int.MaxValue)
        .ToList();

        return Resultado<List<ProximoMantenimientoItem>>.Ok(items);
    }
}
