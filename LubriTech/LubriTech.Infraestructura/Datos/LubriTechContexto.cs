using System.Linq.Expressions;

using LubriTech.Aplicacion.Comun;
using LubriTech.Dominio.Comun;
using LubriTech.Dominio.Entidades;
using Microsoft.EntityFrameworkCore;

namespace LubriTech.Infraestructura.Datos;

/// <summary>
/// El contexto. El esquema NO lo genera EF: lo escribe a mano
/// scripts/00_nucleo.sql y los que le siguen. Aqui solo se describe el mapeo
/// para que EF lea y escriba sobre lo que ya existe.
/// </summary>
public class LubriTechContexto(
    DbContextOptions<LubriTechContexto> opciones,
    IProveedorEmpresaActual proveedorEmpresa) : DbContext(opciones)
{
    private readonly IProveedorEmpresaActual _proveedorEmpresa = proveedorEmpresa;

    /// <summary>
    /// Lo que lee el filtro global. -1 cuando no hay empresa: no se ve nada.
    /// </summary>
    public int EmpresaFiltro => _proveedorEmpresa.EmpresaId;

    // Plataforma
    public DbSet<Plan> Planes => Set<Plan>();
    public DbSet<Funcion> Funciones => Set<Funcion>();
    public DbSet<PlanFuncion> PlanFunciones => Set<PlanFuncion>();
    public DbSet<PlanRolCupo> PlanRolCupos => Set<PlanRolCupo>();
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Suscripcion> Suscripciones => Set<Suscripcion>();
    public DbSet<Sede> Sedes => Set<Sede>();

    // Seguridad
    public DbSet<Rol> Roles => Set<Rol>();
    public DbSet<Permiso> Permisos => Set<Permiso>();
    public DbSet<RolPermiso> RolPermisos => Set<RolPermiso>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioRefresh> UsuarioRefrescos => Set<UsuarioRefresh>();

    // Clientes
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ClienteAcceso> ClienteAccesos => Set<ClienteAcceso>();
    public DbSet<ClienteRefresh> ClienteRefrescos => Set<ClienteRefresh>();
    public DbSet<ClienteRecuperacion> ClienteRecuperaciones => Set<ClienteRecuperacion>();

    // Maestros del taller (Fase 2)
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<Servicio> Servicios => Set<Servicio>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<ProductoStock> ProductoStocks => Set<ProductoStock>();
    public DbSet<Kardex> Kardex => Set<Kardex>();

    // Ordenes de trabajo (Fase 3)
    public DbSet<OrdenEstado> OrdenEstados => Set<OrdenEstado>();
    public DbSet<Orden> Ordenes => Set<Orden>();
    public DbSet<OrdenHistorial> OrdenHistoriales => Set<OrdenHistorial>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<OrdenChecklist> OrdenChecklists => Set<OrdenChecklist>();
    public DbSet<OrdenServicio> OrdenServicios => Set<OrdenServicio>();
    public DbSet<OrdenConsumo> OrdenConsumos => Set<OrdenConsumo>();
    public DbSet<OrdenEvidencia> OrdenEvidencias => Set<OrdenEvidencia>();

    // Cotizaciones y aprobaciones (Fase 4)
    public DbSet<OrdenHallazgo> OrdenHallazgos => Set<OrdenHallazgo>();
    public DbSet<Cotizacion> Cotizaciones => Set<Cotizacion>();
    public DbSet<CotizacionRenglon> CotizacionRenglones => Set<CotizacionRenglon>();
    public DbSet<CotizacionDecision> CotizacionDecisiones => Set<CotizacionDecision>();

    // Transversales
    public DbSet<Dispositivo> Dispositivos => Set<Dispositivo>();
    public DbSet<Bitacora> Bitacoras => Set<Bitacora>();
    public DbSet<OperacionIdempotente> OperacionesIdempotentes => Set<OperacionIdempotente>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // ------------------------------------------------------------------
        // Plataforma
        // ------------------------------------------------------------------
        mb.Entity<Plan>(e =>
        {
            e.ToTable("plan");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.PrecioMensual).HasColumnType("numeric(10,2)");
        });

        mb.Entity<Funcion>(e =>
        {
            e.ToTable("funcion");
            e.HasKey(x => x.Codigo);
        });

        mb.Entity<PlanFuncion>(e =>
        {
            e.ToTable("plan_funcion");
            e.HasKey(x => new { x.PlanId, x.Funcion });
            e.HasOne(x => x.Plan).WithMany(p => p!.Funciones).HasForeignKey(x => x.PlanId);
        });

        mb.Entity<PlanRolCupo>(e =>
        {
            e.ToTable("plan_rol_cupo");
            e.HasKey(x => new { x.PlanId, x.RolId });
            e.HasOne(x => x.Plan).WithMany(p => p!.Cupos).HasForeignKey(x => x.PlanId);
        });

        mb.Entity<Empresa>(e =>
        {
            e.ToTable("empresa");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Ruc).IsUnique();
            e.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId);
        });

        mb.Entity<Suscripcion>(e =>
        {
            e.ToTable("suscripcion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Monto).HasColumnType("numeric(10,2)");
        });

        mb.Entity<Sede>(e =>
        {
            e.ToTable("sede");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Empresa).WithMany(p => p!.Sedes).HasForeignKey(x => x.EmpresaId);
        });

        // ------------------------------------------------------------------
        // Seguridad
        // ------------------------------------------------------------------
        mb.Entity<Rol>(e =>
        {
            e.ToTable("rol");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        mb.Entity<Permiso>(e =>
        {
            e.ToTable("permiso");
            e.HasKey(x => x.Codigo);
        });

        mb.Entity<RolPermiso>(e =>
        {
            e.ToTable("rol_permiso");
            e.HasKey(x => new { x.RolId, x.PermisoCodigo });
            e.HasOne(x => x.Rol).WithMany(r => r!.Permisos).HasForeignKey(x => x.RolId);
            e.HasOne(x => x.Permiso).WithMany().HasForeignKey(x => x.PermisoCodigo);
        });

        mb.Entity<Usuario>(e =>
        {
            e.ToTable("usuario");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Rol).WithMany().HasForeignKey(x => x.RolId);
            e.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId);
            e.HasOne(x => x.Sede).WithMany().HasForeignKey(x => x.SedeId);
        });

        mb.Entity<UsuarioRefresh>(e =>
        {
            e.ToTable("usuario_refresh");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId);
        });

        // ------------------------------------------------------------------
        // Clientes
        // ------------------------------------------------------------------
        mb.Entity<Cliente>(e =>
        {
            e.ToTable("cliente");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Empresa).WithMany().HasForeignKey(x => x.EmpresaId);
            e.HasOne(x => x.Acceso).WithOne(a => a!.Cliente).HasForeignKey<ClienteAcceso>(a => a.ClienteId);
        });

        mb.Entity<ClienteAcceso>(e =>
        {
            e.ToTable("cliente_acceso");
            e.HasKey(x => x.Id);
        });

        mb.Entity<ClienteRefresh>(e =>
        {
            e.ToTable("cliente_refresh");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.Acceso).WithMany().HasForeignKey(x => x.AccesoId);
        });

        mb.Entity<ClienteRecuperacion>(e =>
        {
            e.ToTable("cliente_recuperacion");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        // ------------------------------------------------------------------
        // Maestros del taller (Fase 2)
        // ------------------------------------------------------------------
        mb.Entity<Vehiculo>(e =>
        {
            e.ToTable("vehiculo");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId);
        });

        mb.Entity<Servicio>(e =>
        {
            e.ToTable("servicio");
            e.HasKey(x => x.Id);
            e.Property(x => x.Precio).HasColumnType("numeric(10,2)");
        });

        mb.Entity<Producto>(e =>
        {
            e.ToTable("producto");
            e.HasKey(x => x.Id);
            e.Property(x => x.PrecioVenta).HasColumnType("numeric(10,2)");
            e.Property(x => x.StockMinimo).HasColumnType("numeric(12,3)");
        });

        mb.Entity<ProductoStock>(e =>
        {
            e.ToTable("producto_stock");
            e.HasKey(x => new { x.ProductoId, x.SedeId });
            e.Property(x => x.Cantidad).HasColumnType("numeric(12,3)");
            e.Property(x => x.CostoPromedio).HasColumnType("numeric(12,4)");
            e.HasOne(x => x.Producto).WithMany(p => p!.Stocks).HasForeignKey(x => x.ProductoId);
            e.HasOne(x => x.Sede).WithMany().HasForeignKey(x => x.SedeId);
        });

        mb.Entity<Kardex>(e =>
        {
            e.ToTable("kardex");
            e.HasKey(x => x.Id);
            e.Property(x => x.Cantidad).HasColumnType("numeric(12,3)");
            e.Property(x => x.CostoUnitario).HasColumnType("numeric(12,4)");
            e.Property(x => x.Saldo).HasColumnType("numeric(12,3)");
            e.HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.ProductoId);
            e.HasOne(x => x.Sede).WithMany().HasForeignKey(x => x.SedeId);
            // El saldo lo pone el trigger de la base: EF no debe mandarlo de vuelta
            // en un UPDATE (nunca hay UPDATE, pero por si acaso) ni confiarlo.
            e.Property(x => x.Saldo).ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
        });

        // ------------------------------------------------------------------
        // Ordenes de trabajo (Fase 3)
        // ------------------------------------------------------------------
        mb.Entity<OrdenEstado>(e =>
        {
            e.ToTable("orden_estado");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Codigo).IsUnique();
        });

        mb.Entity<Orden>(e =>
        {
            e.ToTable("orden");
            e.HasKey(x => x.Id);
            e.Property(x => x.TotalServicios).HasColumnType("numeric(12,2)");
            e.Property(x => x.TotalRepuestos).HasColumnType("numeric(12,2)");
            e.Property(x => x.Total).HasColumnType("numeric(12,2)");
            // El numero lo asigna el trigger de la base: EF lo lee de vuelta.
            e.Property(x => x.Numero).ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            e.HasOne(x => x.Estado).WithMany().HasForeignKey(x => x.EstadoId);
            e.HasOne(x => x.Sede).WithMany().HasForeignKey(x => x.SedeId);
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId);
            e.HasOne(x => x.Vehiculo).WithMany().HasForeignKey(x => x.VehiculoId);
            e.HasOne(x => x.Tecnico).WithMany().HasForeignKey(x => x.TecnicoId);
            e.HasOne(x => x.Asesor).WithMany().HasForeignKey(x => x.AsesorId);
        });

        mb.Entity<OrdenHistorial>(e =>
        {
            e.ToTable("orden_historial");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.EstadoAnterior).WithMany().HasForeignKey(x => x.EstadoAnteriorId);
            e.HasOne(x => x.EstadoNuevo).WithMany().HasForeignKey(x => x.EstadoNuevoId);
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId);
        });

        mb.Entity<ChecklistItem>(e =>
        {
            e.ToTable("checklist_item");
            e.HasKey(x => x.Id);
        });

        mb.Entity<OrdenChecklist>(e =>
        {
            e.ToTable("orden_checklist");
            e.HasKey(x => x.Id);
        });

        mb.Entity<OrdenServicio>(e =>
        {
            e.ToTable("orden_servicio");
            e.HasKey(x => x.Id);
            e.Property(x => x.Precio).HasColumnType("numeric(10,2)");
            e.Property(x => x.Cantidad).HasColumnType("numeric(10,2)");
        });

        mb.Entity<OrdenConsumo>(e =>
        {
            e.ToTable("orden_consumo");
            e.HasKey(x => x.Id);
            e.Property(x => x.Cantidad).HasColumnType("numeric(12,3)");
            e.Property(x => x.CostoUnitario).HasColumnType("numeric(12,4)");
            e.Property(x => x.PrecioVenta).HasColumnType("numeric(10,2)");
            e.HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.ProductoId);
        });

        mb.Entity<OrdenEvidencia>(e =>
        {
            e.ToTable("orden_evidencia");
            e.HasKey(x => x.Id);
        });

        // ------------------------------------------------------------------
        // Cotizaciones y aprobaciones (Fase 4)
        // ------------------------------------------------------------------
        mb.Entity<OrdenHallazgo>(e =>
        {
            e.ToTable("orden_hallazgo");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Autor).WithMany().HasForeignKey(x => x.CreadoPor);
        });

        mb.Entity<Cotizacion>(e =>
        {
            e.ToTable("cotizacion");
            e.HasKey(x => x.Id);
            e.Property(x => x.Total).HasColumnType("numeric(12,2)");
            // La version la asigna el trigger de la base: EF la lee de vuelta.
            e.Property(x => x.Version).ValueGeneratedOnAddOrUpdate()
                .Metadata.SetAfterSaveBehavior(Microsoft.EntityFrameworkCore.Metadata.PropertySaveBehavior.Ignore);
            e.HasOne(x => x.Orden).WithMany().HasForeignKey(x => x.OrdenId);
            e.HasOne(x => x.Autor).WithMany().HasForeignKey(x => x.CreadoPor);
            e.HasOne(x => x.Decision).WithOne().HasForeignKey<CotizacionDecision>(d => d.CotizacionId);
        });

        mb.Entity<CotizacionRenglon>(e =>
        {
            e.ToTable("cotizacion_renglon");
            e.HasKey(x => x.Id);
            e.Property(x => x.Cantidad).HasColumnType("numeric(12,3)");
            e.Property(x => x.PrecioUnitario).HasColumnType("numeric(10,2)");
            // El subtotal es columna generada por la base: EF no lo escribe, lo lee.
            e.Property(x => x.Subtotal).HasColumnType("numeric(12,2)")
                .HasComputedColumnSql("round(cantidad * precio_unitario, 2)", stored: true);
            e.HasOne<Cotizacion>().WithMany(c => c.Renglones).HasForeignKey(x => x.CotizacionId);
        });

        mb.Entity<CotizacionDecision>(e =>
        {
            e.ToTable("cotizacion_decision");
            e.HasKey(x => x.Id);
        });

        // ------------------------------------------------------------------
        // Transversales
        // ------------------------------------------------------------------
        mb.Entity<Dispositivo>(e =>
        {
            e.ToTable("dispositivo");
            e.HasKey(x => x.Id);
        });

        mb.Entity<Bitacora>(e =>
        {
            e.ToTable("bitacora");
            e.HasKey(x => x.Id);
            e.Property(x => x.Detalle).HasColumnType("jsonb");
        });

        mb.Entity<OperacionIdempotente>(e =>
        {
            e.ToTable("operacion_idempotente");
            e.HasKey(x => x.Clave);
            e.Property(x => x.Respuesta).HasColumnType("jsonb");
        });

        AplicarFiltroDeEmpresa(mb);

        base.OnModelCreating(mb);
    }

    /// <summary>
    /// Pone el filtro de empresa en TODA entidad que lleve la marca, por
    /// reflexion. Agregar una entidad nueva con la marca es suficiente: no
    /// hay que acordarse de nada. Si alguien olvida la marca, el que se queda
    /// sin filtro se ve en la prueba de aislamiento, no en produccion.
    /// </summary>
    private void AplicarFiltroDeEmpresa(ModelBuilder mb)
    {
        // El filtro lee EmpresaFiltro de ESTE contexto, que a su vez lo pide
        // al proveedor singleton. Ver IProveedorEmpresaActual.
        var lectorFiltro = Expression.Property(Expression.Constant(this), nameof(EmpresaFiltro));

        foreach (var tipo in mb.Model.GetEntityTypes())
        {
            var clr = tipo.ClrType;
            var obligatoria = typeof(IEntidadEmpresa).IsAssignableFrom(clr);
            var opcional = typeof(IEntidadEmpresaOpcional).IsAssignableFrom(clr);

            if (!obligatoria && !opcional) continue;

            var parametro = Expression.Parameter(clr, "e");
            var propiedad = Expression.Property(parametro, nameof(IEntidadEmpresa.EmpresaId));

            // int  -> e.EmpresaId == EmpresaFiltro
            // int? -> e.EmpresaId == (int?)EmpresaFiltro
            Expression comparacion = opcional
                ? Expression.Equal(propiedad, Expression.Convert(lectorFiltro, typeof(int?)))
                : Expression.Equal(propiedad, lectorFiltro);

            mb.Entity(clr).HasQueryFilter(Expression.Lambda(comparacion, parametro));
        }
    }

    public override int SaveChanges()
    {
        PonerSellosDeTiempo();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        PonerSellosDeTiempo();
        return base.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Sella las fechas desde el servidor, en UTC real. Que el cliente mande
    /// su propia fecha de creacion no es una opcion.
    /// </summary>
    private void PonerSellosDeTiempo()
    {
        var ahora = Tiempo.Ahora;

        foreach (var entrada in ChangeTracker.Entries())
        {
            if (entrada.State == EntityState.Added &&
                entrada.Metadata.FindProperty("CreadoEn") is not null)
            {
                var creado = entrada.Property("CreadoEn");
                var sinPoner = creado.CurrentValue is null
                               || (creado.CurrentValue is DateTimeOffset d && d == default);
                if (sinPoner) creado.CurrentValue = ahora;
            }

            if (entrada.State == EntityState.Modified &&
                entrada.Metadata.FindProperty("ActualizadoEn") is not null)
            {
                entrada.Property("ActualizadoEn").CurrentValue = ahora;
            }
        }
    }
}
