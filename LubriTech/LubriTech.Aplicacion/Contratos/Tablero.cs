namespace LubriTech.Aplicacion.Contratos;

/// <summary>
/// El tablero del taller: la foto operativa del dia para el Admin/Gerente.
/// Todo sale de las ordenes, cotizaciones y stock de SU empresa (el filtro de
/// EF ya acota por el token). No toca plata real: "valor" es el total de los
/// trabajos registrados, no caja cobrada (eso llega con la Fase de caja).
/// </summary>
public class TableroTaller
{
    public int OrdenesAbiertas { get; set; }
    public int OrdenesHoy { get; set; }
    public int CotizacionesPendientes { get; set; }
    public int ProductosBajoMinimo { get; set; }
    public decimal ValorTrabajosMes { get; set; }

    public List<ConteoEstado> PorEstado { get; set; } = [];
    public List<CargaTecnico> CargaPorTecnico { get; set; } = [];
    public List<OrdenMini> UltimasOrdenes { get; set; } = [];
}

public class ConteoEstado
{
    public int EstadoId { get; set; }
    public string Estado { get; set; } = string.Empty;
    public int Secuencia { get; set; }
    public int Cantidad { get; set; }
}

public class CargaTecnico
{
    public string Tecnico { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public class OrdenMini
{
    public long Id { get; set; }
    public int Numero { get; set; }
    public string? Placa { get; set; }
    public string? Cliente { get; set; }
    public string Estado { get; set; } = string.Empty;
    public bool EstadoFinal { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
