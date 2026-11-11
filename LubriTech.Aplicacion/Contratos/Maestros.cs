namespace LubriTech.Aplicacion.Contratos;

// ===========================================================================
// PERSONAL  (sobre la tabla usuario, dentro de la propia empresa)
// ===========================================================================

public class PersonalItem
{
    public int Id { get; set; }
    public string? Dni { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public int RolId { get; set; }
    public string Rol { get; set; } = string.Empty;
    public int? SedeId { get; set; }
    public string? Sede { get; set; }
    public bool Activo { get; set; }
    public bool DebeCambiarClave { get; set; }
    public DateTimeOffset? UltimoAcceso { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class GuardarPersonalPeticion
{
    public string? Dni { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public int RolId { get; set; }
    public int? SedeId { get; set; }
}

public class ActualizarPersonalPeticion
{
    public string? Dni { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public int RolId { get; set; }
    public int? SedeId { get; set; }
}

public class EstadoPeticion
{
    public bool Activo { get; set; }
}

/// <summary>El alta de personal devuelve la clave temporal una sola vez.</summary>
public class PersonalCreado
{
    public PersonalItem Personal { get; set; } = new();
    public string ClaveTemporal { get; set; } = string.Empty;
}

// ===========================================================================
// CLIENTES
// ===========================================================================

public class ClienteItem
{
    public long Id { get; set; }
    public string Tipo { get; set; } = "PARTICULAR";
    public string TipoDocumento { get; set; } = "DNI";
    public string? Documento { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public bool Activo { get; set; }
    public int Vehiculos { get; set; }
    public bool TienePortal { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class ClienteDetalle : ClienteItem
{
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? RazonSocial { get; set; }
    public string? Direccion { get; set; }
    public List<VehiculoItem> ListaVehiculos { get; set; } = [];
}

public class GuardarClientePeticion
{
    public string Tipo { get; set; } = "PARTICULAR";          // PARTICULAR | EMPRESA
    public string TipoDocumento { get; set; } = "DNI";
    public string? Documento { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? RazonSocial { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Direccion { get; set; }
}

/// <summary>Habilitar el portal del cliente devuelve la clave temporal una vez.</summary>
public class PortalHabilitado
{
    public string Email { get; set; } = string.Empty;
    public string ClaveTemporal { get; set; } = string.Empty;
}

// ===========================================================================
// VEHICULOS
// ===========================================================================

public class VehiculoItem
{
    public long Id { get; set; }
    public long ClienteId { get; set; }
    public string? Cliente { get; set; }
    public string Placa { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public int? Anio { get; set; }
    public string Tipo { get; set; } = "AUTO";
    public string? Color { get; set; }
    public int Kilometraje { get; set; }
    public bool Activo { get; set; }
}

public class VehiculoDetalle : VehiculoItem
{
    public string? Vin { get; set; }
    public string? Motor { get; set; }
    public string? Combustible { get; set; }
    public string? Transmision { get; set; }
    public string? Notas { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class GuardarVehiculoPeticion
{
    public long ClienteId { get; set; }
    public string Placa { get; set; } = string.Empty;
    public string Marca { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public int? Anio { get; set; }
    public string Tipo { get; set; } = "AUTO";
    public string? Color { get; set; }
    public string? Vin { get; set; }
    public string? Motor { get; set; }
    public string? Combustible { get; set; }
    public string? Transmision { get; set; }
    public int Kilometraje { get; set; }
    public string? Notas { get; set; }
}

public class KilometrajePeticion
{
    public int Kilometraje { get; set; }
}

// ===========================================================================
// SERVICIOS
// ===========================================================================

public class ServicioItem
{
    public long Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public decimal Precio { get; set; }
    public int? DuracionMin { get; set; }
    public bool Activo { get; set; }
}

public class GuardarServicioPeticion
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string? Descripcion { get; set; }
    public decimal Precio { get; set; }
    public int? DuracionMin { get; set; }
}

// ===========================================================================
// ALMACEN: PRODUCTOS, STOCK Y KARDEX
// ===========================================================================

public class ProductoItem
{
    public long Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string Tipo { get; set; } = "REPUESTO";
    public string Unidad { get; set; } = "UND";
    public decimal PrecioVenta { get; set; }
    public decimal StockMinimo { get; set; }
    public bool ControlaStock { get; set; }
    public bool Activo { get; set; }

    /// <summary>Suma del stock en todas las sedes. La foto rapida del listado.</summary>
    public decimal StockTotal { get; set; }
    public bool BajoMinimo { get; set; }
}

public class ProductoDetalle : ProductoItem
{
    public string? Marca { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public List<StockSedeItem> Stock { get; set; } = [];
}

public class StockSedeItem
{
    public int SedeId { get; set; }
    public string Sede { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal CostoPromedio { get; set; }
}

public class GuardarProductoPeticion
{
    public string Codigo { get; set; } = string.Empty;
    public string? CodigoBarras { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string Tipo { get; set; } = "REPUESTO";
    public string Unidad { get; set; } = "UND";
    public string? Marca { get; set; }
    public decimal PrecioVenta { get; set; }
    public decimal StockMinimo { get; set; }
    public bool ControlaStock { get; set; } = true;
}

/// <summary>Un ingreso de mercaderia a una sede.</summary>
public class IngresoStockPeticion
{
    public int SedeId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public string? Referencia { get; set; }
    public string? Motivo { get; set; }
}

/// <summary>
/// Un ajuste de inventario: deja el stock de una sede EN el valor contado. El
/// servicio calcula si eso es un movimiento positivo o negativo.
/// </summary>
public class AjusteStockPeticion
{
    public int SedeId { get; set; }
    public decimal CantidadReal { get; set; }
    public string Motivo { get; set; } = string.Empty;
}

public class MovimientoKardexItem
{
    public long Id { get; set; }
    public long ProductoId { get; set; }
    public string? Producto { get; set; }
    public int SedeId { get; set; }
    public string? Sede { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal Saldo { get; set; }
    public string? Referencia { get; set; }
    public string? Motivo { get; set; }
    public string? Usuario { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
