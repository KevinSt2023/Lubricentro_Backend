using FluentValidation;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Validaciones;

// ===========================================================================
// PERSONAL
// ===========================================================================

public class GuardarPersonalValidador : AbstractValidator<GuardarPersonalPeticion>
{
    public GuardarPersonalValidador()
    {
        RuleFor(x => x.Nombres).NotEmpty().WithMessage("El nombre es obligatorio.").MaximumLength(100);
        RuleFor(x => x.Apellidos).NotEmpty().WithMessage("Los apellidos son obligatorios.").MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("El correo no tiene un formato valido.").MaximumLength(200);
        RuleFor(x => x.Dni).Matches("^[0-9]{8}$").WithMessage("El DNI tiene que ser 8 digitos.")
            .When(x => !string.IsNullOrWhiteSpace(x.Dni));
        RuleFor(x => x.Telefono).MaximumLength(30);
        RuleFor(x => x.RolId).InclusiveBetween(1, 8).WithMessage("El rol tiene que estar entre 1 y 8.");
    }
}

public class ActualizarPersonalValidador : AbstractValidator<ActualizarPersonalPeticion>
{
    public ActualizarPersonalValidador()
    {
        RuleFor(x => x.Nombres).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Apellidos).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Dni).Matches("^[0-9]{8}$").WithMessage("El DNI tiene que ser 8 digitos.")
            .When(x => !string.IsNullOrWhiteSpace(x.Dni));
        RuleFor(x => x.Telefono).MaximumLength(30);
        RuleFor(x => x.RolId).InclusiveBetween(1, 8).WithMessage("El rol tiene que estar entre 1 y 8.");
    }
}

// ===========================================================================
// CLIENTES
// ===========================================================================

public class GuardarClienteValidador : AbstractValidator<GuardarClientePeticion>
{
    private static readonly string[] Tipos = ["PARTICULAR", "EMPRESA"];
    private static readonly string[] Documentos = ["DNI", "RUC", "CE", "PASAPORTE", "SIN_DOCUMENTO"];

    public GuardarClienteValidador()
    {
        RuleFor(x => x.Tipo).Must(t => Tipos.Contains(t)).WithMessage("El tipo de cliente no es valido.");
        RuleFor(x => x.TipoDocumento).Must(t => Documentos.Contains(t))
            .WithMessage("El tipo de documento no es valido.");

        RuleFor(x => x.Nombres).NotEmpty().WithMessage("Un particular necesita nombres.")
            .When(x => x.Tipo == "PARTICULAR");
        RuleFor(x => x.RazonSocial).NotEmpty().WithMessage("Una empresa necesita razon social.")
            .When(x => x.Tipo == "EMPRESA");

        RuleFor(x => x.Documento).Matches("^[0-9]{8}$").WithMessage("El DNI tiene que ser 8 digitos.")
            .When(x => x.TipoDocumento == "DNI" && !string.IsNullOrWhiteSpace(x.Documento));
        RuleFor(x => x.Documento).Matches("^[0-9]{11}$").WithMessage("El RUC tiene que ser 11 digitos.")
            .When(x => x.TipoDocumento == "RUC" && !string.IsNullOrWhiteSpace(x.Documento));

        RuleFor(x => x.Email).EmailAddress().WithMessage("El correo no tiene un formato valido.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Telefono).MaximumLength(30);
        RuleFor(x => x.Nombres).MaximumLength(100);
        RuleFor(x => x.Apellidos).MaximumLength(100);
        RuleFor(x => x.RazonSocial).MaximumLength(200);
        RuleFor(x => x.Direccion).MaximumLength(250);
    }
}

// ===========================================================================
// VEHICULOS
// ===========================================================================

public class GuardarVehiculoValidador : AbstractValidator<GuardarVehiculoPeticion>
{
    private static readonly string[] Tipos = ["AUTO", "CAMIONETA", "SUV", "CAMION", "BUS", "MOTO", "OTRO"];

    public GuardarVehiculoValidador()
    {
        RuleFor(x => x.ClienteId).GreaterThan(0).WithMessage("Hay que indicar el cliente del vehiculo.");
        RuleFor(x => x.Placa).NotEmpty().WithMessage("La placa es obligatoria.").MaximumLength(15);
        RuleFor(x => x.Marca).NotEmpty().WithMessage("La marca es obligatoria.").MaximumLength(60);
        RuleFor(x => x.Modelo).NotEmpty().WithMessage("El modelo es obligatorio.").MaximumLength(60);
        RuleFor(x => x.Tipo).Must(t => Tipos.Contains(t)).WithMessage("El tipo de vehiculo no es valido.");
        RuleFor(x => x.Anio).InclusiveBetween(1900, 2100).WithMessage("El anio no parece correcto.")
            .When(x => x.Anio.HasValue);
        RuleFor(x => x.Kilometraje).GreaterThanOrEqualTo(0).WithMessage("El kilometraje no puede ser negativo.");
    }
}

public class KilometrajeValidador : AbstractValidator<KilometrajePeticion>
{
    public KilometrajeValidador()
    {
        RuleFor(x => x.Kilometraje).GreaterThanOrEqualTo(0).WithMessage("El kilometraje no puede ser negativo.");
    }
}

// ===========================================================================
// SERVICIOS
// ===========================================================================

public class GuardarServicioValidador : AbstractValidator<GuardarServicioPeticion>
{
    public GuardarServicioValidador()
    {
        RuleFor(x => x.Codigo).NotEmpty().WithMessage("El servicio necesita un codigo.").MaximumLength(40);
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("El servicio necesita un nombre.").MaximumLength(150);
        RuleFor(x => x.Precio).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
        RuleFor(x => x.DuracionMin).GreaterThanOrEqualTo(0).WithMessage("La duracion no puede ser negativa.")
            .When(x => x.DuracionMin.HasValue);
        RuleFor(x => x.Categoria).MaximumLength(60);
    }
}

// ===========================================================================
// ALMACEN
// ===========================================================================

public class GuardarProductoValidador : AbstractValidator<GuardarProductoPeticion>
{
    private static readonly string[] Tipos = ["REPUESTO", "INSUMO", "ACEITE", "FILTRO", "LLANTA", "OTRO"];
    private static readonly string[] Unidades = ["UND", "LT", "GAL", "KG", "JGO", "MT", "CAJA"];

    public GuardarProductoValidador()
    {
        RuleFor(x => x.Codigo).NotEmpty().WithMessage("El producto necesita un codigo.").MaximumLength(40);
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("El producto necesita un nombre.").MaximumLength(150);
        RuleFor(x => x.Tipo).Must(t => Tipos.Contains(t)).WithMessage("El tipo de producto no es valido.");
        RuleFor(x => x.Unidad).Must(u => Unidades.Contains(u)).WithMessage("La unidad no es valida.");
        RuleFor(x => x.PrecioVenta).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.");
        RuleFor(x => x.StockMinimo).GreaterThanOrEqualTo(0).WithMessage("El stock minimo no puede ser negativo.");
        RuleFor(x => x.CodigoBarras).MaximumLength(50);
        RuleFor(x => x.Marca).MaximumLength(60);
    }
}

public class IngresoStockValidador : AbstractValidator<IngresoStockPeticion>
{
    public IngresoStockValidador()
    {
        RuleFor(x => x.SedeId).GreaterThan(0).WithMessage("Hay que indicar la sede.");
        RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad a ingresar tiene que ser mayor que cero.");
        RuleFor(x => x.CostoUnitario).GreaterThanOrEqualTo(0).WithMessage("El costo no puede ser negativo.");
        RuleFor(x => x.Referencia).MaximumLength(100);
        RuleFor(x => x.Motivo).MaximumLength(200);
    }
}

public class AjusteStockValidador : AbstractValidator<AjusteStockPeticion>
{
    public AjusteStockValidador()
    {
        RuleFor(x => x.SedeId).GreaterThan(0).WithMessage("Hay que indicar la sede.");
        RuleFor(x => x.CantidadReal).GreaterThanOrEqualTo(0).WithMessage("La cantidad contada no puede ser negativa.");
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Un ajuste de inventario exige un motivo.").MaximumLength(200);
    }
}
