using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LubriTech.Infraestructura.Datos;

/// <summary>
/// Crea las cuentas de arranque. Los hashes los genera la misma libreria que
/// despues los verifica, por eso esto no vive en el SQL.
///
/// Es idempotente: si la cuenta existe, no la toca — nunca le pisa la clave
/// a nadie. Todas las consultas van con IgnoreQueryFilters() porque no hay
/// sesion y el filtro de empresa esta cerrado.
///
/// Solo se ejecuta en Desarrollo. En produccion las cuentas se dan de alta
/// desde la consola del SuperAdmin.
/// </summary>
public class SembradoInicial(
    LubriTechContexto bd,
    IClaves claves,
    ILogger<SembradoInicial> log)
{
    private const string ClaveDev = "Admin2026*";

    private record Cuenta(string Email, int Rol, string Nombres, string Apellidos);

    public async Task EjecutarAsync(CancellationToken ct = default)
    {
        var creadas = new List<string>();

        // --- SuperAdmin: sin empresa, sin sede ---------------------------
        if (!await ExisteUsuarioAsync("superadmin@lubritech.pe", ct))
        {
            bd.Usuarios.Add(new Usuario
            {
                EmpresaId = null,
                SedeId = null,
                RolId = Roles.SuperAdmin,
                Nombres = "Super",
                Apellidos = "Admin",
                Email = "superadmin@lubritech.pe",
                PasswordHash = claves.Hash(ClaveDev)
            });
            creadas.Add("superadmin@lubritech.pe");
        }

        // --- Empresa de demostracion: el taller completo ------------------
        await SembrarPersonalAsync("20600123456", creadas, ct,
            new Cuenta("admin@lubritechdemo.pe", Roles.Admin, "Jorge", "Mendoza Rios"),
            new Cuenta("supervisor@lubritechdemo.pe", Roles.Supervisor, "Lucia", "Quispe Vargas"),
            new Cuenta("asesor@lubritechdemo.pe", Roles.Asesor, "Diego", "Palacios Luna"),
            new Cuenta("tecnico@lubritechdemo.pe", Roles.Tecnico, "Mateo", "Huaman Flores"),
            new Cuenta("cajero@lubritechdemo.pe", Roles.Cajero, "Rosa", "Ccahuana Peralta"),
            new Cuenta("almacen@lubritechdemo.pe", Roles.Almacenero, "Victor", "Salazar Ponce"));

        // --- Segunda empresa: existe para PROBAR el aislamiento -----------
        await SembrarPersonalAsync("20600987654", creadas, ct,
            new Cuenta("admin@lubritechvecino.pe", Roles.Admin, "Carmen", "Ibanez Rojas"),
            new Cuenta("tecnico@lubritechvecino.pe", Roles.Tecnico, "Pedro", "Aliaga Nunez"));

        // --- Cuentas del portal de los clientes de demostracion -----------
        await SembrarAccesoClienteAsync("20600123456", "44556677", "cliente@demo.pe", creadas, ct);
        await SembrarAccesoClienteAsync("20600987654", "33445566", "elena@vecino.pe", creadas, ct);

        await bd.SaveChangesAsync(ct);
        Informar(creadas);
    }

    private async Task SembrarPersonalAsync(
        string ruc, List<string> creadas, CancellationToken ct, params Cuenta[] cuentas)
    {
        var empresa = await bd.Empresas
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.Ruc == ruc, ct);

        if (empresa is null)
        {
            log.LogWarning("No existe la empresa {Ruc}: corre scripts/01_seed.sql. " +
                           "Se omite su personal.", ruc);
            return;
        }

        var sede = await bd.Sedes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.EmpresaId == empresa.Id && s.EsPrincipal, ct);

        if (sede is null)
        {
            log.LogWarning("La empresa {Ruc} no tiene sede principal. Se omite su personal.", ruc);
            return;
        }

        foreach (var cuenta in cuentas)
        {
            if (await ExisteUsuarioAsync(cuenta.Email, ct)) continue;

            bd.Usuarios.Add(new Usuario
            {
                EmpresaId = empresa.Id,
                SedeId = sede.Id,
                RolId = cuenta.Rol,
                Nombres = cuenta.Nombres,
                Apellidos = cuenta.Apellidos,
                Email = cuenta.Email,
                PasswordHash = claves.Hash(ClaveDev)
            });

            // De a uno: si el cupo del plan rechaza una alta, el candado de la
            // base levanta excepcion y hay que saber CUAL cuenta la provoco.
            await bd.SaveChangesAsync(ct);
            creadas.Add(cuenta.Email);
        }
    }

    private async Task SembrarAccesoClienteAsync(
        string ruc, string documento, string email, List<string> creadas, CancellationToken ct)
    {
        var cliente = await bd.Clientes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Empresa!.Ruc == ruc && c.Documento == documento, ct);

        if (cliente is null) return;

        var yaTiene = await bd.ClienteAccesos
            .IgnoreQueryFilters()
            .AnyAsync(a => a.ClienteId == cliente.Id, ct);

        if (yaTiene) return;

        bd.ClienteAccesos.Add(new ClienteAcceso
        {
            ClienteId = cliente.Id,
            Email = email,
            PasswordHash = claves.Hash(ClaveDev),
            EmailVerificado = true
        });

        await bd.SaveChangesAsync(ct);
        creadas.Add($"{email} (portal)");
    }

    private Task<bool> ExisteUsuarioAsync(string email, CancellationToken ct) =>
        bd.Usuarios.IgnoreQueryFilters()
          .AnyAsync(u => u.Email.ToLower() == email.ToLower(), ct);

    private void Informar(List<string> creadas)
    {
        if (creadas.Count == 0)
        {
            log.LogInformation("Sembrado: nada por crear, las cuentas ya existen.");
            return;
        }

        log.LogInformation("Sembrado: {Cantidad} cuenta(s) creada(s) con la clave de dev: {Cuentas}",
            creadas.Count, string.Join(", ", creadas));
    }
}
