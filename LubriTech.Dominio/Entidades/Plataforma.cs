using LubriTech.Dominio.Comun;

namespace LubriTech.Dominio.Entidades;

/// <summary>Plan comercial. Lo que el plan habilita son datos, no codigo.</summary>
public class Plan
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public decimal PrecioMensual { get; set; }
    public int MaxSedes { get; set; }
    public int Orden { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }

    public List<PlanFuncion> Funciones { get; set; } = [];
    public List<PlanRolCupo> Cupos { get; set; } = [];
}

/// <summary>
/// Catalogo de funciones vendibles. Existe para que "la funcion que el plan
/// habilita" no sea texto libre: un MULTISEDE mal escrito no habilitaria
/// nada y nadie se enteraria.
/// </summary>
public class Funcion
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Grupo { get; set; } = "GENERAL";
    public int Orden { get; set; }
}

/// <summary>Funcion que el plan habilita (MULTISEDE, VISOR_3D, ...).</summary>
public class PlanFuncion
{
    public int PlanId { get; set; }
    public string Funcion { get; set; } = string.Empty;
    public Plan? Plan { get; set; }
}

/// <summary>Cuantos usuarios de un rol admite el plan. 0 = sin tope. Sin fila = rol no habilitado.</summary>
public class PlanRolCupo
{
    public int PlanId { get; set; }
    public int RolId { get; set; }
    public int Cupo { get; set; }
    public Plan? Plan { get; set; }
}

/// <summary>El tenant. Un lubricentro o taller.</summary>
public class Empresa
{
    public int Id { get; set; }
    public string Ruc { get; set; } = string.Empty;
    public string RazonSocial { get; set; } = string.Empty;
    public string NombreComercial { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? LogoRuta { get; set; }
    public int PlanId { get; set; }
    public bool Activo { get; set; } = true;

    /// <summary>Hasta cuando esta pagado el servicio. Vencido = corte de acceso.</summary>
    public DateOnly ServicioHasta { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset? ActualizadoEn { get; set; }

    public Plan? Plan { get; set; }
    public List<Sede> Sedes { get; set; } = [];

    /// <summary>Al dia = activa y con servicio vigente al dia de Lima.</summary>
    public bool EstaAlDia(DateOnly hoyLima) => Activo && ServicioHasta >= hoyLima;
}

public class Suscripcion
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public int PlanId { get; set; }
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public decimal Monto { get; set; }
    public string Estado { get; set; } = "PAGADA";
    public string? Referencia { get; set; }
    public DateTimeOffset RegistradoEn { get; set; }
}

/// <summary>Local fisico de la empresa.</summary>
public class Sede : IEntidadEmpresa
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Direccion { get; set; }
    public string? Telefono { get; set; }
    public bool EsPrincipal { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }

    public Empresa? Empresa { get; set; }
}
