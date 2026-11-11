using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Datos;

/// <summary>
/// Transacciones que conviven con EnableRetryOnFailure.
///
/// El porque de este archivo: la conexion esta configurada con reintentos
/// (NpgsqlRetryingExecutionStrategy) para aguantar un corte momentaneo de la
/// base. Esa estrategia PROHIBE abrir una transaccion a mano: si alguien hace
/// BeginTransactionAsync y luego SaveChanges, EF lanza
/// InvalidOperationException y la peticion termina en 500. Lo encontro el
/// arnes de la Fase 1 en el alta de una empresa.
///
/// La forma correcta es pedirle a EF la estrategia y entregarle TODO el
/// bloque como una sola unidad reintentable. Eso es lo que hace esto, y por
/// eso ningun servicio vuelve a llamar a BeginTransactionAsync directamente.
///
/// Sobre el reintento: si el primer intento se cayo, su transaccion quedo
/// deshecha, pero las entidades que se alcanzaron a marcar como Added siguen
/// en el rastreador. Si se reintentara sin mas, el segundo intento las
/// insertaria dos veces. Por eso antes de cada reintento se sueltan las
/// entidades Added: el bloque arranca limpio. Las Modified se conservan,
/// porque son filas que el servicio leyo antes de entrar aqui.
/// </summary>
public static class Transacciones
{
    public static async Task<T> EnTransaccionAsync<T>(
        this LubriTechContexto bd,
        Func<CancellationToken, Task<T>> trabajo,
        CancellationToken ct = default)
    {
        var estrategia = bd.Database.CreateExecutionStrategy();
        var intento = 0;

        return await estrategia.ExecuteAsync(async () =>
        {
            if (intento++ > 0) SoltarLoNuevo(bd);

            await using var transaccion = await bd.Database.BeginTransactionAsync(ct);

            var resultado = await trabajo(ct);

            await transaccion.CommitAsync(ct);
            return resultado;
        });
    }

    public static Task EnTransaccionAsync(
        this LubriTechContexto bd,
        Func<CancellationToken, Task> trabajo,
        CancellationToken ct = default) =>
        bd.EnTransaccionAsync(async token =>
        {
            await trabajo(token);
            return true;
        }, ct);

    private static void SoltarLoNuevo(LubriTechContexto bd)
    {
        foreach (var entrada in bd.ChangeTracker.Entries()
                                  .Where(e => e.State == EntityState.Added)
                                  .ToList())
            entrada.State = EntityState.Detached;
    }
}
