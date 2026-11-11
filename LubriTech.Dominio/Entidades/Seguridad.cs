using LubriTech.Dominio.Comun;

namespace LubriTech.Dominio.Entidades;

/// <summary>Catalogo global de roles. IDs fijos, ver <see cref="Roles"/>.</summary>
public class Rol
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    /// <summary>Si es false, la base rechaza darle cualquier permiso de dinero.</summary>
    public bool ManejaDinero { get; set; }

    public List<RolPermiso> Permisos { get; set; } = [];
}

/// <summary>Catalogo global de permisos.</summary>
public class Permiso
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Modulo { get; set; } = string.Empty;
    public bool EsDinero { get; set; }

    /// <summary>Funcion del plan que ademas tiene que estar habilitada. NULL = no depende del plan.</summary>
    public string? FuncionRequerida { get; set; }
}

public class RolPermiso
{
    public int RolId { get; set; }
    public string PermisoCodigo { get; set; } = string.Empty;
    public Rol? Rol { get; set; }
    public Permiso? Permiso { get; set; }
}

/// <summary>
/// Personal de la empresa. El SuperAdmin es el unico sin empresa.
/// Un cliente NO es un usuario: ver <see cref="ClienteAcceso"/>.
/// </summary>
public class Usuario : IEntidadEmpresaOpcional
{
    public int Id { get; set; }
    public int? EmpresaId { get; set; }
    public int? SedeId { get; set; }
    public int RolId { get; set; }
    public string? Dni { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Sube de a uno para invalidar al instante todos los tokens del usuario.
    /// Va dentro del JWT y se compara en cada peticion.
    /// </summary>
    public int SesionVersion { get; set; } = 1;

    public int IntentosFallidos { get; set; }
    public DateTimeOffset? BloqueadoHasta { get; set; }
    public bool DebeCambiarClave { get; set; }
    public bool BiometriaActiva { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? UltimoAcceso { get; set; }

    public Rol? Rol { get; set; }
    public Empresa? Empresa { get; set; }
    public Sede? Sede { get; set; }

    public string NombreCompleto => $"{Nombres} {Apellidos}".Trim();
    public bool EstaBloqueado(DateTimeOffset ahora) => BloqueadoHasta.HasValue && BloqueadoHasta > ahora;
}

/// <summary>
/// Refresh token del personal. Rotativo: usarlo lo marca como usado y emite
/// otro. Reusar uno ya rotado se trata como robo y revoca toda la cadena.
/// Se guarda el hash, nunca el token.
/// </summary>
public class UsuarioRefresh
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset EmitidoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public DateTimeOffset? UsadoEn { get; set; }
    public DateTimeOffset? RevocadoEn { get; set; }
    public long? ReemplazadoPor { get; set; }
    public long? DispositivoId { get; set; }
    public string? Ip { get; set; }
    public string? Agente { get; set; }

    public Usuario? Usuario { get; set; }

    public bool EstaVigente(DateTimeOffset ahora) =>
        UsadoEn is null && RevocadoEn is null && ExpiraEn > ahora;
}

/// <summary>
/// Dispositivo registrado. Pertenece a un usuario O a un acceso de cliente,
/// nunca a los dos ni a ninguno (candado en la base).
/// Sirve para el push y para cerrar sesiones a distancia.
/// </summary>
public class Dispositivo
{
    public long Id { get; set; }
    public int? UsuarioId { get; set; }
    public long? AccesoId { get; set; }
    public string Aplicacion { get; set; } = string.Empty;   // OPERACIONES | CLIENTES | ERP_WEB
    public string Plataforma { get; set; } = string.Empty;   // ANDROID | IOS | WEB
    public string? TokenPush { get; set; }
    public string? ProveedorPush { get; set; }               // FCM | APNS
    public string? Modelo { get; set; }
    public string? VersionApp { get; set; }
    public string? VersionOs { get; set; }
    public string Identificador { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;
    public DateTimeOffset RegistradoEn { get; set; }
    public DateTimeOffset? UltimoVisto { get; set; }
    public DateTimeOffset? CerradoEn { get; set; }
}

/// <summary>Rastro de quien hizo que. Inmutable en la base: solo INSERT.</summary>
public class Bitacora : IEntidadEmpresaOpcional
{
    public long Id { get; set; }
    public int? EmpresaId { get; set; }
    public int? UsuarioId { get; set; }
    public long? AccesoId { get; set; }
    public int? SedeId { get; set; }
    public string Accion { get; set; } = string.Empty;
    public string Entidad { get; set; } = string.Empty;
    public string? EntidadId { get; set; }
    public string? Detalle { get; set; }
    public string? Ip { get; set; }
    public string? Agente { get; set; }
    public string Origen { get; set; } = Origenes.ErpWeb;
    public DateTimeOffset OcurridoEn { get; set; }
}

/// <summary>
/// Lo que llega del celular puede llegar dos veces. Misma clave y mismo
/// cuerpo devuelve la respuesta guardada; misma clave con otro cuerpo es 409.
/// </summary>
public class OperacionIdempotente
{
    public string Clave { get; set; } = string.Empty;
    public int? EmpresaId { get; set; }
    public int? UsuarioId { get; set; }
    public long? AccesoId { get; set; }
    public string Operacion { get; set; } = string.Empty;
    public string HuellaCuerpo { get; set; } = string.Empty;
    public int EstadoHttp { get; set; }
    public string? Respuesta { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
}
