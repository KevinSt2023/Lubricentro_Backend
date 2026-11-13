using LubriTech.Dominio.Comun;

namespace LubriTech.Dominio.Entidades;

// ===========================================================================
// Cotizaciones y aprobaciones (Fase 4).
//
// El flujo formal del trabajo adicional: el tecnico registra un hallazgo, el
// asesor arma una cotizacion versionada, el cliente la aprueba o la rechaza y
// esa decision queda inmutable. Sin la decision, el renglon no pasa a
// ejecucion. El ORDEN de esas reglas lo vigila la base; aqui solo se mapea.
// ===========================================================================

/// <summary>
/// Un hallazgo del tecnico durante el diagnostico: algo que hay que cotizar.
/// Es la materia prima de la cotizacion del asesor. Mientras no se atienda, el
/// tecnico lo corrige o lo quita.
/// </summary>
public class OrdenHallazgo : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long OrdenId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public string Severidad { get; set; } = "MODERADO";    // LEVE | MODERADO | CRITICO
    public bool Atendido { get; set; }
    public int? CreadoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Usuario? Autor { get; set; }
}

/// <summary>
/// La cotizacion, VERSIONADA por orden. La version la pone la base (correlativo
/// por orden). Una cotizacion solo se edita mientras es BORRADOR; una vez
/// ENVIADA queda congelada, y solo la decision del cliente la mueve a APROBADA
/// o RECHAZADA.
/// </summary>
public class Cotizacion : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long OrdenId { get; set; }
    public int Version { get; set; }
    public string Estado { get; set; } = "BORRADOR";   // BORRADOR|ENVIADA|APROBADA|RECHAZADA|REEMPLAZADA|VENCIDA
    public decimal Total { get; set; }
    public string Moneda { get; set; } = "PEN";
    public string? Nota { get; set; }
    public DateOnly? VigenciaHasta { get; set; }
    public int? CreadoPor { get; set; }
    public DateTimeOffset? EnviadaEn { get; set; }
    public DateTimeOffset? DecididaEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }

    public Orden? Orden { get; set; }
    public Usuario? Autor { get; set; }
    public CotizacionDecision? Decision { get; set; }
    public List<CotizacionRenglon> Renglones { get; set; } = [];
}

/// <summary>
/// Un renglon de la cotizacion: un servicio o un repuesto. El subtotal lo
/// calcula la base (columna generada); EF lo lee, no lo escribe.
/// </summary>
public class CotizacionRenglon : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long CotizacionId { get; set; }
    public string Tipo { get; set; } = "SERVICIO";     // SERVICIO | REPUESTO
    public long? ServicioId { get; set; }
    public long? ProductoId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Cantidad { get; set; } = 1;
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }              // generado por la base
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>
/// La decision del cliente sobre una cotizacion. Inmutable y unica por
/// cotizacion: es la prueba del criterio 5. Al insertarla, la base pone el
/// estado de la cotizacion; no hay otra forma de aprobar o rechazar.
/// </summary>
public class CotizacionDecision : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long CotizacionId { get; set; }
    public string Decision { get; set; } = string.Empty;   // APROBADA | RECHAZADA
    public long ClienteId { get; set; }
    public string? Comentario { get; set; }
    public string Canal { get; set; } = "PORTAL";
    public string? Ip { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
