using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Servicios;

/// <summary>
/// Las ordenes de trabajo. Tenant-scoped: la empresa sale del token. Quien
/// puede mover cada estado lo deciden los permisos del controlador; el ORDEN
/// de los estados (de a uno, sin saltos, Anulada aparte) lo vigila la base.
///
/// <paramref name="usuarioId"/> y <paramref name="puedeVerCostos"/> se pasan
/// desde el controlador (salen de la sesion): el primero firma el historial y
/// los consumos; el segundo decide si la respuesta lleva costos y margen.
/// </summary>
public interface IOrdenServicio
{
    Task<List<EstadoItem>> EstadosAsync(CancellationToken ct = default);

    Task<Pagina<OrdenItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        int? estadoId = null, int? tecnicoId = null, int? sedeId = null,
        long? clienteId = null, long? vehiculoId = null,
        int? soloDelTecnico = null, CancellationToken ct = default);

    Task<Resultado<OrdenDetalle>> VerAsync(
        int empresaId, long ordenId, bool puedeVerCostos, CancellationToken ct = default);

    Task<Resultado<OrdenDetalle>> CrearAsync(
        int empresaId, int? usuarioId, CrearOrdenPeticion peticion, bool puedeVerCostos, CancellationToken ct = default);

    Task<Resultado<OrdenDetalle>> ActualizarAsync(
        int empresaId, long ordenId, ActualizarOrdenPeticion peticion, bool puedeVerCostos, CancellationToken ct = default);

    /// <summary>Mueve el estado. La base valida la transicion; aqui se escribe el historial.</summary>
    Task<Resultado<OrdenDetalle>> CambiarEstadoAsync(
        int empresaId, int? usuarioId, long ordenId, CambiarEstadoOrdenPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default);

    Task<Resultado<OrdenDetalle>> AsignarTecnicoAsync(
        int empresaId, int? usuarioId, long ordenId, AsignarTecnicoPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default);

    Task<Resultado<OrdenDetalle>> AnularAsync(
        int empresaId, int? usuarioId, long ordenId, AnularOrdenPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default);

    // --- Servicios de la orden ---
    Task<Resultado<OrdenDetalle>> AgregarServicioAsync(
        int empresaId, long ordenId, AgregarServicioPeticion peticion, bool puedeVerCostos, CancellationToken ct = default);

    Task<Resultado<OrdenDetalle>> QuitarServicioAsync(
        int empresaId, long ordenId, long servicioLineaId, bool puedeVerCostos, CancellationToken ct = default);

    // --- Consumo de repuestos (mueve kardex) ---
    /// <summary>
    /// Consume un repuesto. Solo con la orden En ejecucion. Escribe la SALIDA
    /// de kardex y la linea de consumo en una sola transaccion; si no hay
    /// stock, la base lo rechaza y no queda nada a medias.
    /// </summary>
    Task<Resultado<OrdenDetalle>> ConsumirRepuestoAsync(
        int empresaId, int? usuarioId, long ordenId, ConsumirRepuestoPeticion peticion,
        bool puedeVerCostos, CancellationToken ct = default);

    // --- Checklist de la orden ---
    Task<Resultado<OrdenDetalle>> GuardarChecklistAsync(
        int empresaId, long ordenId, GuardarChecklistPeticion peticion, bool puedeVerCostos, CancellationToken ct = default);

    // --- Evidencias ---
    /// <summary>
    /// Sube una foto o documento. El controlador ya valido por bytes que el
    /// contenido sea lo que dice ser y lo guardo en disco; aqui entra la
    /// metadata ya verificada.
    /// </summary>
    Task<Resultado<OrdenEvidenciaItem>> RegistrarEvidenciaAsync(
        int empresaId, int? usuarioId, long ordenId,
        string tipo, string archivo, string? nombre, string mime, long bytes, string hash,
        CancellationToken ct = default);

    Task<Resultado<(string Archivo, string Mime)>> EvidenciaArchivoAsync(
        int empresaId, long ordenId, long evidenciaId, CancellationToken ct = default);

    // --- Plantilla de checklist (configuracion por empresa) ---
    Task<List<ChecklistPlantillaItem>> PlantillaAsync(int empresaId, bool soloActivos, CancellationToken ct = default);

    Task<Resultado<ChecklistPlantillaItem>> GuardarPlantillaItemAsync(
        int empresaId, long? itemId, GuardarChecklistItemPeticion peticion, CancellationToken ct = default);

    Task<Resultado<ChecklistPlantillaItem>> CambiarEstadoPlantillaItemAsync(
        int empresaId, long itemId, bool activo, CancellationToken ct = default);
}

/// <summary>
/// Donde viven fisicamente las evidencias. Lo implementa la infraestructura
/// (disco local en dev). La orden solo guarda la ruta relativa que esto
/// devuelve; nunca el archivo entero.
/// </summary>
public interface IEvidenciaAlmacen
{
    Task<string> GuardarAsync(int empresaId, long ordenId, string hash, string extension, byte[] datos, CancellationToken ct = default);
    Task<byte[]?> LeerAsync(string rutaRelativa, CancellationToken ct = default);
}
