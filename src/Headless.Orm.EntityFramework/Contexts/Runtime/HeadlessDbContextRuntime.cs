// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Headless.EntityFramework.ChangeTrackers;
using Headless.EntityFramework.Configurations;
using Headless.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework;

/// <summary>
/// Per-<see cref="DbContext"/> runtime that wires the navigation-change tracker, runs framework
/// conventions in <c>OnModelCreating</c>, and forwards <c>SaveChanges</c> calls to the
/// <see cref="IHeadlessSaveChangesPipeline"/> resolved on <paramref name="services"/>.
/// </summary>
/// <remarks>
/// One instance per active <see cref="DbContext"/>. <see cref="Initialize"/> must be called once after
/// the DbContext is constructed so the navigation-change tracker can attach to
/// <see cref="DbContext.ChangeTracker"/>.
/// </remarks>
[PublicAPI]
public class HeadlessDbContextRuntime(DbContext db, HeadlessDbContextServices services) : IAsyncDisposable
{
    private static readonly MethodInfo _ConfigureQueryFiltersMethod = typeof(HeadlessDbContextRuntime).GetMethod(
        nameof(_ConfigureQueryFilters),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    private static readonly Type _DateTimeType = typeof(DateTime);
    private static readonly Type _NullableDateTimeType = typeof(DateTime?);

    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();
    private bool _initialized;
    private bool _stampTenantHandlerAttached;

    public string? TenantId => services.TenantId;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        db.ChangeTracker.Tracked += _navigationModifiedTracker.ChangeTrackerTracked;
        db.ChangeTracker.StateChanged += _navigationModifiedTracker.ChangeTrackerStateChanged;
        _initialized = true;

        if (services.IsTenantWriteGuardEnabled)
        {
            db.ChangeTracker.Tracked += _StampTenantOnAdded;
            _stampTenantHandlerAttached = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);

        if (!_initialized)
        {
            return ValueTask.CompletedTask;
        }

        db.ChangeTracker.Tracked -= _navigationModifiedTracker.ChangeTrackerTracked;
        db.ChangeTracker.StateChanged -= _navigationModifiedTracker.ChangeTrackerStateChanged;

        if (_stampTenantHandlerAttached)
        {
            db.ChangeTracker.Tracked -= _StampTenantOnAdded;
            _stampTenantHandlerAttached = false;
        }

        _initialized = false;

        return ValueTask.CompletedTask;
    }

    private void _StampTenantOnAdded(object? sender, EntityTrackedEventArgs e)
    {
        if (!services.IsTenantWriteGuardEnabled)
        {
            return;
        }

        if (e.Entry.State != EntityState.Added || e.Entry.Entity is not IMultiTenant entity)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(entity.TenantId))
        {
            return;
        }

        var tenantId = services.TenantId;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        ObjectPropertiesHelper.TrySetProperty(entity, x => x.TenantId, () => tenantId);
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
            return await services
                .SaveChangesPipeline.SaveChangesAsync(
                    db,
                    baseSaveChangesAsync,
                    acceptAllChangesOnSuccess,
                    cancellationToken
                )
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
            return services.SaveChangesPipeline.SaveChanges(db, baseSaveChanges, acceptAllChangesOnSuccess);
        }
        finally
        {
            _navigationModifiedTracker.RemoveModifiedEntityEntries();
        }
    }

    public virtual void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.AddBuildingBlocksPrimitivesConvertersMappings();
    }

    public void ProcessModelCreating(ModelBuilder builder)
    {
        _ConfigureEntityConventions(builder);
        _ConfigureDateTimeValueConverters(builder);
        _ConfigureQueryFiltersForModel(builder, _GetRuntimeContext());
    }

    private IHeadlessDbContext _GetRuntimeContext()
    {
        return db as IHeadlessDbContext
            ?? throw new InvalidOperationException(
                $"{db.GetType().Name} must inherit from a Headless DbContext base type."
            );
    }

    private static void _ConfigureEntityConventions(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (!type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
            {
                modelBuilder.Entity(type.ClrType).ConfigureHeadlessConvention();
            }
        }
    }

    private void _ConfigureDateTimeValueConverters(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (type.BaseType is null && !type.IsOwned())
            {
                _ConfigureDateTimeValueConverters(modelBuilder, type);
            }
        }
    }

    private void _ConfigureDateTimeValueConverters(ModelBuilder modelBuilder, IMutableEntityType type)
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

        var dateTimeConverter = new NormalizeDateTimeValueConverter(services.Clock);
        var nullableDateTimeConverter = new NullableNormalizeDateTimeValueConverter(services.Clock);

        foreach (var property in properties)
        {
            ValueConverter converter =
                property.ClrType == _DateTimeType ? dateTimeConverter : nullableDateTimeConverter;
            modelBuilder.Entity(type.ClrType).Property(property.Name).HasConversion(converter);
        }
    }

    private static void _ConfigureQueryFiltersForModel(ModelBuilder modelBuilder, IHeadlessDbContext runtimeContext)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            if (type.BaseType is null && !type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
            {
                _ConfigureQueryFiltersMethod
                    .MakeGenericMethod(type.ClrType)
                    .Invoke(null, [modelBuilder, runtimeContext]);
            }
        }
    }

    private static void _ConfigureQueryFilters<TEntity>(ModelBuilder modelBuilder, IHeadlessDbContext runtimeContext)
        where TEntity : class
    {
        var entityType = typeof(TEntity);
        var entityBuilder = modelBuilder.Entity<TEntity>();

        if (entityType.IsAssignableTo<IMultiTenant>())
        {
            var tenantIdName = _GetColumnName(entityBuilder.Metadata, nameof(IMultiTenant.TenantId));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.MultiTenancyFilter,
                x => EF.Property<string?>(x, tenantIdName) == runtimeContext.TenantId
            );
        }

        if (entityType.IsAssignableTo<IDeleteAudit>())
        {
            var isDeletedName = _GetColumnName(entityBuilder.Metadata, nameof(IDeleteAudit.IsDeleted));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.NotDeletedFilter,
                x => !EF.Property<bool>(x, isDeletedName)
            );
        }

        if (entityType.IsAssignableTo<ISuspendAudit>())
        {
            var isSuspendedName = _GetColumnName(entityBuilder.Metadata, nameof(ISuspendAudit.IsSuspended));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.NotSuspendedFilter,
                x => !EF.Property<bool>(x, isSuspendedName)
            );
        }
    }

    private static string _GetColumnName(IMutableEntityType type, string name)
    {
        return type.FindProperty(name)?.GetColumnName() ?? name;
    }
}
