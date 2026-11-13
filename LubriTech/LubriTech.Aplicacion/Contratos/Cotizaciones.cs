namespace LubriTech.Aplicacion.Contratos;

// ===========================================================================
// Hallazgos del tecnico
// ===========================================================================

public class HallazgoItem
{
    public long Id { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string Severidad { get; set; } = "MODERADO";
    public bool Atendido { get; set; }
    public string? Autor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class RegistrarHallazgoPeticion
{
    public string Descripcion { get; set; } = string.Empty;
    public string Severidad { get; set; } = "MODERADO";   // LEVE | MODERADO | CRITICO
}

// ===========================================================================
// Cotizacion: listado y detalle
// ===========================================================================

public class CotizacionItem
{
    public long Id { get; set; }
    public long OrdenId { get; set; }
    public int NumeroOrden { get; set; }
    public int Version { get; set; }
    public string Estado { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Moneda { get; set; } = "PEN";
    public DateOnly? VigenciaHasta { get; set; }
    public DateTimeOffset? EnviadaEn { get; set; }
    public DateTimeOffset? DecididaEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class CotizacionDetalle : CotizacionItem
{
    public string? Nota { get; set; }
    public string? Autor { get; set; }

    /// <summary>Datos del vehiculo y cliente, para que el cliente sepa que esta aprobando.</summary>
    public string? Placa { get; set; }
    public string? Vehiculo { get; set; }
    public string? Cliente { get; set; }

    public List<CotizacionRenglonItem> Renglones { get; set; } = [];
    public CotizacionDecisionItem? Decision { get; set; }
}

public class CotizacionRenglonItem
{
    public long Id { get; set; }
    public string Tipo { get; set; } = "SERVICIO";
    public long? ServicioId { get; set; }
    public long? ProductoId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
}

public class CotizacionDecisionItem
{
    public string Decision { get; set; } = string.Empty;
    public string? Comentario { get; set; }
    public string Canal { get; set; } = "PORTAL";
    public DateTimeOffset CreadoEn { get; set; }
}

// ===========================================================================
// Crear / editar (asesor)
// ===========================================================================

public class GuardarCotizacionPeticion
{
    public string? Nota { get; set; }
    public DateOnly? VigenciaHasta { get; set; }
    public List<RenglonCotizacionPeticion> Renglones { get; set; } = [];
}

public class RenglonCotizacionPeticion
{
    public string Tipo { get; set; } = "SERVICIO";       // SERVICIO | REPUESTO
    public long? ServicioId { get; set; }
    public long? ProductoId { get; set; }
    public string? Descripcion { get; set; }
    public decimal Cantidad { get; set; } = 1;
    public decimal? PrecioUnitario { get; set; }
}

// ===========================================================================
// Decision del cliente (portal)
// ===========================================================================

public class DecidirCotizacionPeticion
{
    /// <summary>APROBADA o RECHAZADA.</summary>
    public string Decision { get; set; } = string.Empty;
    public string? Comentario { get; set; }
}
