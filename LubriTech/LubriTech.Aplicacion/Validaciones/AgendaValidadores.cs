using FluentValidation;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Validaciones;

public class CrearCitaValidador : AbstractValidator<CrearCitaPeticion>
{
    public CrearCitaValidador()
    {
        RuleFor(x => x.ClienteId).GreaterThan(0).WithMessage("Hay que indicar el cliente.");
        RuleFor(x => x.VehiculoId).GreaterThan(0).WithMessage("Hay que indicar el vehiculo.");
        RuleFor(x => x.SedeId).GreaterThan(0).WithMessage("Hay que indicar la sede.");
        RuleFor(x => x.FechaCita).NotEqual(default(DateTimeOffset))
            .WithMessage("Hay que indicar la fecha y hora de la cita.");
        RuleFor(x => x.Motivo).MaximumLength(2000);
    }
}

public class ReprogramarCitaValidador : AbstractValidator<ReprogramarCitaPeticion>
{
    public ReprogramarCitaValidador()
    {
        RuleFor(x => x.FechaCita).NotEqual(default(DateTimeOffset))
            .WithMessage("Hay que indicar la nueva fecha y hora.");
    }
}

public class RecibirCitaValidador : AbstractValidator<RecibirCitaPeticion>
{
    public RecibirCitaValidador()
    {
        RuleFor(x => x.KilometrajeIngreso).GreaterThanOrEqualTo(0)
            .WithMessage("El kilometraje no puede ser negativo.").When(x => x.KilometrajeIngreso.HasValue);
    }
}
