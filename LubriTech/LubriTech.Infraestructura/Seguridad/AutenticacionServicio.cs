using LubriTech.Aplicacion.Comun;
using LubriTech.Aplicacion.Contratos;
using LubriTech.Aplicacion.Servicios;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using LubriTech.Infraestructura.Datos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LubriTech.Infraestructura.Seguridad;

/// <summary>
/// Las dos puertas de entrada. Comparten las piezas (hash, rotacion, cupo de
/// intentos) pero no el flujo: el personal y el cliente son mundos separados
/// y sus tokens llevan audiencias distintas.
///
/// Todas las consultas de autenticacion van con IgnoreQueryFilters() porque
/// todavia no hay empresa resuelta: el filtro global vale -1 y, si no se
/// desactivara aqui, nadie podria entrar nunca. Es el unico lugar donde eso
/// es correcto, y por eso esta concentrado en esta clase.
/// </summary>
public class AutenticacionServicio(
    LubriTechContexto bd,
    IClaves claves,
    ITokenServicio tokens,
    IPermisoServicio permisos,
    IValidadorSesion validador,
    ISesionActual sesion,
    IOptions<OpcionesJwt> opciones,
    ILogger<AutenticacionServicio> log) : IAutenticacionServicio
{
    private readonly OpcionesJwt _o = opciones.Value;

    // Mensaje unico para credenciales malas: no se revela si el correo
    // existe. Decirlo regala la mitad del trabajo a quien prueba claves.
    private const string CredencialesMalas = "Correo o contrasena incorrectos.";

    // -----------------------------------------------------------------------
    // Login del personal
    // -----------------------------------------------------------------------

    public async Task<Resultado<SesionRespuesta>> LoginPersonalAsync(
        LoginPeticion peticion, CancellationToken ct = default)
    {
        var email = (peticion.Email ?? string.Empty).Trim().ToLowerInvariant();
        var ahora = Tiempo.Ahora;

        var usuario = await bd.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Rol)
            .Include(u => u.Empresa).ThenInclude(e => e!.Plan)
            .Include(u => u.Sede)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

        if (usuario is null)
            return Resultado<SesionRespuesta>.Error(
                TipoFallo.NoAutenticado, CredencialesMalas, "CREDENCIALES");

        if (usuario.EstaBloqueado(ahora))
            return Resultado<SesionRespuesta>.Error(TipoFallo.Bloqueado,
                $"Cuenta bloqueada por intentos fallidos. Vuelve a intentar en {_o.MinutosBloqueo} minutos.",
                "BLOQUEADO");

        if (!usuario.Activo)
            return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                "La cuenta esta desactivada.", "CUENTA_INACTIVA");

        if (!claves.Verificar(usuario.PasswordHash, peticion.Password ?? string.Empty, out var rehashear))
        {
            await ContarFalloPersonalAsync(usuario, ahora, ct);
            return Resultado<SesionRespuesta>.Error(
                TipoFallo.NoAutenticado, CredencialesMalas, "CREDENCIALES");
        }

        // La empresa tiene que estar al dia. El SuperAdmin no tiene empresa.
        if (usuario.RolId != Roles.SuperAdmin)
        {
            if (usuario.Empresa is null)
                return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                    "La cuenta no tiene empresa asignada.", "SIN_EMPRESA");

            if (!usuario.Empresa.Activo)
                return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                    "La empresa esta desactivada.", "EMPRESA_INACTIVA");

            if (usuario.Empresa.ServicioHasta < Tiempo.HoyLima())
                return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                    "El servicio esta vencido. Renueva para seguir usando el sistema.",
                    "SERVICIO_VENCIDO");
        }

        if (rehashear) usuario.PasswordHash = claves.Hash(peticion.Password!);

        usuario.IntentosFallidos = 0;
        usuario.BloqueadoHasta = null;
        usuario.UltimoAcceso = ahora;

        var dispositivo = await RegistrarDispositivoAsync(
            peticion.Dispositivo, usuarioId: usuario.Id, accesoId: null, ct);

        var token = tokens.ParaPersonal(usuario);
        var refresh = await NuevoRefreshPersonalAsync(usuario.Id, dispositivo?.Id, ct);

        await bd.SaveChangesAsync(ct);
        validador.Invalidar(usuarioId: usuario.Id);

        var perfil = await ArmarPerfilPersonalAsync(usuario, ct);

        return Resultado<SesionRespuesta>.Ok(new SesionRespuesta
        {
            Token = token.Token,
            RefreshToken = refresh,
            ExpiraEn = token.ExpiraEn,
            Audiencia = Audiencias.Erp,
            Perfil = perfil
        });
    }

    private async Task ContarFalloPersonalAsync(Usuario usuario, DateTimeOffset ahora, CancellationToken ct)
    {
        usuario.IntentosFallidos++;

        if (usuario.IntentosFallidos >= _o.IntentosAntesDeBloqueo)
        {
            usuario.BloqueadoHasta = ahora.AddMinutes(_o.MinutosBloqueo);
            usuario.IntentosFallidos = 0;
            log.LogWarning("Cuenta {Email} bloqueada por intentos fallidos", usuario.Email);
        }

        await bd.SaveChangesAsync(ct);
        validador.Invalidar(usuarioId: usuario.Id);
    }

    // -----------------------------------------------------------------------
    // Login del cliente (portal)
    // -----------------------------------------------------------------------

    public async Task<Resultado<SesionRespuesta>> LoginClienteAsync(
        LoginPeticion peticion, CancellationToken ct = default)
    {
        var email = (peticion.Email ?? string.Empty).Trim().ToLowerInvariant();
        var ahora = Tiempo.Ahora;

        var acceso = await bd.ClienteAccesos
            .IgnoreQueryFilters()
            .Include(a => a.Cliente).ThenInclude(c => c!.Empresa)
            .FirstOrDefaultAsync(a => a.Email.ToLower() == email, ct);

        if (acceso is null || acceso.Cliente is null)
            return Resultado<SesionRespuesta>.Error(
                TipoFallo.NoAutenticado, CredencialesMalas, "CREDENCIALES");

        if (acceso.EstaBloqueado(ahora))
            return Resultado<SesionRespuesta>.Error(TipoFallo.Bloqueado,
                $"Cuenta bloqueada por intentos fallidos. Vuelve a intentar en {_o.MinutosBloqueo} minutos.",
                "BLOQUEADO");

        if (!acceso.Activo || !acceso.Cliente.Activo)
            return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                "La cuenta esta desactivada.", "CUENTA_INACTIVA");

        if (!claves.Verificar(acceso.PasswordHash, peticion.Password ?? string.Empty, out var rehashear))
        {
            acceso.IntentosFallidos++;
            if (acceso.IntentosFallidos >= _o.IntentosAntesDeBloqueo)
            {
                acceso.BloqueadoHasta = ahora.AddMinutes(_o.MinutosBloqueo);
                acceso.IntentosFallidos = 0;
            }
            await bd.SaveChangesAsync(ct);
            validador.Invalidar(accesoId: acceso.Id);

            return Resultado<SesionRespuesta>.Error(
                TipoFallo.NoAutenticado, CredencialesMalas, "CREDENCIALES");
        }

        // Si el taller esta de baja o vencido, el portal tampoco responde.
        var empresa = acceso.Cliente.Empresa;
        if (empresa is null || !empresa.EstaAlDia(Tiempo.HoyLima()))
            return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                "El establecimiento no tiene el servicio disponible en este momento.",
                "ESTABLECIMIENTO_NO_DISPONIBLE");

        if (rehashear) acceso.PasswordHash = claves.Hash(peticion.Password!);

        acceso.IntentosFallidos = 0;
        acceso.BloqueadoHasta = null;
        acceso.UltimoAcceso = ahora;

        var dispositivo = await RegistrarDispositivoAsync(
            peticion.Dispositivo, usuarioId: null, accesoId: acceso.Id, ct);

        var token = tokens.ParaCliente(acceso, acceso.Cliente);
        var refresh = await NuevoRefreshClienteAsync(acceso.Id, dispositivo?.Id, ct);

        await bd.SaveChangesAsync(ct);
        validador.Invalidar(accesoId: acceso.Id);

        return Resultado<SesionRespuesta>.Ok(new SesionRespuesta
        {
            Token = token.Token,
            RefreshToken = refresh,
            ExpiraEn = token.ExpiraEn,
            Audiencia = Audiencias.Cliente,
            Perfil = PerfilDeCliente(acceso)
        });
    }

    // -----------------------------------------------------------------------
    // Refresh rotativo con deteccion de reuso
    // -----------------------------------------------------------------------

    public async Task<Resultado<SesionRespuesta>> RefrescarPersonalAsync(
        RefrescarPeticion peticion, CancellationToken ct = default)
    {
        var huella = claves.Huella(peticion.RefreshToken ?? string.Empty);
        var ahora = Tiempo.Ahora;

        var fila = await bd.UsuarioRefrescos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TokenHash == huella, ct);

        if (fila is null)
            return Resultado<SesionRespuesta>.Error(TipoFallo.NoAutenticado,
                "Sesion no valida. Vuelve a entrar.", "REFRESH_INVALIDO");

        // Reuso de un token ya rotado: se trata como robo y se cae toda la
        // cadena del usuario, no solo este token.
        if (fila.UsadoEn is not null || fila.RevocadoEn is not null)
        {
            await RevocarTodoPersonalAsync(fila.UsuarioId, ahora, ct);
            log.LogWarning("Reuso de refresh token del usuario {UsuarioId}: se revoco todo", fila.UsuarioId);

            return Resultado<SesionRespuesta>.Error(TipoFallo.NoAutenticado,
                "Sesion no valida. Vuelve a entrar.", "REFRESH_REUSADO");
        }

        if (fila.ExpiraEn <= ahora)
            return Resultado<SesionRespuesta>.Error(TipoFallo.NoAutenticado,
                "Sesion expirada. Vuelve a entrar.", "REFRESH_EXPIRADO");

        var usuario = await bd.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Rol)
            .Include(u => u.Empresa).ThenInclude(e => e!.Plan)
            .Include(u => u.Sede)
            .FirstOrDefaultAsync(u => u.Id == fila.UsuarioId, ct);

        if (usuario is null || !usuario.Activo)
            return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                "La cuenta esta desactivada.", "CUENTA_INACTIVA");

        if (usuario.RolId != Roles.SuperAdmin &&
            (usuario.Empresa is null || !usuario.Empresa.EstaAlDia(Tiempo.HoyLima())))
            return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                "El servicio no esta disponible en este momento.", "SERVICIO_VENCIDO");

        fila.UsadoEn = ahora;
        var token = tokens.ParaPersonal(usuario);
        var nuevo = await NuevoRefreshPersonalAsync(usuario.Id, fila.DispositivoId, ct, fila);

        await bd.SaveChangesAsync(ct);

        return Resultado<SesionRespuesta>.Ok(new SesionRespuesta
        {
            Token = token.Token,
            RefreshToken = nuevo,
            ExpiraEn = token.ExpiraEn,
            Audiencia = Audiencias.Erp,
            Perfil = await ArmarPerfilPersonalAsync(usuario, ct)
        });
    }

    public async Task<Resultado<SesionRespuesta>> RefrescarClienteAsync(
        RefrescarPeticion peticion, CancellationToken ct = default)
    {
        var huella = claves.Huella(peticion.RefreshToken ?? string.Empty);
        var ahora = Tiempo.Ahora;

        var fila = await bd.ClienteRefrescos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.TokenHash == huella, ct);

        if (fila is null)
            return Resultado<SesionRespuesta>.Error(TipoFallo.NoAutenticado,
                "Sesion no valida. Vuelve a entrar.", "REFRESH_INVALIDO");

        if (fila.UsadoEn is not null || fila.RevocadoEn is not null)
        {
            await RevocarTodoClienteAsync(fila.AccesoId, ahora, ct);
            log.LogWarning("Reuso de refresh token del acceso {AccesoId}: se revoco todo", fila.AccesoId);

            return Resultado<SesionRespuesta>.Error(TipoFallo.NoAutenticado,
                "Sesion no valida. Vuelve a entrar.", "REFRESH_REUSADO");
        }

        if (fila.ExpiraEn <= ahora)
            return Resultado<SesionRespuesta>.Error(TipoFallo.NoAutenticado,
                "Sesion expirada. Vuelve a entrar.", "REFRESH_EXPIRADO");

        var acceso = await bd.ClienteAccesos
            .IgnoreQueryFilters()
            .Include(a => a.Cliente).ThenInclude(c => c!.Empresa)
            .FirstOrDefaultAsync(a => a.Id == fila.AccesoId, ct);

        if (acceso?.Cliente is null || !acceso.Activo || !acceso.Cliente.Activo)
            return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                "La cuenta esta desactivada.", "CUENTA_INACTIVA");

        if (acceso.Cliente.Empresa is null || !acceso.Cliente.Empresa.EstaAlDia(Tiempo.HoyLima()))
            return Resultado<SesionRespuesta>.Error(TipoFallo.Prohibido,
                "El establecimiento no tiene el servicio disponible en este momento.",
                "ESTABLECIMIENTO_NO_DISPONIBLE");

        fila.UsadoEn = ahora;
        var token = tokens.ParaCliente(acceso, acceso.Cliente);
        var nuevo = await NuevoRefreshClienteAsync(acceso.Id, fila.DispositivoId, ct, fila);

        await bd.SaveChangesAsync(ct);

        return Resultado<SesionRespuesta>.Ok(new SesionRespuesta
        {
            Token = token.Token,
            RefreshToken = nuevo,
            ExpiraEn = token.ExpiraEn,
            Audiencia = Audiencias.Cliente,
            Perfil = PerfilDeCliente(acceso)
        });
    }

    // -----------------------------------------------------------------------
    // Cierre de sesion
    // -----------------------------------------------------------------------

    public async Task<Resultado> CerrarSesionAsync(string? refreshToken, CancellationToken ct = default)
    {
        var ahora = Tiempo.Ahora;

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var huella = claves.Huella(refreshToken);

            var dePersonal = await bd.UsuarioRefrescos
                .IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TokenHash == huella, ct);
            if (dePersonal is not null) dePersonal.RevocadoEn ??= ahora;

            var deCliente = await bd.ClienteRefrescos
                .IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TokenHash == huella, ct);
            if (deCliente is not null) deCliente.RevocadoEn ??= ahora;

            await bd.SaveChangesAsync(ct);
        }

        return Resultado.Ok();
    }

    public async Task<Resultado> CerrarTodasLasSesionesAsync(CancellationToken ct = default)
    {
        var ahora = Tiempo.Ahora;

        if (sesion.EsCliente && sesion.AccesoId is not null)
        {
            var acceso = await bd.ClienteAccesos
                .IgnoreQueryFilters().FirstAsync(a => a.Id == sesion.AccesoId, ct);

            acceso.SesionVersion++;
            await RevocarTodoClienteAsync(acceso.Id, ahora, ct);
            validador.Invalidar(accesoId: acceso.Id);
            return Resultado.Ok();
        }

        if (sesion.UsuarioId is null)
            return Resultado.Error(TipoFallo.NoAutenticado, "Sin sesion.", "SESION_INVALIDA");

        var usuario = await bd.Usuarios
            .IgnoreQueryFilters().FirstAsync(u => u.Id == sesion.UsuarioId, ct);

        usuario.SesionVersion++;
        await RevocarTodoPersonalAsync(usuario.Id, ahora, ct);
        validador.Invalidar(usuarioId: usuario.Id);

        return Resultado.Ok();
    }

    // -----------------------------------------------------------------------
    // Perfil y clave
    // -----------------------------------------------------------------------

    public async Task<Resultado<PerfilRespuesta>> PerfilAsync(CancellationToken ct = default)
    {
        if (!sesion.Autenticado)
            return Resultado<PerfilRespuesta>.Error(TipoFallo.NoAutenticado, "Sin sesion.", "SESION_INVALIDA");

        if (sesion.EsCliente)
        {
            var acceso = await bd.ClienteAccesos
                .IgnoreQueryFilters()
                .Include(a => a.Cliente)
                .FirstOrDefaultAsync(a => a.Id == sesion.AccesoId, ct);

            return acceso?.Cliente is null
                ? Resultado<PerfilRespuesta>.Error(TipoFallo.NoEncontrado, "Cuenta no encontrada.", "NO_ENCONTRADO")
                : Resultado<PerfilRespuesta>.Ok(PerfilDeCliente(acceso));
        }

        var usuario = await bd.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Rol)
            .Include(u => u.Empresa).ThenInclude(e => e!.Plan)
            .Include(u => u.Sede)
            .FirstOrDefaultAsync(u => u.Id == sesion.UsuarioId, ct);

        return usuario is null
            ? Resultado<PerfilRespuesta>.Error(TipoFallo.NoEncontrado, "Cuenta no encontrada.", "NO_ENCONTRADO")
            : Resultado<PerfilRespuesta>.Ok(await ArmarPerfilPersonalAsync(usuario, ct));
    }

    public async Task<Resultado> CambiarClaveAsync(CambiarClavePeticion peticion, CancellationToken ct = default)
    {
        if (!sesion.Autenticado)
            return Resultado.Error(TipoFallo.NoAutenticado, "Sin sesion.", "SESION_INVALIDA");

        var ahora = Tiempo.Ahora;

        if (sesion.EsCliente)
        {
            var acceso = await bd.ClienteAccesos
                .IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == sesion.AccesoId, ct);

            if (acceso is null)
                return Resultado.Error(TipoFallo.NoEncontrado, "Cuenta no encontrada.", "NO_ENCONTRADO");

            if (!claves.Verificar(acceso.PasswordHash, peticion.ClaveActual, out _))
                return Resultado.Error(TipoFallo.Validacion, "La contrasena actual no coincide.", "CLAVE_ACTUAL");

            acceso.PasswordHash = claves.Hash(peticion.ClaveNueva);
            acceso.SesionVersion++;                     // cambiar la clave cierra las otras sesiones
            await RevocarTodoClienteAsync(acceso.Id, ahora, ct);
            validador.Invalidar(accesoId: acceso.Id);

            return Resultado.Ok();
        }

        var usuario = await bd.Usuarios
            .IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == sesion.UsuarioId, ct);

        if (usuario is null)
            return Resultado.Error(TipoFallo.NoEncontrado, "Cuenta no encontrada.", "NO_ENCONTRADO");

        if (!claves.Verificar(usuario.PasswordHash, peticion.ClaveActual, out _))
            return Resultado.Error(TipoFallo.Validacion, "La contrasena actual no coincide.", "CLAVE_ACTUAL");

        usuario.PasswordHash = claves.Hash(peticion.ClaveNueva);
        usuario.DebeCambiarClave = false;
        usuario.SesionVersion++;
        await RevocarTodoPersonalAsync(usuario.Id, ahora, ct);
        validador.Invalidar(usuarioId: usuario.Id);

        return Resultado.Ok();
    }

    // -----------------------------------------------------------------------
    // Piezas compartidas
    // -----------------------------------------------------------------------

    private async Task<PerfilRespuesta> ArmarPerfilPersonalAsync(Usuario usuario, CancellationToken ct)
    {
        var perfil = new PerfilRespuesta
        {
            Tipo = "PERSONAL",
            UsuarioId = usuario.Id,
            Nombre = usuario.NombreCompleto,
            Email = usuario.Email,
            RolId = usuario.RolId,
            Rol = usuario.Rol?.Nombre,
            EmpresaId = usuario.EmpresaId,
            Empresa = usuario.Empresa?.NombreComercial,
            SedeId = usuario.SedeId,
            Sede = usuario.Sede?.Nombre,
            DebeCambiarClave = usuario.DebeCambiarClave,
            PlanCodigo = usuario.Empresa?.Plan?.Codigo
        };

        // El SuperAdmin no lleva permisos de empresa: su autorizacion es el rol.
        if (usuario.RolId == Roles.SuperAdmin || usuario.EmpresaId is null) return perfil;

        var empresaId = usuario.EmpresaId.Value;
        perfil.Permisos = [.. (await permisos.PermisosEfectivosAsync(empresaId, usuario.RolId, ct)).Order()];
        perfil.Funciones = [.. (await permisos.FuncionesDelPlanAsync(empresaId, ct)).Order()];

        return perfil;
    }

    private static PerfilRespuesta PerfilDeCliente(ClienteAcceso acceso) => new()
    {
        Tipo = "CLIENTE",
        ClienteId = acceso.ClienteId,
        Nombre = acceso.Cliente?.NombreMostrado ?? string.Empty,
        Email = acceso.Email,
        EmpresaId = acceso.Cliente?.EmpresaId,
        Empresa = acceso.Cliente?.Empresa?.NombreComercial
        // Sin rol, sin permisos y sin plan: el portal del cliente no tiene
        // nada que mostrar de la administracion del taller.
    };

    private async Task<string> NuevoRefreshPersonalAsync(
        int usuarioId, long? dispositivoId, CancellationToken ct, UsuarioRefresh? reemplaza = null)
    {
        var token = claves.NuevoTokenOpaco();
        var fila = new UsuarioRefresh
        {
            UsuarioId = usuarioId,
            TokenHash = claves.Huella(token),
            EmitidoEn = Tiempo.Ahora,
            ExpiraEn = Tiempo.Ahora.AddDays(_o.DiasRefreshPersonal),
            DispositivoId = dispositivoId,
            Ip = sesion.Ip,
            Agente = sesion.Agente
        };

        bd.UsuarioRefrescos.Add(fila);
        await bd.SaveChangesAsync(ct);

        if (reemplaza is not null) reemplaza.ReemplazadoPor = fila.Id;

        return token;
    }

    private async Task<string> NuevoRefreshClienteAsync(
        long accesoId, long? dispositivoId, CancellationToken ct, ClienteRefresh? reemplaza = null)
    {
        var token = claves.NuevoTokenOpaco();
        var fila = new ClienteRefresh
        {
            AccesoId = accesoId,
            TokenHash = claves.Huella(token),
            EmitidoEn = Tiempo.Ahora,
            ExpiraEn = Tiempo.Ahora.AddDays(_o.DiasRefreshCliente),
            DispositivoId = dispositivoId,
            Ip = sesion.Ip,
            Agente = sesion.Agente
        };

        bd.ClienteRefrescos.Add(fila);
        await bd.SaveChangesAsync(ct);

        if (reemplaza is not null) reemplaza.ReemplazadoPor = fila.Id;

        return token;
    }

    private async Task RevocarTodoPersonalAsync(int usuarioId, DateTimeOffset ahora, CancellationToken ct)
    {
        await bd.UsuarioRefrescos
            .IgnoreQueryFilters()
            .Where(r => r.UsuarioId == usuarioId && r.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
    }

    private async Task RevocarTodoClienteAsync(long accesoId, DateTimeOffset ahora, CancellationToken ct)
    {
        await bd.ClienteRefrescos
            .IgnoreQueryFilters()
            .Where(r => r.AccesoId == accesoId && r.RevocadoEn == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevocadoEn, ahora), ct);
    }

    /// <summary>
    /// Alta o actualizacion del dispositivo. El token de push se guarda aqui
    /// para que el envio no tenga que preguntarselo a nadie.
    /// </summary>
    private async Task<Dispositivo?> RegistrarDispositivoAsync(
        DispositivoPeticion? datos, int? usuarioId, long? accesoId, CancellationToken ct)
    {
        if (datos is null || string.IsNullOrWhiteSpace(datos.Identificador)) return null;

        var existente = await bd.Dispositivos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.Identificador == datos.Identificador
                                      && d.UsuarioId == usuarioId
                                      && d.AccesoId == accesoId, ct);

        var proveedor = datos.Plataforma == "IOS" ? "APNS" : "FCM";

        if (existente is not null)
        {
            existente.TokenPush = datos.TokenPush ?? existente.TokenPush;
            existente.ProveedorPush = datos.TokenPush is null ? existente.ProveedorPush : proveedor;
            existente.Modelo = datos.Modelo ?? existente.Modelo;
            existente.VersionApp = datos.VersionApp ?? existente.VersionApp;
            existente.VersionOs = datos.VersionOs ?? existente.VersionOs;
            existente.Activo = true;
            existente.CerradoEn = null;
            existente.UltimoVisto = Tiempo.Ahora;
            return existente;
        }

        var nuevo = new Dispositivo
        {
            UsuarioId = usuarioId,
            AccesoId = accesoId,
            Aplicacion = string.IsNullOrWhiteSpace(datos.Aplicacion) ? "ERP_WEB" : datos.Aplicacion,
            Plataforma = string.IsNullOrWhiteSpace(datos.Plataforma) ? "WEB" : datos.Plataforma,
            TokenPush = datos.TokenPush,
            ProveedorPush = datos.TokenPush is null ? null : proveedor,
            Modelo = datos.Modelo,
            VersionApp = datos.VersionApp,
            VersionOs = datos.VersionOs,
            Identificador = datos.Identificador,
            RegistradoEn = Tiempo.Ahora,
            UltimoVisto = Tiempo.Ahora
        };

        bd.Dispositivos.Add(nuevo);
        await bd.SaveChangesAsync(ct);
        return nuevo;
    }
}
