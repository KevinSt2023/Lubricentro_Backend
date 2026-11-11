using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LubriTech.Infraestructura.Seguridad;

public record TokenEmitido(string Token, DateTimeOffset ExpiraEn);

/// <summary>
/// Arma los JWT. La audiencia es la frontera entre los dos mundos: un token
/// del portal del cliente lleva <see cref="Audiencias.Cliente"/> y el ERP no
/// lo acepta, pase lo que pase con los permisos.
/// </summary>
public interface ITokenServicio
{
    TokenEmitido ParaPersonal(Usuario usuario);
    TokenEmitido ParaCliente(ClienteAcceso acceso, Cliente cliente);
}

public class TokenServicio(IOptions<OpcionesJwt> opciones) : ITokenServicio
{
    private readonly OpcionesJwt _o = opciones.Value;

    public TokenEmitido ParaPersonal(Usuario usuario)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, usuario.Email),
            new(Claims.Rol, usuario.RolId.ToString()),
            new(Claims.SesionVersion, usuario.SesionVersion.ToString())
        };

        if (usuario.EmpresaId.HasValue)
            claims.Add(new Claim(Claims.Empresa, usuario.EmpresaId.Value.ToString()));

        if (usuario.SedeId.HasValue)
            claims.Add(new Claim(Claims.Sede, usuario.SedeId.Value.ToString()));

        return Emitir(claims, Audiencias.Erp);
    }

    public TokenEmitido ParaCliente(ClienteAcceso acceso, Cliente cliente)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, acceso.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, acceso.Email),
            new(Claims.Acceso, acceso.Id.ToString()),
            new(Claims.Cliente, cliente.Id.ToString()),
            new(Claims.Empresa, cliente.EmpresaId.ToString()),
            new(Claims.SesionVersion, acceso.SesionVersion.ToString())
        };

        // A proposito NO lleva claim de rol: un cliente no tiene rol del ERP.
        return Emitir(claims, Audiencias.Cliente);
    }

    private TokenEmitido Emitir(List<Claim> claims, string audiencia)
    {
        if (string.IsNullOrWhiteSpace(_o.Clave) || _o.Clave.Length < 32)
            throw new InvalidOperationException(
                "Falta configurar Jwt:Clave con al menos 32 caracteres.");

        var expira = Tiempo.Ahora.AddMinutes(_o.MinutosToken);
        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.Clave)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _o.Emisor,
            audience: audiencia,
            claims: claims,
            notBefore: Tiempo.Ahora.UtcDateTime,
            expires: expira.UtcDateTime,
            signingCredentials: credenciales);

        return new TokenEmitido(new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}

/// <summary>
/// Los nombres de claim estandar. Se escriben cortos a mano en vez de usar
/// ClaimTypes.* para que el token no cargue URIs largas en cada peticion.
/// </summary>
internal static class JwtRegisteredClaimNames
{
    public const string Sub = "sub";
    public const string Email = "email";
}
