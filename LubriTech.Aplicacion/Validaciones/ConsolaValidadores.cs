using FluentValidation;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Validaciones;

public class CrearEmpresaValidador : AbstractValidator<CrearEmpresaPeticion>
{
    public CrearEmpresaValidador()
    {
        RuleFor(x => x.Ruc)
            .NotEmpty().WithMessage("El RUC es obligatorio.")
            .Matches("^[0-9]{11}$").WithMessage("El RUC tiene que ser 11 digitos.");

        RuleFor(x => x.RazonSocial)
            .NotEmpty().WithMessage("La razon social es obligatoria.")
            .MaximumLength(200);

        RuleFor(x => x.NombreComercial)
            .NotEmpty().WithMessage("El nombre comercial es obligatorio.")
            .MaximumLength(150);

        RuleFor(x => x.PlanCodigo)
            .NotEmpty().WithMessage("Hay que elegir un plan.");

        RuleFor(x => x.Direccion).MaximumLength(250);
        RuleFor(x => x.Telefono).MaximumLength(30);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("El correo de la empresa no tiene un formato valido.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        // Hasta 90 dias de prueba. Mas que eso ya no es una prueba: es un
        // regalo que conviene que alguien decida a mano.
        RuleFor(x => x.DiasDeCortesia)
            .InclusiveBetween(0, 90)
            .WithMessage("Los dias de cortesia van de 0 a 90.");

        RuleFor(x => x.Admin).NotNull().SetValidator(new AdminNuevoValidador());
        RuleFor(x => x.SedePrincipal).NotNull().SetValidator(new SedeNuevaValidador());
    }
}

public class AdminNuevoValidador : AbstractValidator<AdminNuevo>
{
    public AdminNuevoValidador()
    {
        RuleFor(x => x.Nombres)
            .NotEmpty().WithMessage("El nombre del Admin es obligatorio.")
            .MaximumLength(100);

        RuleFor(x => x.Apellidos)
            .NotEmpty().WithMessage("Los apellidos del Admin son obligatorios.")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo del Admin es obligatorio.")
            .EmailAddress().WithMessage("El correo del Admin no tiene un formato valido.")
            .MaximumLength(200);

        RuleFor(x => x.Dni)
            .Matches("^[0-9]{8}$").WithMessage("El DNI tiene que ser 8 digitos.")
            .When(x => !string.IsNullOrWhiteSpace(x.Dni));

        RuleFor(x => x.Telefono).MaximumLength(30);
    }
}

public class SedeNuevaValidador : AbstractValidator<SedeNueva>
{
    public SedeNuevaValidador()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("La sede principal necesita un nombre.")
            .MaximumLength(120);

        RuleFor(x => x.Direccion).MaximumLength(250);
        RuleFor(x => x.Telefono).MaximumLength(30);
    }
}

public class ActualizarEmpresaValidador : AbstractValidator<ActualizarEmpresaPeticion>
{
    public ActualizarEmpresaValidador()
    {
        RuleFor(x => x.RazonSocial).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NombreComercial).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Direccion).MaximumLength(250);
        RuleFor(x => x.Telefono).MaximumLength(30);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("El correo no tiene un formato valido.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

public class CambiarPlanValidador : AbstractValidator<CambiarPlanPeticion>
{
    public CambiarPlanValidador()
    {
        RuleFor(x => x.PlanCodigo).NotEmpty().WithMessage("Hay que indicar el plan.");
        RuleFor(x => x.Motivo).MaximumLength(300);
    }
}

public class EstadoEmpresaValidador : AbstractValidator<EstadoEmpresaPeticion>
{
    public EstadoEmpresaValidador()
    {
        // Dar de baja exige motivo; reactivar no. La regla completa vive en
        // el servicio, porque depende del valor de Activo.
        RuleFor(x => x.Motivo).MaximumLength(300);
    }
}

public class GuardarSedeValidador : AbstractValidator<GuardarSedePeticion>
{
    public GuardarSedeValidador()
    {
        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("La sede necesita un nombre.")
            .MaximumLength(120);

        RuleFor(x => x.Direccion).MaximumLength(250);
        RuleFor(x => x.Telefono).MaximumLength(30);
    }
}

public class GuardarPlanValidador : AbstractValidator<GuardarPlanPeticion>
{
    public GuardarPlanValidador()
    {
        RuleFor(x => x.Codigo)
            .NotEmpty().WithMessage("El plan necesita un codigo.")
            .MaximumLength(40)
            .Matches("^[A-Za-z0-9_]+$")
            .WithMessage("El codigo del plan admite solo letras, numeros y guion bajo.");

        RuleFor(x => x.Nombre)
            .NotEmpty().WithMessage("El plan necesita un nombre.")
            .MaximumLength(100);

        RuleFor(x => x.PrecioMensual)
            .GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.")
            .LessThanOrEqualTo(99999).WithMessage("Ese precio no parece correcto.");

        RuleFor(x => x.MaxSedes)
            .GreaterThanOrEqualTo(0).WithMessage("El maximo de sedes no puede ser negativo (0 = sin tope).");

        RuleForEach(x => x.Cupos).ChildRules(cupo =>
        {
            cupo.RuleFor(c => c.RolId)
                .InclusiveBetween(1, 8)
                .WithMessage("El rol del cupo tiene que estar entre 1 y 8 (el 0 es el SuperAdmin).");

            cupo.RuleFor(c => c.Cupo)
                .GreaterThanOrEqualTo(0).WithMessage("Un cupo no puede ser negativo (0 = sin tope).")
                .LessThanOrEqualTo(999);
        });
    }
}

public class RegistrarSuscripcionValidador : AbstractValidator<RegistrarSuscripcionPeticion>
{
    public RegistrarSuscripcionValidador()
    {
        RuleFor(x => x.Meses)
            .InclusiveBetween(1, 36).WithMessage("Los meses a pagar van de 1 a 36.");

        RuleFor(x => x.Monto)
            .GreaterThanOrEqualTo(0).WithMessage("El monto no puede ser negativo.")
            .When(x => x.Monto.HasValue);

        RuleFor(x => x.Referencia).MaximumLength(100);
    }
}

public class AnularSuscripcionValidador : AbstractValidator<AnularSuscripcionPeticion>
{
    public AnularSuscripcionValidador()
    {
        RuleFor(x => x.Motivo)
            .NotEmpty().WithMessage("Anular un pago exige un motivo.")
            .MaximumLength(300);
    }
}
