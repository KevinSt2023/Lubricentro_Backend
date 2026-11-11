using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;

namespace LubriTech.Aplicacion.Servicios;

/// <summary>
/// Los dos mundos de identidad, cada uno con su puerta. No hay una sola
/// puerta con un "if es cliente": son flujos distintos y tokens distintos.
/// </summary>
public interface IAutenticacionServicio
{
    Task<Resultado<SesionRespuesta>> LoginPersonalAsync(LoginPeticion peticion, CancellationToken ct = default);
    Task<Resultado<SesionRespuesta>> LoginClienteAsync(LoginPeticion peticion, CancellationToken ct = default);

    Task<Resultado<SesionRespuesta>> RefrescarPersonalAsync(RefrescarPeticion peticion, CancellationToken ct = default);
    Task<Resultado<SesionRespuesta>> RefrescarClienteAsync(RefrescarPeticion peticion, CancellationToken ct = default);

    Task<Resultado> CerrarSesionAsync(string? refreshToken, CancellationToken ct = default);

    /// <summary>Cierra TODAS las sesiones del usuario: sube su sesion_version y revoca sus refresh.</summary>
    Task<Resultado> CerrarTodasLasSesionesAsync(CancellationToken ct = default);

    Task<Resultado<PerfilRespuesta>> PerfilAsync(CancellationToken ct = default);
    Task<Resultado> CambiarClaveAsync(CambiarClavePeticion peticion, CancellationToken ct = default);
}

/// <summary>Permiso efectivo = lo que el rol puede, intersectado con lo que el plan habilita.</summary>
public interface IPermisoServicio
{
    Task<IReadOnlyCollection<string>> PermisosEfectivosAsync(int empresaId, int rolId, CancellationToken ct = default);
    Task<IReadOnlyCollection<string>> FuncionesDelPlanAsync(int empresaId, CancellationToken ct = default);
    Task<bool> TienePermisoAsync(int empresaId, int rolId, string permiso, CancellationToken ct = default);

    /// <summary>Tira el cache de una empresa al instante (cambio de plan, de matriz o de estado).</summary>
    void Invalidar(int empresaId);
}

/// <summary>
/// Comprueba en cada peticion que la sesion siga valiendo: usuario activo,
/// sesion_version igual a la del token, empresa activa y al dia. Con cache
/// corto, invalidado al instante cuando algo cambia.
/// </summary>
public interface IValidadorSesion
{
    Task<Resultado> ValidarAsync(ISesionActual sesion, CancellationToken ct = default);
    void Invalidar(int? usuarioId = null, long? accesoId = null, int? empresaId = null);
}

/// <summary>Escribe en la bitacora. Nunca lanza: un fallo de auditoria no tumba la operacion, pero se loguea.</summary>
public interface IBitacoraServicio
{
    Task RegistrarAsync(string accion, string entidad, string? entidadId = null,
                        object? detalle = null, CancellationToken ct = default);
}
