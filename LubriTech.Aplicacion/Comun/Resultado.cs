namespace LubriTech.Aplicacion.Comun;

/// <summary>
/// Cada fallo sabe con que codigo HTTP debe salir. Asi el controlador no
/// adivina y no termina devolviendo 500 por una regla de negocio.
/// Regla de la casa: cero 500, y ningun 400 que frene un flujo legitimo.
/// </summary>
public enum TipoFallo
{
    Ninguno = 0,
    Validacion = 400,
    NoAutenticado = 401,
    Prohibido = 403,
    NoEncontrado = 404,
    Conflicto = 409,
    Bloqueado = 423,
    DemasiadasPeticiones = 429
}

public class Resultado
{
    public bool Exito { get; init; }
    public TipoFallo Fallo { get; init; } = TipoFallo.Ninguno;
    public string? Mensaje { get; init; }
    public string? Codigo { get; init; }

    public static Resultado Ok() => new() { Exito = true };

    public static Resultado Error(TipoFallo tipo, string mensaje, string? codigo = null) =>
        new() { Exito = false, Fallo = tipo, Mensaje = mensaje, Codigo = codigo };
}

public class Resultado<T> : Resultado
{
    public T? Valor { get; init; }

    public static Resultado<T> Ok(T valor) => new() { Exito = true, Valor = valor };

    public static new Resultado<T> Error(TipoFallo tipo, string mensaje, string? codigo = null) =>
        new() { Exito = false, Fallo = tipo, Mensaje = mensaje, Codigo = codigo };
}
