using LubriTech.Dominio.Comun;

namespace LubriTech.Dominio.Entidades;

// ===========================================================================
// Ordenes de trabajo (Fase 3). El corazon del sistema.
// ===========================================================================

/// <summary>
/// Un estado del flujo de la orden. Catalogo GLOBAL (igual para todas las
/// empresas), como rol o permiso. La orden sube de a una <see cref="Secuencia"/>;
/// Anulada va fuera de linea (secuencia 99, es_anulacion).
/// </summary>
public class OrdenEstado
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Secuencia { get; set; }
    public bool EsFinal { get; set; }
    public bool EsAnulacion { get; set; }
}

/// <summary>
/// La orden de trabajo. El numero es correlativo por empresa (lo pone la base).
/// El estado avanza de a uno, lo vigila un trigger. Los totales los recalcula
/// el servicio con cada servicio o repuesto agregado.
/// </summary>
public class Orden : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public int SedeId { get; set; }
    public int Numero { get; set; }
    public long ClienteId { get; set; }
    public long VehiculoId { get; set; }
    public int EstadoId { get; set; }
    public int? AsesorId { get; set; }
    public int? TecnicoId { get; set; }
    public int? KilometrajeIngreso { get; set; }
    public string? Diagnostico { get; set; }
    public string? Observaciones { get; set; }
    public DateTimeOffset? FechaCita { get; set; }
    public DateTimeOffset? RecibidoEn { get; set; }
    public DateTimeOffset? EntregadoEn { get; set; }
    public decimal TotalServicios { get; set; }
    public decimal TotalRepuestos { get; set; }
    public decimal Total { get; set; }
    public string? AnuladaMotivo { get; set; }
    public int? CreadoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }

    public OrdenEstado? Estado { get; set; }
    public Sede? Sede { get; set; }
    public Cliente? Cliente { get; set; }
    public Vehiculo? Vehiculo { get; set; }
    public Usuario? Tecnico { get; set; }
    public Usuario? Asesor { get; set; }

    public List<OrdenServicio> Servicios { get; set; } = [];
    public List<OrdenConsumo> Consumos { get; set; } = [];
    public List<OrdenChecklist> Checklist { get; set; } = [];
    public List<OrdenEvidencia> Evidencias { get; set; } = [];
}

/// <summary>
/// El rastro de la orden: cada cambio de estado, con quien y cuando. Inmutable;
/// es la prueba del criterio de auditoria.
/// </summary>
public class OrdenHistorial : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long OrdenId { get; set; }
    public int? EstadoAnteriorId { get; set; }
    public int EstadoNuevoId { get; set; }
    public int? UsuarioId { get; set; }
    public string? Observacion { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public OrdenEstado? EstadoAnterior { get; set; }
    public OrdenEstado? EstadoNuevo { get; set; }
    public Usuario? Usuario { get; set; }
}

/// <summary>Un punto de la plantilla de inspeccion. Editable por empresa.</summary>
public class ChecklistItem : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>
/// Una respuesta del checklist de una orden. El nombre del punto se guarda como
/// copia: editar la plantilla no reescribe inspecciones ya hechas.
/// </summary>
public class OrdenChecklist : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long OrdenId { get; set; }
    public string Item { get; set; } = string.Empty;
    public string Resultado { get; set; } = "OK";     // OK | OBSERVADO | MALO | NA
    public string? Nota { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>Un servicio aplicado a la orden. El precio se congela al agregarlo.</summary>
public class OrdenServicio : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long OrdenId { get; set; }
    public long? ServicioId { get; set; }
    public string Descripcion { get; set; } = string.Empty;
    public decimal Precio { get; set; }
    public decimal Cantidad { get; set; } = 1;
    /// <summary>Si el renglon nace de una cotizacion aprobada, el vinculo a su renglon.</summary>
    public long? CotizacionRenglonId { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>
/// Un repuesto consumido en la orden. Inmutable: ya movio el stock. Deshacerlo
/// es un ingreso o un ajuste, no un borrado. Guarda el vinculo al kardex.
/// </summary>
public class OrdenConsumo : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long OrdenId { get; set; }
    public long ProductoId { get; set; }
    public int SedeId { get; set; }
    public decimal Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal PrecioVenta { get; set; }
    public long? KardexId { get; set; }
    /// <summary>Si el consumo nace de una cotizacion aprobada, el vinculo a su renglon.</summary>
    public long? CotizacionRenglonId { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Producto? Producto { get; set; }
}

/// <summary>
/// Una foto o documento de la orden. El archivo vive en disco; aqui queda la
/// metadata, incluido el hash y el tamano validados por bytes al subir.
/// </summary>
public class OrdenEvidencia : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public long OrdenId { get; set; }
    public string Tipo { get; set; } = "FOTO";        // FOTO | DOCUMENTO
    public string Archivo { get; set; } = string.Empty;
    public string? Nombre { get; set; }
    public string Mime { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string? Hash { get; set; }
    public int? SubidoPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
