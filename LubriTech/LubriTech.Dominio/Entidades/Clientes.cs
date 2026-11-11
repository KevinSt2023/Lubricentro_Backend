using LubriTech.Dominio.Comun;

namespace LubriTech.Dominio.Entidades;

/// <summary>
/// El cliente del taller. Particular o empresa con flota.
/// En la Fase 2 se le agregan vehiculos e historial; la ficha crece por
/// columnas nuevas, no reescribiendo esto.
/// </summary>
public class Cliente : IEntidadEmpresa
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public string Tipo { get; set; } = "PARTICULAR";            // PARTICULAR | EMPRESA
    public string TipoDocumento { get; set; } = "DNI";
    public string? Documento { get; set; }
    public string? Nombres { get; set; }
    public string? Apellidos { get; set; }
    public string? RazonSocial { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Direccion { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }

    public Empresa? Empresa { get; set; }
    public ClienteAcceso? Acceso { get; set; }

    public string NombreMostrado => Tipo == "EMPRESA"
        ? (RazonSocial ?? string.Empty)
        : $"{Nombres} {Apellidos}".Trim();
}

/// <summary>
/// La cuenta del portal del cliente. Cuelga del cliente, no del usuario, y
/// NO puede recibir ningun rol del ERP: su token lleva otra audiencia.
/// Es la mitad del candado del criterio 11.
/// </summary>
public class ClienteAcceso
{
    public long Id { get; set; }
    public long ClienteId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int SesionVersion { get; set; } = 1;
    public int IntentosFallidos { get; set; }
    public DateTimeOffset? BloqueadoHasta { get; set; }
    public bool EmailVerificado { get; set; }
    public bool BiometriaActiva { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? UltimoAcceso { get; set; }

    public Cliente? Cliente { get; set; }

    public bool EstaBloqueado(DateTimeOffset ahora) => BloqueadoHasta.HasValue && BloqueadoHasta > ahora;
}

public class ClienteRefresh
{
    public long Id { get; set; }
    public long AccesoId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset EmitidoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? UsadoEn { get; set; }
    public DateTimeOffset? RevocadoEn { get; set; }
    public long? ReemplazadoPor { get; set; }
    public long? DispositivoId { get; set; }
    public string? Ip { get; set; }
    public string? Agente { get; set; }

    public ClienteAcceso? Acceso { get; set; }

    public bool EstaVigente(DateTimeOffset ahora) =>
        UsadoEn is null && RevocadoEn is null && ExpiraEn > ahora;
}

/// <summary>Token de un solo uso para recuperar la contrasena del portal.</summary>
public class ClienteRecuperacion
{
    public long Id { get; set; }
    public long AccesoId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset EmitidoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? UsadoEn { get; set; }
}
