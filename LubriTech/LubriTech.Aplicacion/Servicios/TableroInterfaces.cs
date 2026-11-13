using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Servicios;

/// <summary>
/// El tablero operativo del taller. Tenant-scoped por el filtro de EF: cada
/// empresa ve lo suyo. Pura lectura con conteos y sumas en la base.
/// </summary>
public interface ITableroTallerServicio
{
    Task<TableroTaller> VerAsync(CancellationToken ct = default);
}
