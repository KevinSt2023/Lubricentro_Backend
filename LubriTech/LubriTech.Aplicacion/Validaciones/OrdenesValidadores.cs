using FluentValidation;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Validaciones;

public class CrearOrdenValidador : AbstractValidator<CrearOrdenPeticion>
{
    private static readonly string[] Inicial = ["CITA_REGISTRADA", "RECIBIDO"];

    public CrearOrdenValidador()
    {
        RuleFor(x => x.ClienteId).GreaterThan(0).WithMessage("Hay que indicar el cliente.");
        RuleFor(x => x.VehiculoId).GreaterThan(0).WithMessage("Hay que indicar el vehiculo.");
        RuleFor(x => x.SedeId).GreaterThan(0).WithMessage("Hay que indicar la sede.");
        RuleFor(x => x.EstadoInicial).Must(e => Inicial.Contains(e))
            .WithMessage("Una orden arranca en Cita registrada o en Recibido.");
        RuleFor(x => x.KilometrajeIngreso).GreaterThanOrEqualTo(0)
            .WithMessage("El kilometraje no puede ser negativo.").When(x => x.KilometrajeIngreso.HasValue);
    }
}

public class ActualizarOrdenValidador : AbstractValidator<ActualizarOrdenPeticion>
{
    public ActualizarOrdenValidador()
    {
        RuleFor(x => x.KilometrajeIngreso).GreaterThanOrEqualTo(0)
            .WithMessage("El kilometraje no puede ser negativo.").When(x => x.KilometrajeIngreso.HasValue);
        RuleFor(x => x.Diagnostico).MaximumLength(2000);
        RuleFor(x => x.Observaciones).MaximumLength(2000);
    }
}

public class CambiarEstadoOrdenValidador : AbstractValidator<CambiarEstadoOrdenPeticion>
{
    public CambiarEstadoOrdenValidador()
    {
        RuleFor(x => x.Estado).NotEmpty().WithMessage("Hay que indicar el estado destino.");
        RuleFor(x => x.Observacion).MaximumLength(500);
    }
}

public class AsignarTecnicoValidador : AbstractValidator<AsignarTecnicoPeticion>
{
    public AsignarTecnicoValidador()
    {
        RuleFor(x => x.TecnicoId).GreaterThan(0).WithMessage("Hay que indicar el tecnico.");
    }
}

public class AnularOrdenValidador : AbstractValidator<AnularOrdenPeticion>
{
    public AnularOrdenValidador()
    {
        RuleFor(x => x.Motivo).NotEmpty().WithMessage("Anular una orden exige un motivo.").MaximumLength(500);
    }
}

public class AgregarServicioValidador : AbstractValidator<AgregarServicioPeticion>
{
    public AgregarServicioValidador()
    {
        RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad tiene que ser mayor que cero.");
        RuleFor(x => x.Precio).GreaterThanOrEqualTo(0).WithMessage("El precio no puede ser negativo.")
            .When(x => x.Precio.HasValue);
        RuleFor(x => x.Descripcion).MaximumLength(200);
        RuleFor(x => x)
            .Must(x => x.ServicioId.HasValue || !string.IsNullOrWhiteSpace(x.Descripcion))
            .WithMessage("Indica un servicio del catalogo o una descripcion.");
    }
}

public class ConsumirRepuestoValidador : AbstractValidator<ConsumirRepuestoPeticion>
{
    public ConsumirRepuestoValidador()
    {
        RuleFor(x => x.ProductoId).GreaterThan(0).WithMessage("Hay que indicar el producto.");
        RuleFor(x => x.SedeId).GreaterThan(0).WithMessage("Hay que indicar la sede.");
        RuleFor(x => x.Cantidad).GreaterThan(0).WithMessage("La cantidad tiene que ser mayor que cero.");
    }
}

public class GuardarChecklistValidador : AbstractValidator<GuardarChecklistPeticion>
{
    private static readonly string[] Resultados = ["OK", "OBSERVADO", "MALO", "NA"];

    public GuardarChecklistValidador()
    {
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(r => r.Item).NotEmpty().WithMessage("Cada punto necesita nombre.").MaximumLength(120);
            i.RuleFor(r => r.Resultado).Must(v => Resultados.Contains(v))
                .WithMessage("El resultado va OK / OBSERVADO / MALO / NA.");
            i.RuleFor(r => r.Nota).MaximumLength(300);
        });
    }
}

public class GuardarChecklistItemValidador : AbstractValidator<GuardarChecklistItemPeticion>
{
    public GuardarChecklistItemValidador()
    {
        RuleFor(x => x.Codigo).NotEmpty().WithMessage("El punto necesita un codigo.").MaximumLength(40);
        RuleFor(x => x.Nombre).NotEmpty().WithMessage("El punto necesita un nombre.").MaximumLength(120);
        RuleFor(x => x.Orden).GreaterThanOrEqualTo(0);
    }
}

public class SubirEvidenciaValidador : AbstractValidator<SubirEvidenciaPeticion>
{
    private static readonly string[] Tipos = ["FOTO", "DOCUMENTO"];

    public SubirEvidenciaValidador()
    {
        RuleFor(x => x.Tipo).Must(t => Tipos.Contains(t)).WithMessage("El tipo va FOTO o DOCUMENTO.");
        RuleFor(x => x.ContenidoBase64).NotEmpty().WithMessage("Falta el contenido del archivo.");
        RuleFor(x => x.Nombre).MaximumLength(200);
    }
}
