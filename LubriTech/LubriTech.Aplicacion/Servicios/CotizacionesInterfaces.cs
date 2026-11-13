using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Servicios;

/// <summary>
/// Cotizaciones y aprobaciones (Fase 4). Tenant-scoped: la empresa sale del
/// token. El lado del taller (hallazgos, armar y enviar la cotizacion,
/// aplicarla) y el lado del cliente (ver y decidir) viven aqui, separados por
/// los permisos del controlador y por la audiencia del token.
///
/// Lo que vigila la base: la version correlativa, que una cotizacion enviada
/// no se edite, que solo la decision del cliente la apruebe, y que el renglon
/// cotizado no pase a ejecucion sin esa aprobacion.
/// </summary>
public interface ICotizacionServicio
{
    // --- Hallazgos del tecnico ---
    Task<Resultado<List<HallazgoItem>>> HallazgosAsync(int empresaId, long ordenId, CancellationToken ct = default);

    Task<Resultado<HallazgoItem>> RegistrarHallazgoAsync(
        int empresaId, int? usuarioId, long ordenId, RegistrarHallazgoPeticion peticion, CancellationToken ct = default);

    Task<Resultado> QuitarHallazgoAsync(int empresaId, long ordenId, long hallazgoId, CancellationToken ct = default);

    // --- Cotizacion (asesor) ---
    Task<Resultado<List<CotizacionItem>>> ListarPorOrdenAsync(int empresaId, long ordenId, CancellationToken ct = default);

    Task<Resultado<CotizacionDetalle>> VerAsync(int empresaId, long cotizacionId, CancellationToken ct = default);

    /// <summary>Arma una cotizacion nueva en BORRADOR con sus renglones.</summary>
    Task<Resultado<CotizacionDetalle>> CrearAsync(
        int empresaId, int? usuarioId, long ordenId, GuardarCotizacionPeticion peticion, CancellationToken ct = default);

    /// <summary>Reemplaza los renglones de una cotizacion que sigue en BORRADOR.</summary>
    Task<Resultado<CotizacionDetalle>> ActualizarAsync(
        int empresaId, long cotizacionId, GuardarCotizacionPeticion peticion, CancellationToken ct = default);

    /// <summary>La envia al cliente: pasa a ENVIADA y congela sus renglones.</summary>
    Task<Resultado<CotizacionDetalle>> EnviarAsync(
        int empresaId, int? usuarioId, long cotizacionId, CancellationToken ct = default);

    /// <summary>
    /// Tras la aprobacion, pasa los servicios cotizados a la orden como
    /// renglones de ejecucion. La base rechaza el renglon si la cotizacion no
    /// esta APROBADA, asi que esto no se puede forzar.
    /// </summary>
    Task<Resultado<CotizacionDetalle>> AplicarAsync(
        int empresaId, int? usuarioId, long cotizacionId, CancellationToken ct = default);

    // --- Portal del cliente ---
    Task<List<CotizacionItem>> PortalListarAsync(int empresaId, long clienteId, CancellationToken ct = default);

    Task<Resultado<CotizacionDetalle>> PortalVerAsync(
        int empresaId, long clienteId, long cotizacionId, CancellationToken ct = default);

    /// <summary>La decision del cliente. Queda inmutable; la base pone el estado.</summary>
    Task<Resultado<CotizacionDetalle>> DecidirAsync(
        int empresaId, long clienteId, long cotizacionId, DecidirCotizacionPeticion peticion,
        string? ip, CancellationToken ct = default);
}
