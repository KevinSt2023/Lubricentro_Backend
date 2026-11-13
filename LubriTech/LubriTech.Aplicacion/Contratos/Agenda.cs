namespace LubriTech.Aplicacion.Contratos;

// ===========================================================================
// Agenda y citas (Fase 5)
//
// Una cita es una orden en estado CITA_REGISTRADA con fecha_cita. Estos
// contratos son la vista que necesita el calendario: solo lo de la cita, sin
// el peso del detalle completo de la orden.
// ===========================================================================

/// <summary>Una cita en el calendario (una orden que todavia es CITA_REGISTRADA).</summary>
public class AgendaCitaItem
{
    public long Id { get; set; }
    public int Numero { get; set; }
    public DateTimeOffset FechaCita { get; set; }
    public int SedeId { get; set; }
    public string? Sede { get; set; }
    public long ClienteId { get; set; }
    public string? Cliente { get; set; }
    public string? Telefono { get; set; }
    public long VehiculoId { get; set; }
    public string? Placa { get; set; }
    public string? Vehiculo { get; set; }
    public int? TecnicoId { get; set; }
    public string? Tecnico { get; set; }
    /// <summary>Lo que el cliente pidió (se guarda en observaciones de la orden).</summary>
    public string? Motivo { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

/// <summary>Agendar una cita: crea la orden en CITA_REGISTRADA con fecha.</summary>
public class CrearCitaPeticion
{
    public long ClienteId { get; set; }
    public long VehiculoId { get; set; }
    public int SedeId { get; set; }
    public DateTimeOffset FechaCita { get; set; }
    public int? TecnicoId { get; set; }
    /// <summary>Motivo de la cita; va a las observaciones de la orden.</summary>
    public string? Motivo { get; set; }
}

/// <summary>Reprogramar: solo cambia la fecha/hora, y solo mientras es cita.</summary>
public class ReprogramarCitaPeticion
{
    public DateTimeOffset FechaCita { get; set; }
    public int? TecnicoId { get; set; }
}

/// <summary>Recepción (check-in): la cita pasa a Recibido al llegar el vehículo.</summary>
public class RecibirCitaPeticion
{
    public int? KilometrajeIngreso { get; set; }
}

/// <summary>
/// Un vehículo que necesita (o pronto necesitará) mantenimiento. Para que el
/// taller llame al cliente antes de que toque, por fecha o por kilometraje.
/// </summary>
public class ProximoMantenimientoItem
{
    public long VehiculoId { get; set; }
    public string Placa { get; set; } = string.Empty;
    public string Vehiculo { get; set; } = string.Empty;
    public long ClienteId { get; set; }
    public string? Cliente { get; set; }
    public string? Telefono { get; set; }
    public int Kilometraje { get; set; }
    public DateOnly? ProximoMantFecha { get; set; }
    public int? ProximoMantKm { get; set; }
    /// <summary>Días hasta la fecha objetivo (negativo = vencido).</summary>
    public int? DiasRestantes { get; set; }
    /// <summary>Km hasta el objetivo (negativo = pasado).</summary>
    public int? KmRestantes { get; set; }
    /// <summary>Ya tocó: por fecha o por kilometraje.</summary>
    public bool Vencido { get; set; }
}
