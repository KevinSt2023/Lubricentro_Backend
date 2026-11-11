using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Servicios;

// ===========================================================================
// Los maestros del taller son de la EMPRESA: el empresaId sale siempre del
// token del que llama, nunca de la ruta ni del cuerpo. Por eso estos servicios
// lo reciben como primer parametro y el controlador lo saca de sesion.EmpresaId.
// Las lecturas van por el filtro global de empresa; las altas fijan EmpresaId.
// ===========================================================================

public interface IPersonalServicio
{
    Task<Pagina<PersonalItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        int? rolId = null, int? sedeId = null, bool? activo = null, CancellationToken ct = default);

    Task<Resultado<PersonalItem>> VerAsync(int empresaId, int usuarioId, CancellationToken ct = default);

    /// <summary>
    /// Alta de una persona del equipo. El cupo por rol del plan lo vigila la
    /// base (fn_limite_plan_usuario); aqui se comprueba antes para dar un
    /// mensaje claro. Devuelve la clave temporal una sola vez.
    /// </summary>
    Task<Resultado<PersonalCreado>> CrearAsync(
        int empresaId, GuardarPersonalPeticion peticion, CancellationToken ct = default);

    Task<Resultado<PersonalItem>> ActualizarAsync(
        int empresaId, int usuarioId, ActualizarPersonalPeticion peticion, CancellationToken ct = default);

    /// <summary>Activar o desactivar. Desactivar corta las sesiones al instante.</summary>
    Task<Resultado<PersonalItem>> CambiarEstadoAsync(
        int empresaId, int usuarioId, EstadoPeticion peticion, CancellationToken ct = default);

    /// <summary>Clave temporal nueva. Cierra las sesiones abiertas de esa persona.</summary>
    Task<Resultado<string>> ReiniciarClaveAsync(int empresaId, int usuarioId, CancellationToken ct = default);
}

public interface IClienteServicio
{
    Task<Pagina<ClienteItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        string? tipo = null, bool? activo = null, CancellationToken ct = default);

    Task<Resultado<ClienteDetalle>> VerAsync(int empresaId, long clienteId, CancellationToken ct = default);

    Task<Resultado<ClienteDetalle>> CrearAsync(
        int empresaId, GuardarClientePeticion peticion, CancellationToken ct = default);

    Task<Resultado<ClienteDetalle>> ActualizarAsync(
        int empresaId, long clienteId, GuardarClientePeticion peticion, CancellationToken ct = default);

    Task<Resultado<ClienteDetalle>> CambiarEstadoAsync(
        int empresaId, long clienteId, EstadoPeticion peticion, CancellationToken ct = default);

    /// <summary>
    /// Le abre al cliente su cuenta del portal. Necesita correo y la funcion
    /// APP_CLIENTES del plan. Devuelve la clave temporal una sola vez.
    /// </summary>
    Task<Resultado<PortalHabilitado>> HabilitarPortalAsync(
        int empresaId, long clienteId, CancellationToken ct = default);
}

public interface IVehiculoServicio
{
    Task<Pagina<VehiculoItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        long? clienteId = null, bool? activo = null, CancellationToken ct = default);

    Task<Resultado<VehiculoDetalle>> VerAsync(int empresaId, long vehiculoId, CancellationToken ct = default);

    Task<Resultado<VehiculoDetalle>> CrearAsync(
        int empresaId, GuardarVehiculoPeticion peticion, CancellationToken ct = default);

    Task<Resultado<VehiculoDetalle>> ActualizarAsync(
        int empresaId, long vehiculoId, GuardarVehiculoPeticion peticion, CancellationToken ct = default);

    Task<Resultado<VehiculoDetalle>> CambiarEstadoAsync(
        int empresaId, long vehiculoId, EstadoPeticion peticion, CancellationToken ct = default);

    /// <summary>Anota una nueva lectura de odometro. Nunca por debajo de la anterior.</summary>
    Task<Resultado<VehiculoDetalle>> RegistrarKilometrajeAsync(
        int empresaId, long vehiculoId, KilometrajePeticion peticion, CancellationToken ct = default);
}

public interface IServicioCatalogoServicio
{
    Task<Pagina<ServicioItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        string? categoria = null, bool? activo = null, CancellationToken ct = default);

    Task<Resultado<ServicioItem>> VerAsync(int empresaId, long servicioId, CancellationToken ct = default);

    Task<Resultado<ServicioItem>> CrearAsync(
        int empresaId, GuardarServicioPeticion peticion, CancellationToken ct = default);

    Task<Resultado<ServicioItem>> ActualizarAsync(
        int empresaId, long servicioId, GuardarServicioPeticion peticion, CancellationToken ct = default);

    Task<Resultado<ServicioItem>> CambiarEstadoAsync(
        int empresaId, long servicioId, EstadoPeticion peticion, CancellationToken ct = default);
}

public interface IAlmacenServicio
{
    Task<Pagina<ProductoItem>> ListarAsync(
        int empresaId, ConsultaPaginada consulta,
        string? tipo = null, bool? activo = null, bool soloBajoMinimo = false, CancellationToken ct = default);

    Task<Resultado<ProductoDetalle>> VerAsync(int empresaId, long productoId, CancellationToken ct = default);

    /// <summary>Busca por codigo de barras. Para el lector del almacen.</summary>
    Task<Resultado<ProductoDetalle>> PorCodigoBarrasAsync(
        int empresaId, string codigoBarras, CancellationToken ct = default);

    Task<Resultado<ProductoDetalle>> CrearAsync(
        int empresaId, GuardarProductoPeticion peticion, CancellationToken ct = default);

    Task<Resultado<ProductoDetalle>> ActualizarAsync(
        int empresaId, long productoId, GuardarProductoPeticion peticion, CancellationToken ct = default);

    Task<Resultado<ProductoDetalle>> CambiarEstadoAsync(
        int empresaId, long productoId, EstadoPeticion peticion, CancellationToken ct = default);

    /// <summary>
    /// Ingresa mercaderia a una sede. Escribe un movimiento de kardex; el
    /// trigger de la base actualiza el stock y el costo promedio.
    /// </summary>
    Task<Resultado<ProductoDetalle>> IngresarAsync(
        int empresaId, long productoId, int? usuarioId, IngresoStockPeticion peticion, CancellationToken ct = default);

    /// <summary>
    /// Ajusta el stock de una sede al valor contado. Deriva un AJUSTE_POS o
    /// AJUSTE_NEG segun la diferencia. Si ya cuadra, no mueve nada.
    /// </summary>
    Task<Resultado<ProductoDetalle>> AjustarAsync(
        int empresaId, long productoId, int? usuarioId, AjusteStockPeticion peticion, CancellationToken ct = default);

    /// <summary>El kardex de un producto, lo mas reciente primero. Paginado.</summary>
    Task<Resultado<Pagina<MovimientoKardexItem>>> KardexAsync(
        int empresaId, long productoId, ConsultaPaginada consulta, int? sedeId = null, CancellationToken ct = default);
}
