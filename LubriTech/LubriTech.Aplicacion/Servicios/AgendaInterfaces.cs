using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Servicios;

/// <summary>
/// La agenda del taller. Una cita es una orden en CITA_REGISTRADA con fecha;
/// este servicio es la capa fina que el calendario necesita. Lo de siempre
/// (alta, cambio de estado, anulacion) se apoya en <see cref="IOrdenServicio"/>
/// para no duplicar historial ni bitacora.
/// </summary>
public interface IAgendaServicio
{
    /// <summary>Las citas (ordenes aun en CITA_REGISTRADA) en un rango de fechas.</summary>
    Task<Resultado<List<AgendaCitaItem>>> ListarAsync(
        int empresaId, DateTimeOffset desde, DateTimeOffset hasta,
        int? sedeId, int? tecnicoId, CancellationToken ct = default);

    /// <summary>Agenda una cita: crea la orden en CITA_REGISTRADA con su fecha.</summary>
    Task<Resultado<OrdenDetalle>> CrearCitaAsync(
        int empresaId, int? usuarioId, CrearCitaPeticion peticion, bool puedeVerCostos, CancellationToken ct = default);

    /// <summary>Reprograma la cita (fecha y/o técnico). Solo mientras sigue siendo cita.</summary>
    Task<Resultado<AgendaCitaItem>> ReprogramarAsync(
        int empresaId, int? usuarioId, long ordenId, ReprogramarCitaPeticion peticion, CancellationToken ct = default);

    /// <summary>Recepción: la cita pasa a Recibido al llegar el vehículo.</summary>
    Task<Resultado<OrdenDetalle>> RecibirAsync(
        int empresaId, int? usuarioId, long ordenId, RecibirCitaPeticion peticion, bool puedeVerCostos, CancellationToken ct = default);

    /// <summary>Cancela la cita (anula la orden; exige motivo).</summary>
    Task<Resultado<OrdenDetalle>> CancelarAsync(
        int empresaId, int? usuarioId, long ordenId, AnularOrdenPeticion peticion, bool puedeVerCostos, CancellationToken ct = default);

    /// <summary>
    /// Vehículos con mantenimiento vencido o por vencer: dentro de <paramref name="dias"/>
    /// días o de <paramref name="km"/> kilómetros del objetivo. Para llamar al cliente antes.
    /// </summary>
    Task<Resultado<List<ProximoMantenimientoItem>>> ProximosMantenimientosAsync(
        int empresaId, int dias, int km, CancellationToken ct = default);
}
