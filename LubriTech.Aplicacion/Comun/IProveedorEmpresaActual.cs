namespace LubriTech.Aplicacion.Comun;

/// <summary>
/// Lo unico que el contexto de EF necesita saber para filtrar: que empresa
/// esta pidiendo. Se registra como SINGLETON a proposito.
///
/// Por que singleton: EF cachea el modelo una sola vez por tipo de contexto,
/// y el filtro global queda con una referencia a este objeto dentro. Si el
/// objeto fuera por peticion, el modelo cacheado se quedaria pegado al
/// primero que lo construyo y todas las peticiones siguientes filtrarian por
/// la empresa equivocada. Siendo singleton, la referencia siempre es la
/// misma y el valor se lee del contexto de la peticion en curso.
///
/// Devuelve -1 cuando no hay empresa resuelta: el filtro queda cerrado.
/// </summary>
public interface IProveedorEmpresaActual
{
    int EmpresaId { get; }
}
