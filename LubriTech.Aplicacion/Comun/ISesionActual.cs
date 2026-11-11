namespace LubriTech.Aplicacion.Comun;

/// <summary>
/// Quien esta pidiendo. Lo resuelve la API desde el token y lo consume todo
/// lo demas, incluido el filtro de empresa del contexto de EF.
///
/// REGLA: <see cref="EmpresaId"/> devuelve -1 cuando no hay empresa resuelta.
/// No 0, no null: -1, porque ninguna empresa tiene ese id y el filtro queda
/// cerrado. Fallar cerrado significa que un olvido no abre datos de nadie.
/// </summary>
public interface ISesionActual
{
    bool Autenticado { get; }

    /// <summary>Audiencia del token: ERP o portal del cliente.</summary>
    string? Audiencia { get; }

    /// <summary>-1 cuando no hay empresa resuelta.</summary>
    int EmpresaId { get; }

    int? UsuarioId { get; }
    long? AccesoId { get; }
    long? ClienteId { get; }
    int? SedeId { get; }
    int? RolId { get; }
    int SesionVersion { get; }

    bool EsSuperAdmin { get; }
    bool EsCliente { get; }

    string? Ip { get; }
    string? Agente { get; }
    string Origen { get; }
}
