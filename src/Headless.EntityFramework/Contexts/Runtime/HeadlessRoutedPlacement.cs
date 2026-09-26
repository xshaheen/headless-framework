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
internal sealed class HeadlessRoutedPlacement
{
    private readonly Type _contextType;
    private readonly IMemoryCache _modelCache;

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

    /// <summary>The context's own default schema: the schema its migrations are authored against.</summary>
    public string? HostSchema { get; }

    /// <summary>The schema the model is built for: the placement's schema, else the context's default schema.</summary>
    public string? EffectiveSchema { get; }

    /// <summary>Whether the context runs in a tenant's own schema (as opposed to the host or a database-only placement).</summary>
    public bool IsSchemaPlaced => Placement?.Schema is not null;

    /// <summary>
    /// Pins <paramref name="db"/> when its type is tenant-routed, or returns <see langword="null"/> for an unrouted
    /// context. Without an ambient tenant the context keeps its registration placement. Under a tenant the placement
    /// must already be pinned in this scope by <c>IDbContextFactory&lt;T&gt;.CreateDbContextAsync</c>, which is the only
    /// path that can resolve it asynchronously before the model is built.
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

        var pin = services.GetService<HeadlessTenantPlacementPin>();

        if (pin?.Placement is null || !string.Equals(pin.TenantId, tenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The tenant-routed context '{contextType.Name}' was resolved for tenant '{tenantId}' without a "
                    + "resolved data placement. Create it with "
                    + $"await IDbContextFactory<{contextType.Name}>.CreateDbContextAsync(), which resolves the "
                    + "tenant's placement before the context is built, instead of injecting it directly."
            );
        }

        return new HeadlessRoutedPlacement(
            contextType,
            routing.GetModelCache(contextType),
            tenantId,
            pin.Placement,
            defaultSchema
        );
    }

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
        // Replaced services and interceptor instances are identical for every routed instance, so every tenant
        // shares one EF internal service provider; only the warning configuration below adds a second one.
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

        // An unopened connection only parses its connection string, so building one per open is cheap.
#pragma warning disable MA0045 // EF's synchronous ConnectionOpening hook calls this too; disposing an unopened connection does no I/O.
        using var expected = _CreateExpectedConnection(connection);
#pragma warning restore MA0045

        if (!RelationalDatabaseIdentity.IsSameDatabase(expected, connection))
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

    private static string? _Normalize(string? tenantId) => string.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
}
