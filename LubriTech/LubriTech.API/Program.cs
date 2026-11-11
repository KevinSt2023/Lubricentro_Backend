using System.Text;
using FluentValidation;
using LubriTech.API.Seguridad;
using LubriTech.API.TiempoReal;
using LubriTech.Aplicacion.Comun;
using LubriTech.Dominio.Comun;
using LubriTech.Infraestructura;
using LubriTech.Infraestructura.Datos;
using LubriTech.Infraestructura.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var constructor = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Registro (Serilog). Consola en dev, archivo rotativo siempre.
// ---------------------------------------------------------------------------
constructor.Host.UseSerilog((contexto, registro) => registro
    .ReadFrom.Configuration(contexto.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/lubritech-.log",
        rollingInterval: Serilog.RollingInterval.Day,
        retainedFileCountLimit: 14));

// ---------------------------------------------------------------------------
// Infraestructura (EF, seguridad, servicios) y sesion
// ---------------------------------------------------------------------------
constructor.Services.AgregarInfraestructura(constructor.Configuration);

constructor.Services.AddHttpContextAccessor();
constructor.Services.AddScoped<ISesionActual, SesionActual>();

// SINGLETON a proposito: lo lee el filtro global de EF, que se compila una
// sola vez. Ver el comentario en ProveedorEmpresaActual.
constructor.Services.AddSingleton<IProveedorEmpresaActual, ProveedorEmpresaActual>();

constructor.Services.AddValidatorsFromAssemblyContaining<LubriTech.Aplicacion.Comun.Resultado>();

// ---------------------------------------------------------------------------
// Autenticacion: un solo esquema que acepta las DOS audiencias, y cada
// endpoint exige la suya. Asi un token de cliente llega firmado pero
// rebota en el filtro del ERP, y al reves.
// ---------------------------------------------------------------------------
var jwt = constructor.Configuration.GetSection(OpcionesJwt.Seccion).Get<OpcionesJwt>()
          ?? throw new InvalidOperationException("Falta la seccion Jwt en la configuracion.");

if (string.IsNullOrWhiteSpace(jwt.Clave) || jwt.Clave.Length < 32)
    throw new InvalidOperationException("Jwt:Clave tiene que existir y medir 32 caracteres o mas.");

constructor.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opciones =>
    {
        // Sin mapeo de claims: "sub" se queda "sub" y no se convierte en una
        // URI larga. El token viaja en cada peticion; cada byte cuenta.
        opciones.MapInboundClaims = false;

        opciones.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Emisor,
            ValidateAudience = true,
            ValidAudiences = [Audiencias.Erp, Audiencias.Cliente],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Clave)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

        // SignalR no manda cabeceras en el handshake de WebSocket: el token
        // llega por query string y hay que recogerlo a mano.
        opciones.Events = new JwtBearerEvents
        {
            OnMessageReceived = contexto =>
            {
                var token = contexto.Request.Query["access_token"];
                var ruta = contexto.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(token) && ruta.StartsWithSegments("/hub"))
                    contexto.Token = token;

                return Task.CompletedTask;
            }
        };
    });

constructor.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// MVC: validacion y revalidacion de sesion como filtros globales, para que
// no dependan de que alguien se acuerde de ponerlos en cada controlador.
// ---------------------------------------------------------------------------
constructor.Services.AddControllers(opciones =>
{
    opciones.Filters.Add<ValidacionDeSesionFiltro>();
    opciones.Filters.Add<ValidacionDeEntradaFiltro>();
});

constructor.Services.AddSignalR();
constructor.Services.AddOpenApi();

// CORS solo para los origenes del ERP web en dev. Las apps nativas no usan CORS.
var origenesWeb = constructor.Configuration.GetSection("Cors:Origenes").Get<string[]>()
                  ?? ["http://localhost:4500", "http://localhost:4510"];

constructor.Services.AddCors(opciones =>
    opciones.AddPolicy("erp-web", politica => politica
        .WithOrigins(origenesWeb)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = constructor.Build();

// ---------------------------------------------------------------------------
// Tuberia
// ---------------------------------------------------------------------------
// El orden importa: el registro de peticiones va MAS AFUERA que el manejo
// de errores. Al revés, una excepcion que el manejador convierte en 409 o en
// 499 (cliente que corto) atraviesa primero a Serilog y queda escrita como
// 500 que nunca ocurrio. Revisar el log buscando 500 es como se encuentran
// los problemas de verdad, asi que el log tiene que decir la verdad.
app.UseSerilogRequestLogging();
app.UseMiddleware<ManejoDeErroresMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseCors("erp-web");
app.UseAuthentication();
app.UseAuthorization();

// La raiz existe para que abrir http://localhost:9301 en un navegador no
// deje un 404 en el log pareciendo un defecto. La API no sirve paginas.
// GET y HEAD: los monitores de disponibilidad preguntan con HEAD, y un 405
// en el log se lee igual de mal que un error de verdad.
app.MapMethods("/", ["GET", "HEAD"], () => Results.Ok(new
{
    servicio = "LubriTech API",
    salud = "/api/salud",
    documentacion = app.Environment.IsDevelopment() ? "/openapi/v1.json" : null
}));

app.MapControllers();
app.MapHub<ErpHub>("/hub/erp");
app.MapHub<OperacionesHub>("/hub/operaciones");
app.MapHub<ClientesHub>("/hub/clientes");

// ---------------------------------------------------------------------------
// Sembrado de cuentas, solo en desarrollo. Es idempotente y nunca pisa una
// clave existente. El esquema NO se toca aqui: eso lo hace scripts/*.sql.
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    using var alcance = app.Services.CreateScope();
    var sembrado = alcance.ServiceProvider.GetRequiredService<SembradoInicial>();

    try
    {
        await sembrado.EjecutarAsync();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "El sembrado inicial fallo. La API sigue arriba; " +
                      "revisa que la base exista y que scripts/00 y 01 esten aplicados.");
    }
}

app.Run();
