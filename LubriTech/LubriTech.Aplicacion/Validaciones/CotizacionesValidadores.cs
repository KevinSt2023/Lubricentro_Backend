using FluentValidation;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Validaciones;

public class RegistrarHallazgoValidador : AbstractValidator<RegistrarHallazgoPeticion>
{
    private static readonly string[] Severidades = ["LEVE", "MODERADO", "CRITICO"];

    public RegistrarHallazgoValidador()
    {
        RuleFor(x => x.Descripcion).NotEmpty().WithMessage("El hallazgo necesita una descripcion.").MaximumLength(500);
        RuleFor(x => x.Severidad).Must(s => Severidades.Contains((s ?? "").Trim().ToUpperInvariant()))
            .WithMessage("La severidad va LEVE / MODERADO / CRITICO.");
    }
}

public class GuardarCotizacionValidador : AbstractValidator<GuardarCotizacionPeticion>
{
    private static readonly string[] Tipos = ["SERVICIO", "REPUESTO"];

    public GuardarCotizacionValidador()
    {
        RuleFor(x => x.Nota).MaximumLength(1000);
        RuleFor(x => x.Renglones).NotEmpty().WithMessage("La cotizacion necesita al menos un renglon.");
        RuleForEach(x => x.Renglones).ChildRules(r =>
        {
            r.RuleFor(i => i.Tipo).Must(t => Tipos.Contains((t ?? "").Trim().ToUpperInvariant()))
                .WithMessage("El tipo de renglon va SERVICIO o REPUESTO.");
            r.RuleFor(i => i.Cantidad).GreaterThan(0).WithMessage("La cantidad tiene que ser mayor que cero.");
            r.RuleFor(i => i.PrecioUnitario).GreaterThanOrEqualTo(0)
                .WithMessage("El precio no puede ser negativo.").When(i => i.PrecioUnitario.HasValue);
            r.RuleFor(i => i.Descripcion).MaximumLength(200);
            r.RuleFor(i => i)
                .Must(i => i.ServicioId.HasValue || i.ProductoId.HasValue || !string.IsNullOrWhiteSpace(i.Descripcion))
                .WithMessage("Cada renglon indica un item del catalogo o una descripcion.");
        });
    }
}

public class DecidirCotizacionValidador : AbstractValidator<DecidirCotizacionPeticion>
{
    private static readonly string[] Decisiones = ["APROBADA", "RECHAZADA"];

    public DecidirCotizacionValidador()
    {
        RuleFor(x => x.Decision).Must(d => Decisiones.Contains((d ?? "").Trim().ToUpperInvariant()))
            .WithMessage("La decision va APROBADA o RECHAZADA.");
        RuleFor(x => x.Comentario).MaximumLength(500);
    }
}
