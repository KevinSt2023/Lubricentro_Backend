using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace LubriTech.Infraestructura.Seguridad;

/// <summary>
/// Hash de contrasenas con el de ASP.NET Core Identity (PBKDF2, formato
/// versionado, comparacion en tiempo constante). No se inventa nada propio.
///
/// Se usa el hasher de Identity, no el stack completo de Identity: el
/// esquema de la base es nuestro, escrito a mano.
/// </summary>
public interface IClaves
{
    string Hash(string clave);

    /// <summary>true si la clave coincide. <paramref name="rehashear"/> avisa
    /// cuando el hash guardado quedo con parametros viejos y conviene
    /// regrabarlo con los de hoy.</summary>
    bool Verificar(string hashGuardado, string clave, out bool rehashear);

    /// <summary>Token opaco para refresh y recuperacion. Se devuelve en claro
    /// una sola vez; en la base solo va su hash.</summary>
    string NuevoTokenOpaco(int bytes = 48);

    /// <summary>SHA-256 en hex. Para guardar tokens y huellas de cuerpo.</summary>
    string Huella(string valor);

    /// <summary>
    /// Clave temporal para una cuenta recien creada: se dicta por telefono,
    /// asi que no lleva caracteres que se confundan al leerlos (O/0, l/1/I).
    /// Cumple la politica minima (letras y numeros) y el usuario la cambia al
    /// entrar, porque la cuenta nace con debe_cambiar_clave.
    /// </summary>
    string NuevaClaveTemporal();
}

public class Claves : IClaves
{
    private sealed class Sujeto;

    private readonly PasswordHasher<Sujeto> _hasher = new();
    private static readonly Sujeto Instancia = new();

    public string Hash(string clave) => _hasher.HashPassword(Instancia, clave);

    public bool Verificar(string hashGuardado, string clave, out bool rehashear)
    {
        rehashear = false;
        if (string.IsNullOrWhiteSpace(hashGuardado)) return false;

        var resultado = _hasher.VerifyHashedPassword(Instancia, hashGuardado, clave);
        rehashear = resultado == PasswordVerificationResult.SuccessRehashNeeded;

        return resultado is PasswordVerificationResult.Success
                         or PasswordVerificationResult.SuccessRehashNeeded;
    }

    public string NuevoTokenOpaco(int bytes = 48) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes))
               .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    public string Huella(string valor) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valor))).ToLowerInvariant();

    // Sin O, 0, l, 1, I: estas claves se dictan por telefono.
    private const string Letras = "abcdefghjkmnpqrstuvwxyz";
    private const string Mayusculas = "ABCDEFGHJKMNPQRSTUVWXYZ";
    private const string Digitos = "23456789";

    public string NuevaClaveTemporal()
    {
        var clave = new char[12];

        clave[0] = Tomar(Mayusculas);
        for (var i = 1; i <= 5; i++) clave[i] = Tomar(Letras);
        for (var i = 6; i <= 9; i++) clave[i] = Tomar(Digitos);
        clave[10] = Tomar(Letras);
        clave[11] = Tomar(Mayusculas);

        return new string(clave);

        static char Tomar(string alfabeto) =>
            alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
    }
}
