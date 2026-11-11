namespace LubriTech.Aplicacion.Comun;

/// <summary>
/// Todo listado que puede crecer se pagina. No hay endpoint que devuelva
/// "todas" las filas de algo que un negocio acumula con los anos: el dia que
/// una empresa tenga 40 000 ordenes, ese endpoint se cae y se lleva la
/// pantalla con el.
/// </summary>
public class ConsultaPaginada
{
    private const int TamanoMaximo = 100;
    private int _tamano = 20;
    private int _pagina = 1;

    public int Pagina
    {
        get => _pagina;
        set => _pagina = value < 1 ? 1 : value;
    }

    /// <summary>Tope duro: aunque pidan 10 000, se sirven 100.</summary>
    public int Tamano
    {
        get => _tamano;
        set => _tamano = value switch
        {
            < 1 => 20,
            > TamanoMaximo => TamanoMaximo,
            _ => value
        };
    }

    /// <summary>Texto libre de busqueda. Nulo o vacio = sin filtrar.</summary>
    public string? Buscar { get; set; }

    public int Saltar => (Pagina - 1) * Tamano;
}

public class Pagina<T>
{
    public List<T> Items { get; init; } = [];
    public int PaginaActual { get; init; }
    public int Tamano { get; init; }
    public int Total { get; init; }

    public int TotalPaginas => Tamano <= 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamano);
    public bool HayMas => PaginaActual < TotalPaginas;

    public static Pagina<T> De(List<T> items, ConsultaPaginada consulta, int total) => new()
    {
        Items = items,
        PaginaActual = consulta.Pagina,
        Tamano = consulta.Tamano,
        Total = total
    };
}
