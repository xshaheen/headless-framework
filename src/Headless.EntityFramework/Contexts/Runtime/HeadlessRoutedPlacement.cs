// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// The tenant and placement a tenant-routed context is pinned to for its whole lifetime. Pinned while the context
/// is constructed, because EF builds the model then and the model carries the schema: a pin taken later (for
/// example at first connection open) could pair one tenant's model with another tenant's database.
/// </summary>
internal sealed class HeadlessRoutedPlacement : IDisposable
{
    private readonly Type _contextType;
    private readonly IMemoryCache _modelCache;
    private DbConnection? _expectedConnection;

    private HeadlessRoutedPlacement(
        Type contextType,
        IMemoryCache modelCache,
        string? tenantId,
        TenantDataPlacement? placement,
        string? hostSchema
    )
    {
        _contextType = contextType;
        _modelCache = modelCache;
        HostSchema = hostSchema;
        TenantId = tenantId;
        Placement = placement;
        EffectiveSchema = placement?.Schema ?? hostSchema;
    }

    /// <summary>The pinned canonical tenant id, or <see langword="null"/> for the host placement.</summary>
    public string? TenantId { get; }

    /// <summary>The pinned tenant placement, or <see langword="null"/> for the host placement.</summary>
    public TenantDataPlacement? Placement { get; }

    /// <summary>Whether <see cref="Configure"/> ran for this context, which a derived <c>OnConfiguring</c> can skip.</summary>
    public bool IsConfigured { get; private set; }

    /// <summary>The context's own default schema: the schema its migrations are authored against.</summary>
    public string? HostSchema { get; }

    /// <summary>The schema the model is built for: the placement's schema, else the context's default schema.</summary>
    public string? EffectiveSchema { get; }

    /// <summary>Whether the context runs in a tenant's own schema (as opposed to the host or a database-only placement).</summary>
    public bool IsSchemaPlaced => Placement?.Schema is not null;

    /// <summary>
    /// Pins <paramref name="db"/> when its type is tenant-routed, or returns <see langword="null"/> for an unrouted
    /// context. Without an ambient tenant the context keeps its registration placement. Under a tenant the placement
    /// must already be resolved, because the model is built in the constructor where nothing can be awaited: pinned
    /// in this scope by <c>IDbContextFactory&lt;T&gt;.CreateDbContextAsync</c>, or preloaded for the ambient tenant by a
    /// tenancy entry point through <see cref="TenantDataPlacementPreloader"/>.
    /// </summary>
    public static HeadlessRoutedPlacement? Pin(DbContext db, IServiceProvider services, string? ambientTenantId)
    {
        var contextType = db.GetType();
        var routing = services.GetService<HeadlessTenantDataRouting>();

        if (routing?.IsRouted(contextType) != true)
        {
            return null;
        }

        var defaultSchema =
            ((IHeadlessDbContext)db).DefaultSchema is { } declared && !string.IsNullOrWhiteSpace(declared)
                ? declared
                : null;
        var tenantId = _Normalize(ambientTenantId);

        if (tenantId is null)
        {
            return new HeadlessRoutedPlacement(
                contextType,
                routing.GetModelCache(contextType),
                tenantId: null,
                placement: null,
                defaultSchema
            );
        }

        var placement = _FindResolvedPlacement(services, contextType, tenantId);

        return new HeadlessRoutedPlacement(
            contextType,
            routing.GetModelCache(contextType),
            tenantId,
            placement,
            defaultSchema
        );
    }

    /// <summary>
    /// The placement resolved for <paramref name="tenantId"/> before this constructor ran: the factory's pin for
    /// the scope it created, else the placement the tenancy entry point preloaded for the ambient tenant.
    /// </summary>
    private static TenantDataPlacement _FindResolvedPlacement(
        IServiceProvider services,
        Type contextType,
        string tenantId
    )
    {
        if (
            services.GetService<HeadlessTenantPlacementPin>() is { Placement: { } pinned } pin
            && string.Equals(pin.TenantId, tenantId, StringComparison.Ordinal)
        )
        {
            return pinned;
        }

        if (TenantDataPlacementPreloader.TryGetPreloaded(tenantId, out var preloaded))
        {
            return preloaded ?? throw NoPlacement(tenantId, contextType);
        }

        throw new InvalidOperationException(
            $"The tenant-routed context '{contextType.Name}' was resolved for tenant '{tenantId}' without a "
                + "resolved data placement. Inject it where a Headless tenancy entry point (HTTP tenant resolution, "
                + "the messaging consume pipeline, or Jobs execution) established the tenant, or create it with "
                + $"await IDbContextFactory<{contextType.Name}>.CreateDbContextAsync(), for example after changing "
                + "the ambient tenant yourself."
        );
    }

    /// <summary>The fail-closed refusal for a tenant whose placement source has no entry for it.</summary>
    public static InvalidOperationException NoPlacement(string tenantId, Type contextType) =>
        new(
            $"Tenant '{tenantId}' has no data placement, so the tenant-routed context '{contextType.Name}' cannot be "
                + "created for it. Add the tenant's placement to the configured placement source; routed contexts "
                + "never fall back to the shared database."
        );

    /// <summary>
    /// Returns the pinned tenant id, or throws when the ambient tenant changed since the pin: the context's model
    /// and connection belong to the pinned tenant, so serving another tenant through it would cross stores.
    /// </summary>
    public string? GetTenantId(string? ambientTenantId)
    {
        var ambient = _Normalize(ambientTenantId);

        if (!string.Equals(ambient, TenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The tenant-routed context '{_contextType.Name}' is pinned to "
                    + $"{(TenantId is null ? "the host" : $"tenant '{TenantId}'")}, but the ambient tenant is now "
                    + $"{(ambient is null ? "none" : $"'{ambient}'")}. Create a new context for the new tenant with "
                    + $"IDbContextFactory<{_contextType.Name}>.CreateDbContextAsync()."
            );
        }

        return TenantId;
    }

    /// <summary>
    /// Applies the pinned placement to the context options: tenant model cache key, the enforcement interceptor,
    /// the tenant's connection string, and a per-schema migrations history table.
    /// </summary>
    public void Configure(DbContextOptionsBuilder optionsBuilder)
    {
        IsConfigured = true;

        // Replaced services, the model cache, and the interceptor instance are identical for every routed instance
        // of a context type, so its tenants share one EF internal service provider.
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, HeadlessTenantModelCacheKeyFactory>();
        optionsBuilder.ReplaceService<IMigrationsAssembly, HeadlessTenantMigrationsAssembly>();
        optionsBuilder.UseMemoryCache(_modelCache);
        optionsBuilder.AddInterceptors(HeadlessTenantPlacementInterceptor.Instance);

        if (Placement is null)
        {
            return;
        }

        var relational =
            optionsBuilder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"The tenant-routed context '{_contextType.Name}' must use a relational database provider."
            );

        if (Placement.ConnectionString is not null)
        {
            if (relational.Connection is not null)
            {
                throw new InvalidOperationException(
                    $"The tenant-routed context '{_contextType.Name}' is configured with a DbConnection instance, "
                        + $"so it cannot connect to the database of tenant '{TenantId}'. Configure it with a "
                        + "connection string instead."
                );
            }

            relational = relational.WithConnectionString(Placement.ConnectionString);
        }

        if (Placement.Schema is not null)
        {
            relational = relational.WithMigrationsHistoryTableSchema(Placement.Schema);
        }

        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(relational);
    }

    /// <summary>
    /// Refuses a model built for another schema. It happens when a derived <c>OnConfiguring</c> skips the base
    /// call or replaces the model cache key itself, so tenants would silently share the first model built.
    /// </summary>
    public void VerifyModel(IModel model)
    {
        // Without Configure there is no tenant connection string and no enforcement interceptor, so a database
        // placement would silently use the registration database even though the schema check below passes.
        if (!IsConfigured)
        {
            throw new InvalidOperationException(
                $"The tenant-routed context '{_contextType.Name}' did not apply its tenant data placement. Call "
                    + "base.OnConfiguring(...) from the context's OnConfiguring override."
            );
        }

        var modelSchema = model.FindAnnotation(HeadlessModelAnnotations.Tenancy.PlacementSchema)?.Value as string;

        if (!string.Equals(modelSchema, EffectiveSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The tenant-routed context '{_contextType.Name}' was pinned to schema "
                    + $"'{EffectiveSchema ?? "<default>"}' but EF served a model built for schema "
                    + $"'{modelSchema ?? "<default>"}'. Call base.OnConfiguring(...) from the context and do not "
                    + "replace IModelCacheKeyFactory."
            );
        }
    }

    /// <summary>
    /// Refuses a connection that does not reach the pinned tenant's database, compared by database name and
    /// normalized data source rather than by raw connection string: providers rewrite the string on open (and drop
    /// its password), so a textual comparison would reject a legitimate reopen. Messages never include a connection
    /// string.
    /// </summary>
    public void VerifyConnection(DbConnection connection)
    {
        if (Placement?.ConnectionString is null)
        {
            return;
        }

        // Built once per context: the pinned placement never changes, and EF opens the connection once per query.
        _expectedConnection ??= _CreateExpectedConnection(connection);

        if (!RelationalDatabaseIdentity.IsSameDatabase(_expectedConnection, connection))
        {
            throw new InvalidOperationException(
                $"The tenant-routed context '{_contextType.Name}' is opening a connection that does not reach the "
                    + $"database of tenant '{TenantId}'. Configure the context with a connection string, not a "
                    + "DbConnection or DbDataSource, and do not replace its connection."
            );
        }
    }

    private DbConnection _CreateExpectedConnection(DbConnection connection)
    {
        var expected =
            DbProviderFactories.GetFactory(connection)?.CreateConnection()
            ?? throw new InvalidOperationException(
                $"The tenant-routed context '{_contextType.Name}' uses a provider that cannot create connections."
            );
        expected.ConnectionString = Placement!.ConnectionString;

        return expected;
    }

    /// <summary>Releases the never-opened connection kept for the database comparison.</summary>
    public void Dispose()
    {
        _expectedConnection?.Dispose();
        _expectedConnection = null;
    }

    private static string? _Normalize(string? tenantId) => string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
}
