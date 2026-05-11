// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Headless.EntityFramework.ChangeTrackers;
using Headless.EntityFramework.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework;

[PublicAPI]
public class HeadlessDbContextRuntime(DbContext db, HeadlessDbContextServices services)
{
    private static readonly MethodInfo _ConfigureQueryFiltersMethod = typeof(HeadlessDbContextRuntime).GetMethod(
        nameof(_ConfigureQueryFilters),
        BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly
    )!;

    private static readonly Type _DateTimeType = typeof(DateTime);
    private static readonly Type _NullableDateTimeType = typeof(DateTime?);

    private readonly HeadlessEntityFrameworkNavigationModifiedTracker _navigationModifiedTracker = new();

    public string? TenantId => services.TenantId;

    public void Initialize()
    {
        db.ChangeTracker.Tracked += _navigationModifiedTracker.ChangeTrackerTracked;
        db.ChangeTracker.StateChanged += _navigationModifiedTracker.ChangeTrackerStateChanged;
    }

    public async Task<int> SaveChangesAsync(
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        var result = await services
            .SaveChangesPipeline.SaveChangesAsync(
                db,
                baseSaveChangesAsync,
                acceptAllChangesOnSuccess,
                cancellationToken
            )
            .ConfigureAwait(false);

        _navigationModifiedTracker.RemoveModifiedEntityEntries();

        return result;
    }

    public int SaveChanges(Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess)
    {
        var result = services.SaveChangesPipeline.SaveChanges(db, baseSaveChanges, acceptAllChangesOnSuccess);
        _navigationModifiedTracker.RemoveModifiedEntityEntries();

        return result;
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
