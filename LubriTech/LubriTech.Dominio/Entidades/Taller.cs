using LubriTech.Dominio.Comun;

namespace LubriTech.Dominio.Entidades;

// ===========================================================================
// Maestros del taller (Fase 2). Personal y clientes viven en usuario/cliente;
// aqui estan los que esta fase agrega: vehiculos, servicios y almacen.
// ===========================================================================

/// <summary>
/// Un vehiculo del cliente. La placa lo identifica dentro de la empresa. El
/// kilometraje solo sube (lo blinda un trigger): es el eje de los proximos
/// mantenimientos.
/// </summary>
public class Vehiculo : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
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
    /// <summary>Cuándo toca el próximo mantenimiento (por calendario). Opcional.</summary>
    public DateOnly? ProximoMantFecha { get; set; }
    /// <summary>A qué kilometraje toca el próximo mantenimiento. Opcional.</summary>
    public int? ProximoMantKm { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }

    public Cliente? Cliente { get; set; }
}

/// <summary>Un servicio del catalogo del taller (cambio de aceite, afinamiento...).</summary>
public class Servicio : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Categoria { get; set; }
    public string? Descripcion { get; set; }
    public decimal Precio { get; set; }
    public int? DuracionMin { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }
}

/// <summary>
/// Un producto del almacen: repuesto o insumo. El stock NO vive aqui: vive en
/// producto_stock, una fila por sede, y lo mueve el kardex. Esto es la ficha.
/// </summary>
public class Producto : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
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
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }

    public List<ProductoStock> Stocks { get; set; } = [];
}

/// <summary>
/// El stock de un producto en una sede. Lo escribe SOLO el trigger del kardex;
/// la aplicacion nunca lo toca directo (hay un candado en la base que lo
/// impide). No lleva la marca IEntidadEmpresa porque cuelga de producto y
/// sede, que ya estan filtrados.
/// </summary>
public class ProductoStock
{
    public long ProductoId { get; set; }
    public int SedeId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CostoPromedio { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public Producto? Producto { get; set; }
    public Sede? Sede { get; set; }
}

/// <summary>
/// El libro mayor del almacen. Inmutable: insertar una fila ES mover el stock
/// (lo hace el trigger). La cantidad es siempre positiva; el signo lo pone el
/// tipo. El saldo lo calcula la base, no la aplicacion.
/// </summary>
public class Kardex : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long ProductoId { get; set; }
    public int SedeId { get; set; }
    public string Tipo { get; set; } = string.Empty;   // INGRESO | SALIDA | AJUSTE_POS | AJUSTE_NEG
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal Saldo { get; set; }
    public string? Referencia { get; set; }
    public string? Motivo { get; set; }
    public int? UsuarioId { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Producto? Producto { get; set; }
    public Sede? Sede { get; set; }
}
