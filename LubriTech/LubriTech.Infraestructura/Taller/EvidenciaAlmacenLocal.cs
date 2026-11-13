using LubriTech.Aplicacion.Servicios;
using Microsoft.Extensions.Configuration;

namespace LubriTech.Infraestructura.Taller;

/// <summary>
/// Guarda las evidencias en el disco local, bajo una carpeta por empresa y por
/// orden. En produccion esta misma interfaz se puede apuntar a un almacen de
/// objetos sin tocar el resto. La ruta base sale de configuracion
/// (Evidencias:Ruta) o, por defecto, de App_Data junto al ejecutable.
///
/// El nombre del archivo es su propio hash: dos subidas identicas no se
/// duplican, y el nombre no deja filtrar nada del original.
/// </summary>
public class EvidenciaAlmacenLocal : IEvidenciaAlmacen
{
    private readonly string _base;

    public EvidenciaAlmacenLocal(IConfiguration config)
    {
        _base = config["Evidencias:Ruta"]
                ?? Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "evidencias");
    }

    public async Task<string> GuardarAsync(
        int empresaId, long ordenId, string hash, string extension, byte[] datos, CancellationToken ct = default)
    {
        var relativa = Path.Combine(empresaId.ToString(), ordenId.ToString(), $"{hash}.{extension}");
        var absoluta = Path.Combine(_base, relativa);

        Directory.CreateDirectory(Path.GetDirectoryName(absoluta)!);
        await File.WriteAllBytesAsync(absoluta, datos, ct);

        // Siempre con separador '/' para que la ruta guardada sea portable.
        return relativa.Replace('\\', '/');
    }

    public async Task<byte[]?> LeerAsync(string rutaRelativa, CancellationToken ct = default)
    {
        // Defensa contra traversal: la ruta tiene que quedar dentro de la base.
        var absoluta = Path.GetFullPath(Path.Combine(_base, rutaRelativa));
        var raiz = Path.GetFullPath(_base);
        if (!absoluta.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) return null;
        if (!File.Exists(absoluta)) return null;

        return await File.ReadAllBytesAsync(absoluta, ct);
    }
}
