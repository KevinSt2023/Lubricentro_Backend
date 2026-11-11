namespace LubriTech.Infraestructura.Seguridad;

/// <summary>Configuracion del token. Se lee de la seccion "Jwt".</summary>
public class OpcionesJwt
{
    public const string Seccion = "Jwt";

    public string Clave { get; set; } = string.Empty;
    public string Emisor { get; set; } = "lubritech";

    /// <summary>Vida del token de acceso. Corta: el refresh se encarga del resto.</summary>
    public int MinutosToken { get; set; } = 30;

    /// <summary>Vida del refresh del personal (ERP y app de operaciones).</summary>
    public int DiasRefreshPersonal { get; set; } = 7;

    /// <summary>Vida del refresh del cliente. Mas larga: su app se abre cada tanto.</summary>
    public int DiasRefreshCliente { get; set; } = 30;

    /// <summary>Intentos fallidos antes de bloquear.</summary>
    public int IntentosAntesDeBloqueo { get; set; } = 5;

    /// <summary>Minutos de bloqueo tras agotar los intentos.</summary>
    public int MinutosBloqueo { get; set; } = 15;
}

/// <summary>Claims propios. Nombres cortos: el token viaja en cada peticion.</summary>
public static class Claims
{
    public const string Empresa = "emp";
    public const string Sede = "sed";
    public const string Rol = "rol";
    public const string SesionVersion = "sv";
    public const string Cliente = "cli";
    public const string Acceso = "acc";
}
