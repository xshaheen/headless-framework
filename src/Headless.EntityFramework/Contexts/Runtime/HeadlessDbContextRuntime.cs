// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Headless.EntityFramework.ChangeTrackers;
using Headless.EntityFramework.Contexts;
using Headless.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.EntityFramework;

/// <summary>
/// Per-<see cref="DbContext"/> runtime that wires the navigation-change tracker and the tenant stamp, runs
/// framework conventions in <c>OnModelCreating</c>, and forwards <c>SaveChanges</c> to the
/// <see cref="IHeadlessSaveChangesPipeline"/> of the context's current service binding.
/// </summary>
/// <remarks>
/// <para>
/// The runtime separates what a context instance keeps for its whole life (the model, the EF configuration, the
/// change-tracker handlers) from what belongs to one use of it (the DI scope its collaborators come from). That split
/// is what makes the Headless bases poolable: EF snapshots the change-tracker handlers attached in the constructor
/// and restores them on every lease, while the scope is bound per lease and released before the instance returns
/// to the pool.
/// </para>
/// <para>
/// A context is bound to the DI scope that resolved it: explicitly by the Headless registrations, or, for a context
/// registered with plain EF Core, through its per-scope options, whose application provider is the scope that built
/// them. A context created outside any scope (a pooled or stock <c>IDbContextFactory</c>, or <see langword="new"/> with options
/// carrying the root provider) opens a private scope the first time it needs a scoped collaborator and disposes it
/// with the context, so read-only use creates no scope at all. Singletons (the ambient tenant, the guard options) are
/// read from the application provider without opening one.
/// </para>
/// </remarks>
internal sealed class HeadlessDbContextRuntime(DbContext db)
{
    private static readonly MethodInfo _ConfigureQueryFiltersMethod = typeof(HeadlessDbContextRuntime).GetMethod(
        nameof(_ConfigureQueryFilters),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    private static readonly Type _DateTimeType = typeof(DateTime);
    private static readonly Type _NullableDateTimeType = typeof(DateTime?);

    private readonly DbContext _db = db;
    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();

    // Fixed for the instance's life: the options, and so the application provider, never change across leases.
    private IServiceProvider? _applicationServices;

    // Per lease. Cleared by Release before a pooled instance goes back to the pool.
    private IServiceProvider? _boundServices;
    private IServiceScope? _ownedScope;

    // True once a registration or factory bound the context. An adopted or private scope is a fallback that an explicit
    // binding replaces, because something (such as a derived constructor) may read ServiceProvider before binding.
    private bool _explicitlyBound;
    private IHeadlessSaveChangesPipeline? _pipeline;
    private ICurrentTenant? _currentTenant;
    private TenantGuardOptions? _guardOptions;

    /// <summary>
    /// Attaches the change-tracker handlers. Called once from the context constructor, after the runtime field is
    /// assigned: reading <see cref="DbContext.ChangeTracker"/> builds the model, which calls back into the context's
    /// <c>ConfigureConventions</c> and so into this runtime.
    /// </summary>
    public void AttachChangeTrackerHandlers()
    {
        // Attached in the constructor so EF's pool snapshot captures them and SetLease restores them on every lease;
        // handlers attached later would be dropped by the next lease. The tenant stamp is always attached and checks
        // the write-guard option when it fires, because the option is not known until a provider is bound.
        _db.ChangeTracker.Tracked += _navigationModifiedTracker.ChangeTrackerTracked;
        _db.ChangeTracker.StateChanged += _navigationModifiedTracker.ChangeTrackerStateChanged;
        _db.ChangeTracker.Tracking += _OnTracking;
        _db.ChangeTracker.StateChanging += _OnStateChanging;
    }

    /// <summary>The ambient tenant, read on every access so a tenant change inside one lease is observed.</summary>
    public string? TenantId => (_currentTenant ??= _SingletonServices().GetRequiredService<ICurrentTenant>()).Id;

    /// <summary>
    /// The provider scoped collaborators resolve from: the bound scope, or the private scope opened on first use.
    /// </summary>
    public IServiceProvider ServiceProvider => _boundServices ??= _AdoptOptionsScope() ?? _OpenPrivateScope();

    internal bool IsGuardReadsEnabled => _GetGuardOptions().GuardReads;

    private TenantGuardOptions _GetGuardOptions() =>
        _guardOptions ??= _SingletonServices().GetRequiredService<IOptions<TenantGuardOptions>>().Value;

    private IServiceProvider _SingletonServices() => _boundServices ?? _GetApplicationServices();

    private IServiceProvider _GetApplicationServices() =>
        _applicationServices ??=
            _db.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider
            ?? throw new InvalidOperationException(
                $"'{_db.GetType().Name}' has no service provider. Resolve it from DI, create it through "
                    + "IDbContextFactory, or build its options with UseApplicationServiceProvider(...)."
            );

    /// <summary>Binds this lease to the DI scope that resolved the context.</summary>
    public void Bind(IServiceProvider services)
    {
        if (ReferenceEquals(_boundServices, services))
        {
            _explicitlyBound = true;

            return;
        }

        if (_explicitlyBound)
        {
            throw new InvalidOperationException(
                $"'{_db.GetType().Name}' is already bound to a service scope. A context instance serves one scope at a time."
            );
        }

        // Replace a fallback binding: dispose the private scope it opened (an adopted scope is borrowed), and drop the
        // collaborators resolved from it.
        var fallbackScope = _ownedScope;
        _ownedScope = null;
        _pipeline = null;
        _currentTenant = null;
        _guardOptions = null;
        DisposeScope(fallbackScope, _db.GetType());

        _boundServices = services;
        _explicitlyBound = true;
    }

    /// <summary>
    /// <see langword="true"/> while a scope's lease holds this pooled instance. The context's own dispose is then a
    /// no-op, as it is for EF Core's scoped pool lease: a consumer disposing a scope-resolved context early must not
    /// return an instance the scope still holds, or the scope's later dispose would return it a second time, possibly
    /// out from under the request that leased it in between.
    /// </summary>
    public bool IsScopeLeased { get; private set; }

    /// <summary>Binds this pooled instance to the scope whose lease holds it until the scope ends.</summary>
    public void BindScopeLease(IServiceProvider services)
    {
        Bind(services);
        IsScopeLeased = true;
    }

    /// <summary>Ends the scope's hold, so the next dispose returns the instance to the pool.</summary>
    public void EndScopeLease() => IsScopeLeased = false;

    /// <summary>
    /// Binds this lease to <paramref name="scope"/> and takes ownership of it: the scope is disposed with the
    /// context. Used by the factory that creates a scope per context.
    /// </summary>
    public void BindOwnedScope(IServiceScope scope)
    {
        Bind(scope.ServiceProvider);
        _ownedScope = scope;
    }

    /// <summary>
    /// Clears the lease state and returns the scope this context owned, if any, for the caller to dispose after the
    /// base context is disposed. Clearing comes first because disposing a pooled context returns it to the pool,
    /// where another caller may lease and bind it at once.
    /// </summary>
    public IServiceScope? Release()
    {
        // A unit the caller enlisted on this context and never completed must not follow the instance back into the
        // pool, where the next lease would refuse to save on its account.
        DbContextUnitOfWorkBinding.Unbind(_db);

        var ownedScope = _ownedScope;

        _ownedScope = null;
        IsScopeLeased = false;
        _explicitlyBound = false;
        _boundServices = null;
        _pipeline = null;
        _currentTenant = null;
        _guardOptions = null;
        _navigationModifiedTracker.Clear();

        return ownedScope;
    }

    // Per-scope options carry the scope that built them, so a context EF activated in that scope belongs to it. The
    // scope is borrowed, not owned: whoever created it disposes it. Root (singleton) options are never adopted, since
    // scoped collaborators resolved from the root would live for the host's lifetime.
    private IServiceProvider? _AdoptOptionsScope()
    {
        var application = _GetApplicationServices();

        if (application.GetService<HeadlessRootServiceProvider>() is null || IsRootProvider(application))
        {
            return null;
        }

        return application.GetService<IServiceProvider>() ?? application;
    }

    /// <summary>
    /// Whether <paramref name="services"/> is the root provider, compared against the root a singleton captured. An
    /// unknown provider (no Headless services registered) is treated as not the root.
    /// </summary>
    public static bool IsRootProvider(IServiceProvider services)
    {
        var root = services.GetService<HeadlessRootServiceProvider>()?.Services;

        // Resolving IServiceProvider returns the provider's own scope, which normalizes the root ServiceProvider to the
        // root scope a singleton receives. This holds for Microsoft.Extensions.DependencyInjection.
        return root is not null && ReferenceEquals(services.GetService<IServiceProvider>() ?? services, root);
    }

    private IServiceProvider _OpenPrivateScope()
    {
        _ownedScope = _GetApplicationServices().GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        _boundServices = _ownedScope.ServiceProvider;

        return _boundServices;
    }

    private IHeadlessSaveChangesPipeline _GetPipeline() =>
        _pipeline ??= ServiceProvider.GetRequiredService<IHeadlessSaveChangesPipeline>();

    private void _OnTracking(object? sender, EntityTrackingEventArgs e) => _StampTenantOnAdded(e.Entry, e.State);

    private void _OnStateChanging(object? sender, EntityStateChangingEventArgs e) =>
        _StampTenantOnAdded(e.Entry, e.NewState);

    private void _StampTenantOnAdded(EntityEntry entry, EntityState targetState)
    {
        if (
            targetState != EntityState.Added
            || entry.Metadata.IsOwned()
            || !entry.Metadata.IsTenantOwned()
            || !_GetGuardOptions().GuardWrites
            || _SingletonServices().GetRequiredService<ITenantWriteGuardBypass>().IsActive
        )
        {
            return;
        }

        var property = entry.Property(entry.Metadata.GetTenantPropertyName()!);
        var suppliedTenantId = (string?)property.CurrentValue;
        if (!string.IsNullOrWhiteSpace(suppliedTenantId))
        {
            return;
        }

        var tenantId = TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new MissingTenantContextException(
                $"Tenant-owned Added entry '{entry.Metadata.Name}' requires an ambient tenant before tracking."
            );
        }

        // Required alternate keys enter EF's identity map during tracking, before Tracked/StateChanged fire.
        property.CurrentValue = tenantId;
    }

    // Retry classification: CrossTenantWriteException is non-transient. Callers wrapping
    // SaveChanges in retry policies (Polly, EF execution strategies that swallow EF-specific
    // exceptions) MUST exclude CrossTenantWriteException; retrying either fails identically or,
    // worse, persists the unsafe write if the ambient tenant context drifts between attempts.
    public async Task<int> SaveChangesAsync(
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _GetPipeline()
                .SaveChangesAsync(_db, baseSaveChangesAsync, acceptAllChangesOnSuccess, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // Run cleanup on both success and failure paths so a thrown CrossTenantWriteException
            // (or any other pipeline failure) does not leak stale modified-entry tracking state.
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
        }
    }

    public int SaveChanges(Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess)
    {
        try
        {
            return _GetPipeline().SaveChanges(_db, baseSaveChanges, acceptAllChangesOnSuccess);
        }
        finally
        {
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
        }
    }

    /// <summary>
    /// Disposes a scope returned by <see cref="Release"/>. A secondary scope-dispose failure is logged rather than
    /// thrown, so it never masks the primary disposal exception operators need.
    /// </summary>
    public static void DisposeScope(IServiceScope? scope, Type contextType)
    {
        if (scope is null)
        {
            return;
        }

        // The provider is gone once the scope is disposed, so the logger is resolved first.
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(contextType);

        try
        {
            scope.Dispose();
        }
        catch (Exception scopeEx)
        {
            logger?.LogOwnedScopeDisposeFailed(scopeEx);
        }
    }

    /// <inheritdoc cref="DisposeScope"/>
    public static async ValueTask DisposeScopeAsync(IServiceScope? scope, Type contextType)
    {
        if (scope is null)
        {
            return;
        }

        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(contextType);

        try
        {
            // MS DI scopes may hold async-only-disposable scoped services.
            if (scope is IAsyncDisposable asyncScope)
            {
                await asyncScope.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope.Dispose();
            }
        }
        catch (Exception scopeEx)
        {
            logger?.LogOwnedScopeDisposeFailed(scopeEx);
        }
    }

    public void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.AddBuildingBlocksPrimitivesConvertersMappings();
        builder.Conventions.Add(_ => new HeadlessTenantModelConvention(_db));
    }

    public static void ProcessModelCreating(ModelBuilder builder)
    {
        _ConfigureEntityConventions(builder);
        _ConfigureDateTimeValueConverters(builder);
        _ConfigureQueryFiltersForModel(builder);
    }

    private static void _ConfigureEntityConventions(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (!type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
            {
                modelBuilder.Entity(type.ClrType).ConfigureHeadlessConvention();
                // Opt Guid keys into client-side generation: ValueGenerated.Never plus the framework's
                // IGuidGenerator-backed value generator, which EF Core runs at add time. Only applied here, inside
                // the HeadlessDbContext runtime, so it stays a deliberate framework choice — not in the general
                // ConfigureHeadlessConvention bundle, which also runs in plain consumer DbContexts that may want
                // store-generated keys.
                type.ConfigureHeadlessValueGenerated();
            }
        }
    }

    private static void _ConfigureDateTimeValueConverters(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (type.BaseType is null && !type.IsOwned())
            {
                _ConfigureDateTimeValueConverters(modelBuilder, type);
            }
        }
    }

    private static void _ConfigureDateTimeValueConverters(ModelBuilder modelBuilder, IMutableEntityType type)
    {
        var properties = type.GetProperties()
            .Where(property =>
                property.PropertyInfo is { CanWrite: true }
                && (
                    property.PropertyInfo.PropertyType == _DateTimeType
                    || property.PropertyInfo.PropertyType == _NullableDateTimeType
                )
            )
            .ToList();

        if (properties.Count == 0)
        {
            return;
        }

        var dateTimeConverter = new NormalizeDateTimeValueConverter();
        var nullableDateTimeConverter = new NullableNormalizeDateTimeValueConverter();

        foreach (var property in properties)
        {
            ValueConverter converter =
                property.ClrType == _DateTimeType ? dateTimeConverter : nullableDateTimeConverter;
            modelBuilder.Entity(type.ClrType).Property(property.Name).HasConversion(converter);
        }
    }

    private static void _ConfigureQueryFiltersForModel(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (type.BaseType is null && !type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
            {
                _ConfigureQueryFiltersMethod.MakeGenericMethod(type.ClrType).Invoke(null, [modelBuilder]);
            }
        }
    }

    private static void _ConfigureQueryFilters<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var entityBuilder = modelBuilder.Entity<TEntity>();

        if (entityType.IsAssignableTo<IDeleteAudit>())
        {
            var isDeletedName = _GetColumnName(entityBuilder.Metadata, nameof(IDeleteAudit.IsDeleted));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.NotDeletedFilter,
                x => !EF.Property<bool>(x, isDeletedName)
            );
        }

        // No default filter for ISuspendAudit: suspension is a business state that admin views and the commands that
        // lift it must see. An entity type opts in with HasNotSuspendedFilter() when suspended rows should be hidden.
    }

    private static string _GetColumnName(IMutableEntityType type, string name)
    {
        return type.FindProperty(name)?.GetColumnName() ?? name;
    }
}
