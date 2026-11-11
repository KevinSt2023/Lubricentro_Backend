using LubriTech.Aplicacion.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace LubriTech.API.TiempoReal;

/// <summary>
/// Nombres de los grupos. Un solo lugar, porque el que publica y el que se
/// suscribe tienen que coincidir exactamente o el evento se pierde en
/// silencio, que es la peor forma de fallar.
/// </summary>
public static class Grupos
{
    public static string Empresa(int empresaId) => $"empresa:{empresaId}";
    public static string Sede(int sedeId) => $"sede:{sedeId}";
    public static string Usuario(int usuarioId) => $"usuario:{usuarioId}";
    public static string Cliente(long clienteId) => $"cliente:{clienteId}";
    public static string Orden(long ordenId) => $"orden:{ordenId}";
}

/// <summary>
/// Hub del ERP web: tablero y pantallas de gestion. Grupos por empresa y
/// por sede. El cliente del portal no entra aqui: se corta por audiencia.
/// </summary>
[Authorize]
public class ErpHub(ISesionActual sesion) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (sesion.EsCliente)
        {
            Context.Abort();
            return;
        }

        if (sesion.EmpresaId > 0)
            await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Empresa(sesion.EmpresaId));

        if (sesion.SedeId is > 0)
            await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Sede(sesion.SedeId.Value));

        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Hub de la app de operaciones: lo que le toca al trabajador. Grupos por
/// sede y por usuario, para poder avisarle a uno solo.
/// </summary>
[Authorize]
public class OperacionesHub(ISesionActual sesion) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (sesion.EsCliente || sesion.UsuarioId is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Usuario(sesion.UsuarioId.Value));

        if (sesion.SedeId is > 0)
            await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Sede(sesion.SedeId.Value));

        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Hub del portal del cliente. Un unico grupo: el del propio cliente.
///
/// REGLA: por aqui NO sale el evento crudo del taller. Sale una proyeccion
/// ya filtrada, armada aparte. Si el evento interno pudiera publicarse en
/// este hub, bastaria un campo olvidado para que el cliente leyera el costo
/// de adquisicion o la nota interna del tecnico.
/// </summary>
[Authorize]
public class ClientesHub(ISesionActual sesion) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!sesion.EsCliente || sesion.ClienteId is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Grupos.Cliente(sesion.ClienteId.Value));
        await base.OnConnectedAsync();
    }
}
