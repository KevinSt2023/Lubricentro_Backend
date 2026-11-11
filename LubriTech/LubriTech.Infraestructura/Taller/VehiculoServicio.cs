using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// Los vehiculos del taller. Cuelgan de un cliente. La placa es unica dentro
/// de la empresa, y el kilometraje solo sube: lo blinda un trigger de la base,
/// pero aqui se comprueba antes para dar un mensaje claro.
/// </summary>
public class VehiculoServicio(
    LubriTechContexto bd,
    IBitacoraServicio bitacora) : IVehiculoServicio
{
    public async Task<Pagina<VehiculoItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        long? clienteId = null, bool? activo = null, CancellationToken ct = default)
    {
        var q = bd.Vehiculos.AsNoTracking().AsQueryable();

        if (clienteId.HasValue) q = q.Where(v => v.ClienteId == clienteId.Value);
        if (activo.HasValue) q = q.Where(v => v.Activo == activo.Value);

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var b = consulta.Buscar.Trim().ToLower();
            q = q.Where(v => v.Placa.ToLower().Contains(b) ||
                             v.Marca.ToLower().Contains(b) ||
                             v.Modelo.ToLower().Contains(b));
        }

        var total = await q.CountAsync(ct);
        var filas = await q
            .OrderByDescending(v => v.Id)
            .Skip(consulta.Saltar).Take(consulta.Tamano)
            .Select(v => new { V = v, Cliente = v.Cliente!.RazonSocial ?? (v.Cliente.Nombres + " " + v.Cliente.Apellidos) })
            .ToListAsync(ct);

        var items = filas.Select(f => Item(f.V, (f.Cliente ?? "").Trim())).ToList();
        return Pagina<VehiculoItem>.De(items, consulta, total);
    }

    public async Task<Resultado<VehiculoDetalle>> VerAsync(int empresaId, long vehiculoId, CancellationToken ct = default)
    {
        var v = await bd.Vehiculos.AsNoTracking().Include(x => x.Cliente)
            .FirstOrDefaultAsync(x => x.Id == vehiculoId, ct);
        return v is null
            ? Resultado<VehiculoDetalle>.Error(TipoFallo.NoEncontrado, "El vehiculo no existe.", "NO_ENCONTRADO")
            : Resultado<VehiculoDetalle>.Ok(Detalle(v));
    }

    public async Task<Resultado<VehiculoDetalle>> CrearAsync(
        int empresaId, GuardarVehiculoPeticion peticion, CancellationToken ct = default)
    {
        var cliente = await bd.Clientes.FirstOrDefaultAsync(c => c.Id == peticion.ClienteId, ct);
        if (cliente is null)
            return Resultado<VehiculoDetalle>.Error(TipoFallo.Validacion,
                "El cliente del vehiculo no existe.", "CLIENTE_INVALIDO");

        var placa = peticion.Placa.Trim().ToUpper();
        if (await bd.Vehiculos.AnyAsync(v => v.Placa.ToUpper() == placa, ct))
            return Resultado<VehiculoDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay un vehiculo con esa placa.", "PLACA_DUPLICADA");

        if (peticion.Kilometraje < 0)
            return Resultado<VehiculoDetalle>.Error(TipoFallo.Validacion,
                "El kilometraje no puede ser negativo.", "KM_INVALIDO");

        var vehiculo = new Vehiculo { EmpresaId = empresaId, Activo = true };
        Volcar(vehiculo, peticion, placa);

        bd.Vehiculos.Add(vehiculo);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("VEHICULO.ALTA", "vehiculo", vehiculo.Id.ToString(),
            new { vehiculo.Placa, vehiculo.Marca, vehiculo.Modelo, clienteId = peticion.ClienteId }, ct);

        vehiculo.Cliente = cliente;
        return Resultado<VehiculoDetalle>.Ok(Detalle(vehiculo));
    }

    public async Task<Resultado<VehiculoDetalle>> ActualizarAsync(
        int empresaId, long vehiculoId, GuardarVehiculoPeticion peticion, CancellationToken ct = default)
    {
        var vehiculo = await bd.Vehiculos.Include(x => x.Cliente)
            .FirstOrDefaultAsync(v => v.Id == vehiculoId, ct);
        if (vehiculo is null)
            return Resultado<VehiculoDetalle>.Error(TipoFallo.NoEncontrado, "El vehiculo no existe.", "NO_ENCONTRADO");

        var placa = peticion.Placa.Trim().ToUpper();
        if (await bd.Vehiculos.AnyAsync(v => v.Id != vehiculoId && v.Placa.ToUpper() == placa, ct))
            return Resultado<VehiculoDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay otro vehiculo con esa placa.", "PLACA_DUPLICADA");

        // El cliente se puede corregir, pero tiene que existir.
        if (peticion.ClienteId != vehiculo.ClienteId)
        {
            var nuevo = await bd.Clientes.FirstOrDefaultAsync(c => c.Id == peticion.ClienteId, ct);
            if (nuevo is null)
                return Resultado<VehiculoDetalle>.Error(TipoFallo.Validacion,
                    "El cliente del vehiculo no existe.", "CLIENTE_INVALIDO");
            vehiculo.Cliente = nuevo;
        }

        // El kilometraje no baja: por aqui solo se corrigen datos, no se anda
        // hacia atras. Para subirlo esta RegistrarKilometraje.
        if (peticion.Kilometraje < vehiculo.Kilometraje)
            return Resultado<VehiculoDetalle>.Error(TipoFallo.Conflicto,
                $"El kilometraje no puede bajar de {vehiculo.Kilometraje}.", "KM_NO_BAJA");

        Volcar(vehiculo, peticion, placa);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("VEHICULO.EDICION", "vehiculo", vehiculoId.ToString(),
            new { vehiculo.Placa }, ct);

        return Resultado<VehiculoDetalle>.Ok(Detalle(vehiculo));
    }

    public async Task<Resultado<VehiculoDetalle>> CambiarEstadoAsync(
        int empresaId, long vehiculoId, EstadoPeticion peticion, CancellationToken ct = default)
    {
        var vehiculo = await bd.Vehiculos.Include(x => x.Cliente)
            .FirstOrDefaultAsync(v => v.Id == vehiculoId, ct);
        if (vehiculo is null)
            return Resultado<VehiculoDetalle>.Error(TipoFallo.NoEncontrado, "El vehiculo no existe.", "NO_ENCONTRADO");

        vehiculo.Activo = peticion.Activo;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("VEHICULO.ESTADO", "vehiculo", vehiculoId.ToString(),
            new { vehiculo.Activo }, ct);

        return Resultado<VehiculoDetalle>.Ok(Detalle(vehiculo));
    }

    public async Task<Resultado<VehiculoDetalle>> RegistrarKilometrajeAsync(
        int empresaId, long vehiculoId, KilometrajePeticion peticion, CancellationToken ct = default)
    {
        var vehiculo = await bd.Vehiculos.Include(x => x.Cliente)
            .FirstOrDefaultAsync(v => v.Id == vehiculoId, ct);
        if (vehiculo is null)
            return Resultado<VehiculoDetalle>.Error(TipoFallo.NoEncontrado, "El vehiculo no existe.", "NO_ENCONTRADO");

        if (peticion.Kilometraje < vehiculo.Kilometraje)
            return Resultado<VehiculoDetalle>.Error(TipoFallo.Conflicto,
                $"El kilometraje no puede bajar de {vehiculo.Kilometraje}.", "KM_NO_BAJA");

        var antes = vehiculo.Kilometraje;
        vehiculo.Kilometraje = peticion.Kilometraje;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("VEHICULO.KILOMETRAJE", "vehiculo", vehiculoId.ToString(),
            new { antes, ahora = peticion.Kilometraje }, ct);

        return Resultado<VehiculoDetalle>.Ok(Detalle(vehiculo));
    }

    // ----------------------------------------------------------------------

    private static void Volcar(Vehiculo v, GuardarVehiculoPeticion p, string placa)
    {
        v.ClienteId = p.ClienteId;
        v.Placa = placa;
        v.Marca = p.Marca.Trim();
        v.Modelo = p.Modelo.Trim();
        v.Anio = p.Anio;
        v.Tipo = p.Tipo;
        v.Color = Limpiar(p.Color);
        v.Vin = Limpiar(p.Vin);
        v.Motor = Limpiar(p.Motor);
        v.Combustible = Limpiar(p.Combustible);
        v.Transmision = Limpiar(p.Transmision);
        v.Kilometraje = p.Kilometraje;
        v.Notas = Limpiar(p.Notas);
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>Proyeccion de lista. Publica: la reusa ClienteServicio.</summary>
    public static VehiculoItem Item(Vehiculo v, string? cliente) => new()
    {
        Id = v.Id,
        ClienteId = v.ClienteId,
        Cliente = cliente,
        Placa = v.Placa,
        Marca = v.Marca,
        Modelo = v.Modelo,
        Anio = v.Anio,
        Tipo = v.Tipo,
        Color = v.Color,
        Kilometraje = v.Kilometraje,
        Activo = v.Activo
    };

    private static VehiculoDetalle Detalle(Vehiculo v)
    {
        var cliente = v.Cliente?.NombreMostrado;
        return new VehiculoDetalle
        {
            Id = v.Id,
            ClienteId = v.ClienteId,
            Cliente = cliente,
            Placa = v.Placa,
            Marca = v.Marca,
            Modelo = v.Modelo,
            Anio = v.Anio,
            Tipo = v.Tipo,
            Color = v.Color,
            Kilometraje = v.Kilometraje,
            Activo = v.Activo,
            Vin = v.Vin,
            Motor = v.Motor,
            Combustible = v.Combustible,
            Transmision = v.Transmision,
            Notas = v.Notas,
            CreadoEn = v.CreadoEn
        };
    }
}
