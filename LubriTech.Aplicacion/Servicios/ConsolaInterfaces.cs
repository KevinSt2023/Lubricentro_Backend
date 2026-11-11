using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Servicios;

/// <summary>
/// Alta y vida de las empresas, desde la consola del SuperAdmin.
/// Todo lo de aqui cruza empresas a proposito, asi que las consultas van con
/// IgnoreQueryFilters() y los endpoints estan cerrados al rol 0.
/// </summary>
public interface IEmpresaConsolaServicio
{
    Task<Pagina<EmpresaListaItem>> ListarAsync(
        ConsultaPaginada consulta, string? estado = null, int? planId = null, CancellationToken ct = default);

    Task<Resultado<EmpresaDetalle>> VerAsync(int empresaId, CancellationToken ct = default);

    Task<Resultado<EmpresaCreada>> CrearAsync(CrearEmpresaPeticion peticion, CancellationToken ct = default);

    Task<Resultado<EmpresaDetalle>> ActualizarAsync(
        int empresaId, ActualizarEmpresaPeticion peticion, CancellationToken ct = default);

    Task<Resultado<EmpresaDetalle>> CambiarPlanAsync(
        int empresaId, CambiarPlanPeticion peticion, CancellationToken ct = default);

    /// <summary>Dar de baja o reactivar. La baja corta las sesiones al instante.</summary>
    Task<Resultado<EmpresaDetalle>> CambiarEstadoAsync(
        int empresaId, EstadoEmpresaPeticion peticion, CancellationToken ct = default);

    Task<Resultado<SaludEmpresa>> SaludAsync(int empresaId, CancellationToken ct = default);

    /// <summary>Clave temporal nueva para el Admin de la empresa. Se devuelve una sola vez.</summary>
    Task<Resultado<string>> ReiniciarClaveDelAdminAsync(int empresaId, CancellationToken ct = default);
}

public interface IPlanConsolaServicio
{
    Task<List<PlanDetalle>> ListarAsync(CancellationToken ct = default);
    Task<Resultado<PlanDetalle>> VerAsync(int planId, CancellationToken ct = default);
    Task<List<FuncionItem>> FuncionesAsync(CancellationToken ct = default);

    Task<Resultado<PlanDetalle>> CrearAsync(GuardarPlanPeticion peticion, CancellationToken ct = default);

    /// <summary>
    /// Guardar un plan puede dejar empresas fuera de norma (bajar un cupo por
    /// debajo de lo que ya usan). En ese caso se rechaza con 409 y se dice
    /// cuales, en vez de dejarlas calladamente excedidas.
    /// </summary>
    Task<Resultado<PlanDetalle>> ActualizarAsync(
        int planId, GuardarPlanPeticion peticion, CancellationToken ct = default);
}

public interface ISuscripcionConsolaServicio
{
    Task<Pagina<SuscripcionItem>> ListarAsync(
        ConsultaPaginada consulta, int? empresaId = null, CancellationToken ct = default);

    /// <summary>
    /// Registra un pago y extiende el servicio. El periodo arranca el dia
    /// siguiente al ultimo pagado, o hoy si el servicio ya habia vencido: ni
    /// un dia se regala, ni un dia se cobra dos veces.
    /// </summary>
    Task<Resultado<SuscripcionItem>> RegistrarAsync(
        int empresaId, RegistrarSuscripcionPeticion peticion, CancellationToken ct = default);

    /// <summary>Anula un pago y recalcula hasta cuando llega el servicio.</summary>
    Task<Resultado<SuscripcionItem>> AnularAsync(
        long suscripcionId, AnularSuscripcionPeticion peticion, CancellationToken ct = default);
}

public interface ITableroSaaSServicio
{
    Task<TableroSaaS> VerAsync(CancellationToken ct = default);
}

/// <summary>
/// Sedes. Lo usan dos publicos con la misma logica: el SuperAdmin sobre
/// cualquier empresa, y el Admin sobre la suya. Quien puede que cosa lo
/// deciden los atributos del controlador, no este servicio.
/// </summary>
public interface ISedeServicio
{
    Task<Resultado<List<SedeItem>>> ListarAsync(int empresaId, CancellationToken ct = default);

    Task<Resultado<SedeItem>> CrearAsync(
        int empresaId, GuardarSedePeticion peticion, CancellationToken ct = default);

    Task<Resultado<SedeItem>> ActualizarAsync(
        int empresaId, int sedeId, GuardarSedePeticion peticion, CancellationToken ct = default);

    Task<Resultado<SedeItem>> MarcarPrincipalAsync(
        int empresaId, int sedeId, CancellationToken ct = default);
}
