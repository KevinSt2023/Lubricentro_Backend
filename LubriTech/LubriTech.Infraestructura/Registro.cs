using LubriTech.Aplicacion.Servicios;
using LubriTech.Infraestructura.Consola;
using LubriTech.Infraestructura.Datos;
using LubriTech.Infraestructura.Taller;
using LubriTech.Infraestructura.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LubriTech.Infraestructura;

public static class Registro
{
    /// <summary>
    /// Todo lo de infraestructura en un solo lugar. La API no sabe de EF ni
    /// de como se hashea una clave: pide los servicios por su interfaz.
    /// </summary>
    public static IServiceCollection AgregarInfraestructura(
        this IServiceCollection servicios, IConfiguration config)
    {
        var cadena = config.GetConnectionString("LubriTech")
            ?? throw new InvalidOperationException(
                "Falta ConnectionStrings:LubriTech en la configuracion.");

        servicios.AddDbContext<LubriTechContexto>(opciones =>
        {
            opciones.UseNpgsql(cadena, npg =>
            {
                npg.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
                // A proposito NO se usa MigrationsAssembly: el esquema lo
                // escribe scripts/*.sql a mano. Ver docs/00-ARQUITECTURA.md.
            });
            opciones.UseSnakeCaseNamingConvention();
        });

        servicios.AddMemoryCache();

        servicios.Configure<OpcionesJwt>(config.GetSection(OpcionesJwt.Seccion));

        servicios.AddSingleton<IClaves, Claves>();
        servicios.AddSingleton<ITokenServicio, TokenServicio>();

        servicios.AddScoped<IPermisoServicio, PermisoServicio>();
        servicios.AddScoped<IValidadorSesion, ValidadorSesion>();
        servicios.AddScoped<IBitacoraServicio, BitacoraServicio>();
        servicios.AddScoped<IAutenticacionServicio, AutenticacionServicio>();

        // Consola del SuperAdmin y configuracion de la empresa (Fase 1).
        servicios.AddScoped<IEmpresaConsolaServicio, EmpresaConsolaServicio>();
        servicios.AddScoped<IPlanConsolaServicio, PlanConsolaServicio>();
        servicios.AddScoped<ISuscripcionConsolaServicio, SuscripcionConsolaServicio>();
        servicios.AddScoped<ITableroSaaSServicio, TableroSaaSServicio>();
        servicios.AddScoped<ISedeServicio, SedeServicio>();

        // Maestros del taller (Fase 2).
        servicios.AddScoped<IPersonalServicio, PersonalServicio>();
        servicios.AddScoped<IClienteServicio, ClienteServicio>();
        servicios.AddScoped<IVehiculoServicio, VehiculoServicio>();
        servicios.AddScoped<IServicioCatalogoServicio, ServicioCatalogoServicio>();
        servicios.AddScoped<IAlmacenServicio, AlmacenServicio>();

        // Ordenes de trabajo (Fase 3).
        servicios.AddScoped<IOrdenServicio, OrdenesServicio>();
        servicios.AddSingleton<IEvidenciaAlmacen, EvidenciaAlmacenLocal>();

        // Cotizaciones y aprobaciones (Fase 4).
        servicios.AddScoped<ICotizacionServicio, CotizacionServicio>();

        servicios.AddScoped<SembradoInicial>();

        return servicios;
    }
}
