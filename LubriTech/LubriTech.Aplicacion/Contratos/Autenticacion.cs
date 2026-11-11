namespace LubriTech.Aplicacion.Contratos;

// ---------------------------------------------------------------------------
// Peticiones
// ---------------------------------------------------------------------------

public class LoginPeticion
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Datos del dispositivo, cuando el login viene de una app.</summary>
    public DispositivoPeticion? Dispositivo { get; set; }
}

public class RefrescarPeticion
{
    public string RefreshToken { get; set; } = string.Empty;
}

public class DispositivoPeticion
{
    public string Identificador { get; set; } = string.Empty;
    public string Aplicacion { get; set; } = string.Empty;   // OPERACIONES | CLIENTES | ERP_WEB
    public string Plataforma { get; set; } = string.Empty;   // ANDROID | IOS | WEB
    public string? TokenPush { get; set; }
    public string? Modelo { get; set; }
    public string? VersionApp { get; set; }
    public string? VersionOs { get; set; }
}

public class CambiarClavePeticion
{
    public string ClaveActual { get; set; } = string.Empty;
    public string ClaveNueva { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Respuestas
// ---------------------------------------------------------------------------

public class SesionRespuesta
{
    public string Token { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiraEn { get; set; }
    public string Audiencia { get; set; } = string.Empty;
    public PerfilRespuesta Perfil { get; set; } = new();
}

/// <summary>
/// Lo que la app necesita para armar su menu. Los permisos que llegan aqui
/// ya son los EFECTIVOS: rol interseccion plan. La UI los usa para mostrar,
/// pero la autorizacion de verdad se verifica en el servidor en cada llamada.
/// </summary>
public class PerfilRespuesta
{
    public string Tipo { get; set; } = "PERSONAL";          // PERSONAL | CLIENTE
    public int? UsuarioId { get; set; }
    public long? ClienteId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int? RolId { get; set; }
    public string? Rol { get; set; }
    public int? EmpresaId { get; set; }
    public string? Empresa { get; set; }
    public int? SedeId { get; set; }
    public string? Sede { get; set; }
    public bool DebeCambiarClave { get; set; }
    public string? PlanCodigo { get; set; }
    public List<string> Permisos { get; set; } = [];
    public List<string> Funciones { get; set; } = [];
}
