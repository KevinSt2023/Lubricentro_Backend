using FluentValidation;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Validaciones;

public class LoginValidador : AbstractValidator<LoginPeticion>
{
    public LoginValidador()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("El correo es obligatorio.")
            .MaximumLength(200).WithMessage("El correo es demasiado largo.")
            .EmailAddress().WithMessage("El correo no tiene un formato valido.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("La contrasena es obligatoria.")
            .MaximumLength(200).WithMessage("La contrasena es demasiado larga.");

        RuleFor(x => x.Dispositivo!).SetValidator(new DispositivoValidador())
            .When(x => x.Dispositivo is not null);
    }
}

public class DispositivoValidador : AbstractValidator<DispositivoPeticion>
{
    private static readonly string[] Aplicaciones = ["OPERACIONES", "CLIENTES", "ERP_WEB"];
    private static readonly string[] Plataformas = ["ANDROID", "IOS", "WEB"];

    public DispositivoValidador()
    {
        RuleFor(x => x.Identificador)
            .NotEmpty().WithMessage("El identificador del dispositivo es obligatorio.")
            .MaximumLength(200);

        RuleFor(x => x.Aplicacion)
            .Must(v => Aplicaciones.Contains(v))
            .WithMessage("La aplicacion tiene que ser OPERACIONES, CLIENTES o ERP_WEB.");

        RuleFor(x => x.Plataforma)
            .Must(v => Plataformas.Contains(v))
            .WithMessage("La plataforma tiene que ser ANDROID, IOS o WEB.");

        RuleFor(x => x.TokenPush).MaximumLength(500);
        RuleFor(x => x.Modelo).MaximumLength(120);
        RuleFor(x => x.VersionApp).MaximumLength(40);
        RuleFor(x => x.VersionOs).MaximumLength(40);
    }
}

public class RefrescarValidador : AbstractValidator<RefrescarPeticion>
{
    public RefrescarValidador()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Falta el token de sesion.")
            .MaximumLength(500);
    }
}

public class CambiarClaveValidador : AbstractValidator<CambiarClavePeticion>
{
    public CambiarClaveValidador()
    {
        RuleFor(x => x.ClaveActual)
            .NotEmpty().WithMessage("La contrasena actual es obligatoria.");

        // Politica minima. No se exige una barbaridad: una clave que nadie
        // puede recordar termina pegada en un papel al lado de la caja.
        RuleFor(x => x.ClaveNueva)
            .NotEmpty().WithMessage("La contrasena nueva es obligatoria.")
            .MinimumLength(8).WithMessage("La contrasena nueva necesita al menos 8 caracteres.")
            .MaximumLength(200)
            .Matches("[A-Za-z]").WithMessage("La contrasena nueva necesita al menos una letra.")
            .Matches("[0-9]").WithMessage("La contrasena nueva necesita al menos un numero.")
            .NotEqual(x => x.ClaveActual).WithMessage("La contrasena nueva tiene que ser distinta de la actual.");
    }
}
