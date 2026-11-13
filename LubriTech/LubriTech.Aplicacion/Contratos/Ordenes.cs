namespace LubriTech.Aplicacion.Contratos;

// ===========================================================================
// Catalogo de estados
// ===========================================================================

public class EstadoItem
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Secuencia { get; set; }
    public bool EsFinal { get; set; }
    public bool EsAnulacion { get; set; }
}

// ===========================================================================
// Orden: listado y detalle
// ===========================================================================

public class OrdenItem
{
    public long Id { get; set; }
    public int Numero { get; set; }
    public int EstadoId { get; set; }
    public string Estado { get; set; } = string.Empty;
    public bool EstadoFinal { get; set; }
    public int SedeId { get; set; }
    public string? Sede { get; set; }
    public long ClienteId { get; set; }
    public string? Cliente { get; set; }
    public long VehiculoId { get; set; }
    public string? Placa { get; set; }
    public string? Vehiculo { get; set; }
    public int? TecnicoId { get; set; }
    public string? Tecnico { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class OrdenDetalle : OrdenItem
{
    public int? AsesorId { get; set; }
    public string? Asesor { get; set; }
    public int? KilometrajeIngreso { get; set; }
    public string? Diagnostico { get; set; }
    public string? Observaciones { get; set; }
    public DateTimeOffset? FechaCita { get; set; }
    public DateTimeOffset? RecibidoEn { get; set; }
    public DateTimeOffset? EntregadoEn { get; set; }
    public string? AnuladaMotivo { get; set; }
    public decimal TotalServicios { get; set; }
    public decimal TotalRepuestos { get; set; }

    /// <summary>Solo se llena para quien tiene ORDENES.COSTOS.</summary>
    public decimal? CostoRepuestos { get; set; }
    public decimal? Margen { get; set; }

    public List<OrdenServicioItem> Servicios { get; set; } = [];
    public List<OrdenConsumoItem> Consumos { get; set; } = [];
    public List<OrdenChecklistRespuesta> Checklist { get; set; } = [];
    public List<OrdenEvidenciaItem> Evidencias { get; set; } = [];
    public List<OrdenHistorialItem> Historial { get; set; } = [];
}

public class CrearOrdenPeticion
{
    public long ClienteId { get; set; }
    public long VehiculoId { get; set; }
    public int SedeId { get; set; }

    /// <summary>CITA_REGISTRADA para una cita; RECIBIDO para un ingreso directo (por defecto).</summary>
    public string EstadoInicial { get; set; } = "RECIBIDO";

    public int? KilometrajeIngreso { get; set; }
    public int? TecnicoId { get; set; }
    public DateTimeOffset? FechaCita { get; set; }
    public string? Observaciones { get; set; }
}

public class ActualizarOrdenPeticion
{
    public int? KilometrajeIngreso { get; set; }
    public string? Diagnostico { get; set; }
    public string? Observaciones { get; set; }
}

public class CambiarEstadoOrdenPeticion
{
    /// <summary>El codigo del estado destino (RECIBIDO, EN_DIAGNOSTICO, ...).</summary>
    public string Estado { get; set; } = string.Empty;
    public string? Observacion { get; set; }
}

public class AsignarTecnicoPeticion
{
    public int TecnicoId { get; set; }
}

public class AnularOrdenPeticion
{
    public string Motivo { get; set; } = string.Empty;
}

// ===========================================================================
// Servicios de la orden
// ===========================================================================

public class OrdenServicioItem
{
    public long Id { get; set; }
    public long? ServicioId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public decimal Cantidad { get; set; }
    public decimal Subtotal { get; set; }
}

public class AgregarServicioPeticion
{
    /// <summary>Opcional: si viene, el precio y la descripcion salen del catalogo.</summary>
    public long? ServicioId { get; set; }
    public string? Descripcion { get; set; }
    public decimal? Precio { get; set; }
    public decimal Cantidad { get; set; } = 1;
}

// ===========================================================================
// Consumo de repuestos
// ===========================================================================

public class OrdenConsumoItem
{
    public long Id { get; set; }
    public long ProductoId { get; set; }
    public string? Producto { get; set; }
    public string? Codigo { get; set; }
    public int SedeId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal PrecioVenta { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>Solo para quien tiene ORDENES.COSTOS.</summary>
    public decimal? CostoUnitario { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class ConsumirRepuestoPeticion
{
    public long ProductoId { get; set; }
    public int SedeId { get; set; }
    public decimal Cantidad { get; set; }
}

// ===========================================================================
// Checklist
// ===========================================================================

public class ChecklistPlantillaItem
{
    public long Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool Activo { get; set; }
}

public class GuardarChecklistItemPeticion
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
}

public class OrdenChecklistRespuesta
{
    public long Id { get; set; }
    public string Item { get; set; } = string.Empty;
    public string Resultado { get; set; } = "OK";
    public string? Nota { get; set; }
}

public class GuardarChecklistPeticion
{
    public List<RespuestaChecklist> Items { get; set; } = [];
}

public class RespuestaChecklist
{
    public string Item { get; set; } = string.Empty;
    public string Resultado { get; set; } = "OK";     // OK | OBSERVADO | MALO | NA
    public string? Nota { get; set; }
}

// ===========================================================================
// Evidencias
// ===========================================================================

public class OrdenEvidenciaItem
{
    public long Id { get; set; }
    public string Tipo { get; set; } = "FOTO";
    public string? Nombre { get; set; }
    public string Mime { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? SubidoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class SubirEvidenciaPeticion
{
    public string Tipo { get; set; } = "FOTO";        // FOTO | DOCUMENTO
    public string? Nombre { get; set; }
    /// <summary>El contenido del archivo en base64. El servidor valida por bytes que sea imagen real.</summary>
    public string ContenidoBase64 { get; set; } = string.Empty;
}

// ===========================================================================
// Historial
// ===========================================================================

public class OrdenHistorialItem
{
    public long Id { get; set; }
    public string? EstadoAnterior { get; set; }
    public string EstadoNuevo { get; set; } = string.Empty;
    public string? Usuario { get; set; }
    public string? Observacion { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
