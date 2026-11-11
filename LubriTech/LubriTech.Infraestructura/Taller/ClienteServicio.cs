using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using LubriTech.Infraestructura.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// Los clientes del taller: particulares y empresas con flota. La tabla ya
/// existe del nucleo (la comparte el portal); esta fase le pone la pantalla de
/// gestion y, de paso, el enlace al portal.
/// </summary>
public class ClienteServicio(
    LubriTechContexto bd,
    IClaves claves,
    IBitacoraServicio bitacora) : IClienteServicio
{
    public async Task<Pagina<ClienteItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        string? tipo = null, bool? activo = null, CancellationToken ct = default)
    {
        var q = bd.Clientes.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(tipo))
            q = q.Where(c => c.Tipo == tipo);
        if (activo.HasValue)
            q = q.Where(c => c.Activo == activo.Value);

        if (!string.IsNullOrWhiteSpace(consulta.Buscar))
        {
            var b = consulta.Buscar.Trim().ToLower();
            q = q.Where(c =>
                (c.Documento != null && c.Documento.ToLower().Contains(b)) ||
                (c.Nombres != null && c.Nombres.ToLower().Contains(b)) ||
                (c.Apellidos != null && c.Apellidos.ToLower().Contains(b)) ||
                (c.RazonSocial != null && c.RazonSocial.ToLower().Contains(b)) ||
                (c.Telefono != null && c.Telefono.Contains(b)));
        }

        var total = await q.CountAsync(ct);
        var filas = await q
            .OrderByDescending(c => c.Id)
            .Skip(consulta.Saltar).Take(consulta.Tamano)
            .Select(c => new
            {
                Cliente = c,
                Vehiculos = bd.Vehiculos.Count(v => v.ClienteId == c.Id && v.Activo),
                TienePortal = bd.ClienteAccesos.Any(a => a.ClienteId == c.Id && a.Activo)
            })
            .ToListAsync(ct);

        var items = filas.Select(f => Item(f.Cliente, f.Vehiculos, f.TienePortal)).ToList();
        return Pagina<ClienteItem>.De(items, consulta, total);
    }

    public async Task<Resultado<ClienteDetalle>> VerAsync(int empresaId, long clienteId, CancellationToken ct = default)
    {
        var c = await bd.Clientes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == clienteId, ct);
        if (c is null)
            return Resultado<ClienteDetalle>.Error(TipoFallo.NoEncontrado, "El cliente no existe.", "NO_ENCONTRADO");

        return Resultado<ClienteDetalle>.Ok(await DetalleAsync(c, ct));
    }

    public async Task<Resultado<ClienteDetalle>> CrearAsync(
        int empresaId, GuardarClientePeticion peticion, CancellationToken ct = default)
    {
        var falla = Validar(peticion);
        if (falla is not null)
            return Resultado<ClienteDetalle>.Error(TipoFallo.Validacion, falla, "DATOS");

        if (await DocumentoRepetidoAsync(peticion, null, ct))
            return Resultado<ClienteDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay un cliente con ese documento.", "DOCUMENTO_DUPLICADO");

        var cliente = new Cliente { EmpresaId = empresaId, Activo = true };
        Volcar(cliente, peticion);

        bd.Clientes.Add(cliente);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("CLIENTE.ALTA", "cliente", cliente.Id.ToString(),
            new { cliente.Tipo, cliente.Documento, cliente.NombreMostrado }, ct);

        return Resultado<ClienteDetalle>.Ok(await DetalleAsync(cliente, ct));
    }

    public async Task<Resultado<ClienteDetalle>> ActualizarAsync(
        int empresaId, long clienteId, GuardarClientePeticion peticion, CancellationToken ct = default)
    {
        var cliente = await bd.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null)
            return Resultado<ClienteDetalle>.Error(TipoFallo.NoEncontrado, "El cliente no existe.", "NO_ENCONTRADO");

        var falla = Validar(peticion);
        if (falla is not null)
            return Resultado<ClienteDetalle>.Error(TipoFallo.Validacion, falla, "DATOS");

        if (await DocumentoRepetidoAsync(peticion, clienteId, ct))
            return Resultado<ClienteDetalle>.Error(TipoFallo.Conflicto,
                "Ya hay otro cliente con ese documento.", "DOCUMENTO_DUPLICADO");

        Volcar(cliente, peticion);
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("CLIENTE.EDICION", "cliente", clienteId.ToString(),
            new { cliente.Tipo, cliente.Documento }, ct);

        return Resultado<ClienteDetalle>.Ok(await DetalleAsync(cliente, ct));
    }

    public async Task<Resultado<ClienteDetalle>> CambiarEstadoAsync(
        int empresaId, long clienteId, EstadoPeticion peticion, CancellationToken ct = default)
    {
        var cliente = await bd.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null)
            return Resultado<ClienteDetalle>.Error(TipoFallo.NoEncontrado, "El cliente no existe.", "NO_ENCONTRADO");

        cliente.Activo = peticion.Activo;
        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("CLIENTE.ESTADO", "cliente", clienteId.ToString(),
            new { cliente.Activo }, ct);

        return Resultado<ClienteDetalle>.Ok(await DetalleAsync(cliente, ct));
    }

    public async Task<Resultado<PortalHabilitado>> HabilitarPortalAsync(
        int empresaId, long clienteId, CancellationToken ct = default)
    {
        var cliente = await bd.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId, ct);
        if (cliente is null)
            return Resultado<PortalHabilitado>.Error(TipoFallo.NoEncontrado, "El cliente no existe.", "NO_ENCONTRADO");

        if (string.IsNullOrWhiteSpace(cliente.Email))
            return Resultado<PortalHabilitado>.Error(TipoFallo.Validacion,
                "El cliente necesita un correo para abrirle el portal.", "SIN_CORREO");

        var correo = cliente.Email.Trim().ToLower();

        // El correo del portal es unico entre las cuentas de portal.
        if (await bd.ClienteAccesos.IgnoreQueryFilters()
                .AnyAsync(a => a.Email.ToLower() == correo && a.ClienteId != clienteId, ct))
            return Resultado<PortalHabilitado>.Error(TipoFallo.Conflicto,
                "Ese correo ya tiene una cuenta de portal.", "CORREO_EN_USO");

        var ya = await bd.ClienteAccesos.FirstOrDefaultAsync(a => a.ClienteId == clienteId, ct);
        var clave = claves.NuevaClaveTemporal();

        if (ya is null)
        {
            bd.ClienteAccesos.Add(new ClienteAcceso
            {
                ClienteId = clienteId,
                Email = correo,
                PasswordHash = claves.Hash(clave),
                EmailVerificado = false,
                Activo = true
            });
        }
        else
        {
            // Reabrir / reenviar: nueva clave y a cerrar lo que hubiera abierto.
            ya.Email = correo;
            ya.PasswordHash = claves.Hash(clave);
            ya.Activo = true;
            ya.SesionVersion++;
            ya.IntentosFallidos = 0;
            ya.BloqueadoHasta = null;
        }

        await bd.SaveChangesAsync(ct);

        await bitacora.RegistrarAsync("CLIENTE.PORTAL", "cliente", clienteId.ToString(),
            new { correo }, ct);

        return Resultado<PortalHabilitado>.Ok(new PortalHabilitado { Email = correo, ClaveTemporal = clave });
    }

    // ----------------------------------------------------------------------

    private static string? Validar(GuardarClientePeticion p)
    {
        if (p.Tipo == "EMPRESA" && string.IsNullOrWhiteSpace(p.RazonSocial))
            return "Una empresa necesita razon social.";
        if (p.Tipo == "PARTICULAR" && string.IsNullOrWhiteSpace(p.Nombres))
            return "Un particular necesita nombres.";
        return null;
    }

    private async Task<bool> DocumentoRepetidoAsync(GuardarClientePeticion p, long? excepto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(p.Documento)) return false;
        var doc = p.Documento.Trim();
        return await bd.Clientes.AnyAsync(c =>
            c.Documento == doc && c.TipoDocumento == p.TipoDocumento &&
            (excepto == null || c.Id != excepto), ct);
    }

    private static void Volcar(Cliente c, GuardarClientePeticion p)
    {
        c.Tipo = p.Tipo;
        c.TipoDocumento = p.TipoDocumento;
        c.Documento = Limpiar(p.Documento);
        c.Nombres = Limpiar(p.Nombres);
        c.Apellidos = Limpiar(p.Apellidos);
        c.RazonSocial = Limpiar(p.RazonSocial);
        c.Telefono = Limpiar(p.Telefono);
        c.Email = Limpiar(p.Email);
        c.Direccion = Limpiar(p.Direccion);
    }

    private static string? Limpiar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private async Task<ClienteDetalle> DetalleAsync(Cliente c, CancellationToken ct)
    {
        var filas = await bd.Vehiculos.AsNoTracking()
            .Where(v => v.ClienteId == c.Id)
            .OrderByDescending(v => v.Activo).ThenBy(v => v.Placa)
            .ToListAsync(ct);
        var vehiculos = filas.Select(v => VehiculoServicio.Item(v, c.NombreMostrado)).ToList();

        var tienePortal = await bd.ClienteAccesos.AnyAsync(a => a.ClienteId == c.Id && a.Activo, ct);

        var d = new ClienteDetalle
        {
            Nombres = c.Nombres,
            Apellidos = c.Apellidos,
            RazonSocial = c.RazonSocial,
            Direccion = c.Direccion,
            ListaVehiculos = vehiculos
        };
        Rellenar(d, c, vehiculos.Count, tienePortal);
        return d;
    }

    private static ClienteItem Item(Cliente c, int vehiculos, bool portal)
    {
        var i = new ClienteItem();
        Rellenar(i, c, vehiculos, portal);
        return i;
    }

    private static void Rellenar(ClienteItem i, Cliente c, int vehiculos, bool portal)
    {
        i.Id = c.Id;
        i.Tipo = c.Tipo;
        i.TipoDocumento = c.TipoDocumento;
        i.Documento = c.Documento;
        i.Nombre = c.NombreMostrado;
        i.Telefono = c.Telefono;
        i.Email = c.Email;
        i.Activo = c.Activo;
        i.Vehiculos = vehiculos;
        i.TienePortal = portal;
        i.CreadoEn = c.CreadoEn;
    }
}
