using FluentValidation;
using LubriTech.Aplicacion.Comun;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LubriTech.API.Seguridad;

/// <summary>
/// Valida los cuerpos de peticion con FluentValidation, como filtro global.
///
/// Por que global y no un if en cada accion: porque el if se olvida. Si un
/// contrato tiene validador registrado, se aplica; si no, pasa. Agregar el
/// validador es lo unico que hay que hacer.
/// </summary>
public class ValidacionDeEntradaFiltro(IServiceProvider servicios) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate siguiente)
    {
        foreach (var argumento in contexto.ActionArguments.Values)
        {
            if (argumento is null) continue;

            var tipoValidador = typeof(IValidator<>).MakeGenericType(argumento.GetType());
            if (servicios.GetService(tipoValidador) is not IValidator validador) continue;

            var contextoValidacion = new ValidationContext<object>(argumento);
            var resultado = await validador.ValidateAsync(contextoValidacion);

            if (resultado.IsValid) continue;

            var mensaje = string.Join(" ", resultado.Errors.Select(e => e.ErrorMessage).Distinct());
            contexto.Result = Respuestas.De(TipoFallo.Validacion, mensaje, "VALIDACION");
            return;
        }

        await siguiente();
    }
}
