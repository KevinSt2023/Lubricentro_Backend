namespace LubriTech.Dominio.Comun;

/// <summary>
/// La marca que hace que una entidad quede dentro del filtro de empresa.
/// Toda tabla con datos de un negocio la implementa. Si no la implementa,
/// es porque es catalogo global (rol, permiso, plan) o es de la consola SaaS.
/// </summary>
public interface IEntidadEmpresa
{
    int EmpresaId { get; }
}

/// <summary>
/// Igual que <see cref="IEntidadEmpresa"/>, pero para las tablas donde la
/// empresa puede faltar porque la fila es del SuperAdmin (usuario) o del
/// sistema (bitacora). El filtro se aplica igual: sin empresa resuelta no se
/// ve nada, y el que necesite ver a traves de el usa IgnoreQueryFilters()
/// de forma explicita.
/// </summary>
public interface IEntidadEmpresaOpcional
{
    int? EmpresaId { get; }
}

/// <summary>IDs fijos de los roles. No se renumeran nunca.</summary>
public static class Roles
{
    public const int SuperAdmin = 0;
    public const int Admin = 1;
    public const int Gerente = 2;
    public const int Contador = 3;
    public const int Supervisor = 4;
    public const int Asesor = 5;
    public const int Tecnico = 6;
    public const int Cajero = 7;
    public const int Almacenero = 8;
}

/// <summary>Las dos audiencias de token. Un token de cliente no entra al ERP.</summary>
public static class Audiencias
{
    public const string Erp = "lubritech-erp";
    public const string Cliente = "lubritech-cliente";
}

/// <summary>De donde vino la accion. Se guarda en la bitacora.</summary>
public static class Origenes
{
    public const string ErpWeb = "ERP_WEB";
    public const string Operaciones = "OPERACIONES";
    public const string Clientes = "CLIENTES";
    public const string Sistema = "SISTEMA";
}

/// <summary>
/// El eje de tiempo. Todo se guarda en UTC real; "hoy" es el dia de Lima.
/// Nunca guardar hora de Lima disfrazada de UTC.
/// </summary>
public static class Tiempo
{
    private static readonly TimeSpan DesfaseLima = TimeSpan.FromHours(-5);

    public static DateTimeOffset Ahora => DateTimeOffset.UtcNow;

    /// <summary>El dia calendario en Lima para un instante dado.</summary>
    public static DateOnly DiaLima(DateTimeOffset instante) =>
        DateOnly.FromDateTime(instante.ToOffset(DesfaseLima).DateTime);

    /// <summary>Hoy en Lima.</summary>
    public static DateOnly HoyLima() => DiaLima(DateTimeOffset.UtcNow);

    /// <summary>
    /// El instante, EN UTC, en que empieza ese dia de Lima. Para comparar
    /// contra columnas timestamptz: la base solo acepta desfase 0, asi que
    /// un DateTimeOffset con -05:00 hace que Npgsql reviente. El eje de
    /// tiempo se guarda en UTC y se interpreta en Lima; esta es la bisagra.
    /// </summary>
    public static DateTimeOffset InicioDelDiaLima(DateOnly dia) =>
        new DateTimeOffset(dia.Year, dia.Month, dia.Day, 0, 0, 0, DesfaseLima)
            .ToUniversalTime();
}
