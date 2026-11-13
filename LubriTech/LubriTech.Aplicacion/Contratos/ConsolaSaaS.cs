namespace LubriTech.Aplicacion.Contratos;

// ===========================================================================
// Estado del servicio de una empresa
// ===========================================================================

public static class EstadoServicio
{
    public const string AlDia = "AL_DIA";
    public const string PorVencer = "POR_VENCER";   // vence dentro de 7 dias
    public const string Vencido = "VENCIDO";
    public const string Inactiva = "INACTIVA";      // la dio de baja el SuperAdmin

    public const int DiasDeAviso = 7;

    public static string Calcular(bool activo, DateOnly servicioHasta, DateOnly hoy)
    {
        if (!activo) return Inactiva;
        if (servicioHasta < hoy) return Vencido;
        return servicioHasta.DayNumber - hoy.DayNumber <= DiasDeAviso ? PorVencer : AlDia;
    }
}

// ===========================================================================
// Empresas
// ===========================================================================

public class EmpresaListaItem
{
    public int Id { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string NombreComercial { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public int PlanId { get; set; }
    public string Plan { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public DateOnly ServicioHasta { get; set; }
    public int DiasRestantes { get; set; }
    public string EstadoServicio { get; set; } = string.Empty;
    public int Sedes { get; set; }
    public int Usuarios { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}

public class EmpresaDetalle : EmpresaListaItem
{
    public string? Direccion { get; set; }
    public decimal PrecioMensual { get; set; }
    public List<SedeItem> ListaSedes { get; set; } = [];
    public List<string> Funciones { get; set; } = [];
    public List<UsoDeRol> UsoPorRol { get; set; } = [];
    public int MaxSedes { get; set; }
    public SuscripcionItem? UltimaSuscripcion { get; set; }
    public AdminDeEmpresa? Admin { get; set; }
}

public class AdminDeEmpresa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool DebeCambiarClave { get; set; }
    public DateTimeOffset? UltimoAcceso { get; set; }
}

public class UsoDeRol
{
    public int RolId { get; set; }
    public string Rol { get; set; } = string.Empty;

    /// <summary>0 = sin tope. Nulo = el plan no habilita ese rol.</summary>
    public int? Cupo { get; set; }

    public int Usados { get; set; }
    public bool Excedido { get; set; }
}

public class CrearEmpresaPeticion
{
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string NombreComercial { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string PlanCodigo { get; set; } = string.Empty;

    /// <summary>
    /// Dias de prueba. Es un regalo deliberado del SuperAdmin, no un descuido:
    /// queda escrito en la bitacora con quien lo dio.
    /// </summary>
    public int DiasDeCortesia { get; set; } = 7;

    public AdminNuevo Admin { get; set; } = new();
    public SedeNueva SedePrincipal { get; set; } = new();
}

public class AdminNuevo
{
    public string? Dni { get; set; }
    public string Nombres { get; set; } = string.Empty;
    public string Apellidos { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
}

public class SedeNueva
{
    public string Nombre { get; set; } = "Sede Principal";
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
}

/// <summary>El alta devuelve la clave temporal UNA sola vez. No se guarda en claro.</summary>
public class EmpresaCreada
{
    public EmpresaDetalle Empresa { get; set; } = new();
    public string EmailAdmin { get; set; } = string.Empty;
    public string ClaveTemporal { get; set; } = string.Empty;
}

public class ActualizarEmpresaPeticion
{
    public string RazonSocial { get; set; } = string.Empty;
    public string NombreComercial { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
}

public class CambiarPlanPeticion
{
    public string PlanCodigo { get; set; } = string.Empty;
    public string? Motivo { get; set; }
}

public class EstadoEmpresaPeticion
{
    public bool Activo { get; set; }
    public string? Motivo { get; set; }
}

// ===========================================================================
// Sedes
// ===========================================================================

public class SedeItem
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public bool EsPrincipal { get; set; }
    public bool Activo { get; set; }
    public int Usuarios { get; set; }
}

public class GuardarSedePeticion
{
    public string Nombre { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public bool Activo { get; set; } = true;
}

// ===========================================================================
// Planes y funciones
// ===========================================================================

public class FuncionItem
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Grupo { get; set; } = string.Empty;
    public int Orden { get; set; }
}

public class PlanDetalle
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal PrecioMensual { get; set; }
    public int MaxSedes { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; }
    public List<string> Funciones { get; set; } = [];
    public List<CupoDeRol> Cupos { get; set; } = [];
    public int Empresas { get; set; }
}

public class CupoDeRol
{
    public int RolId { get; set; }
    public string? Rol { get; set; }

    /// <summary>0 = sin tope.</summary>
    public int Cupo { get; set; }
}

public class GuardarPlanPeticion
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal PrecioMensual { get; set; }
    public int MaxSedes { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;
    public List<string> Funciones { get; set; } = [];
    public List<CupoDeRol> Cupos { get; set; } = [];
}

// ===========================================================================
// Suscripciones
// ===========================================================================

public class SuscripcionItem
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public string? Empresa { get; set; }
    public int PlanId { get; set; }
    public string? Plan { get; set; }
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public decimal Monto { get; set; }
    public string Estado { get; set; } = string.Empty;
    public string? Referencia { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
    public int Dias { get; set; }
}

public class RegistrarSuscripcionPeticion
{
    /// <summary>Meses a pagar. 1 a 36.</summary>
    public int Meses { get; set; } = 1;

    /// <summary>Nulo = el monto de lista del plan por la cantidad de meses.</summary>
    public decimal? Monto { get; set; }

    /// <summary>Numero de operacion, voucher o referencia del pago.</summary>
    public string? Referencia { get; set; }
}

public class AnularSuscripcionPeticion
{
    public string Motivo { get; set; } = string.Empty;
}

// ===========================================================================
// Tablero del SaaS
// ===========================================================================

public class TableroSaaS
{
    public int Empresas { get; set; }
    public int Activas { get; set; }
    public int Inactivas { get; set; }
    public int AlDia { get; set; }
    public int PorVencer { get; set; }
    public int Vencidas { get; set; }
    public decimal IngresoMesActual { get; set; }
    public decimal IngresoMesAnterior { get; set; }
    public decimal IngresoRecurrenteMensual { get; set; }
    public List<ConteoPorPlan> PorPlan { get; set; } = [];
    public List<EmpresaListaItem> PorVencerPronto { get; set; } = [];
    public List<SuscripcionItem> UltimosPagos { get; set; } = [];
}

public class ConteoPorPlan
{
    public int PlanId { get; set; }
    public string Plan { get; set; } = string.Empty;
    public int Empresas { get; set; }
    public decimal PrecioMensual { get; set; }
}

// ===========================================================================
// Salud y cumplimiento por empresa
// ===========================================================================

public class SaludEmpresa
{
    public int EmpresaId { get; set; }
    public string Empresa { get; set; } = string.Empty;
    public string EstadoServicio { get; set; } = string.Empty;
    public int DiasRestantes { get; set; }
    public DateOnly ServicioHasta { get; set; }
    public string Plan { get; set; } = string.Empty;
    public List<UsoDeRol> UsoPorRol { get; set; } = [];
    public int SedesUsadas { get; set; }
    public int MaxSedes { get; set; }
    public bool SedesExcedidas { get; set; }
    public List<string> Funciones { get; set; } = [];
    public bool TieneAdmin { get; set; }
    public DateTimeOffset? UltimoAccesoDeAlguien { get; set; }

    /// <summary>
    /// Lo que hay que mirar, en una frase cada cosa. Si viene vacia, la
    /// empresa esta sana.
    /// </summary>
    public List<string> Alertas { get; set; } = [];
}

/// <summary>
/// Un usuario visto desde la consola del SuperAdmin: cruza empresas, por eso
/// trae el nombre de la empresa a la que pertenece. El SuperAdmin aparece sin
/// empresa (es de la plataforma).
/// </summary>
public class UsuarioGlobalItem
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Dni { get; set; }
    public string? Telefono { get; set; }
    public int RolId { get; set; }
    public string? Rol { get; set; }
    public int? EmpresaId { get; set; }
    public string? Empresa { get; set; }
    public int? SedeId { get; set; }
    public string? Sede { get; set; }
    public bool Activo { get; set; }
    public bool DebeCambiarClave { get; set; }
    public DateTimeOffset? UltimoAcceso { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
}
