// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Domain;
using Headless.EntityFramework.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Headless.EntityFramework.Contexts;

public partial class HeadlessEntityModelProcessor
{
    public virtual void ProcessModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var type in modelBuilder.Model.GetEntityTypes())
        {
            ProcessEntityType(modelBuilder, type);
        }
    }

    protected virtual void ProcessEntityType(ModelBuilder modelBuilder, IMutableEntityType type)
    {
        if (!type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
        {
            ConfigureHeadlessConvention(modelBuilder, type);
        }

        if (type.BaseType is null && !type.IsOwned())
        {
            ConfigureValueConverters(modelBuilder, type);
        }

        if (type.BaseType is null && !type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
        {
            InvokeConfigureQueryFilters(modelBuilder, type);
        }
    }

    protected virtual void ConfigureHeadlessConvention(ModelBuilder modelBuilder, IMutableEntityType type)
    {
        modelBuilder.Entity(type.ClrType).ConfigureHeadlessConvention();
    }

    protected virtual void ConfigureValueConverters(ModelBuilder modelBuilder, IMutableEntityType type)
    {
        var dateTimeType = typeof(DateTime);
        var nullableDateTimeType = typeof(DateTime?);

        var properties = type.GetProperties()
            .Where(property =>
                property.PropertyInfo is { CanWrite: true }
                && (
                    property.PropertyInfo.PropertyType == dateTimeType
                    || property.PropertyInfo.PropertyType == nullableDateTimeType
                )
            )
            .ToList();

        if (properties.Count == 0)
        {
            return;
        }

        var dateTimeConverter = new NormalizeDateTimeValueConverter(_clock);
        var nullableDateTimeConverter = new NullableNormalizeDateTimeValueConverter(_clock);

        foreach (var property in properties)
        {
            ValueConverter converter = property.ClrType == dateTimeType ? dateTimeConverter : nullableDateTimeConverter;
            modelBuilder.Entity(type.ClrType).Property(property.Name).HasConversion(converter);
        }
    }

    protected virtual void InvokeConfigureQueryFilters(ModelBuilder modelBuilder, IMutableEntityType type)
    {
        _ConfigureQueryFiltersMethod.MakeGenericMethod(type.ClrType).Invoke(this, [modelBuilder, type]);
    }

    protected virtual void ConfigureQueryFilters<TEntity>(ModelBuilder modelBuilder, IMutableEntityType type)
        where TEntity : class
    {
        // Filters are applied once and cached by EF. Depend on context properties, not cached values.
        var entityType = typeof(TEntity);
        var entityBuilder = modelBuilder.Entity<TEntity>();

        if (entityType.IsAssignableTo<IMultiTenant>())
        {
            var tenantIdName = GetColumnName(entityBuilder.Metadata, nameof(IMultiTenant.TenantId));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.MultiTenancyFilter,
                x => EF.Property<string?>(x, tenantIdName) == TenantId
            );
        }

        if (entityType.IsAssignableTo<IDeleteAudit>())
        {
            var isDeletedName = GetColumnName(entityBuilder.Metadata, nameof(IDeleteAudit.IsDeleted));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.NotDeletedFilter,
                x => !EF.Property<bool>(x, isDeletedName)
            );
        }

        if (entityType.IsAssignableTo<ISuspendAudit>())
        {
            var isSuspendedName = GetColumnName(entityBuilder.Metadata, nameof(ISuspendAudit.IsSuspended));

            entityBuilder.HasQueryFilter(
                HeadlessQueryFilters.NotSuspendedFilter,
                x => !EF.Property<bool>(x, isSuspendedName)
            );
        }
    }

    protected virtual string GetColumnName(IMutableEntityType type, string name)
    {
        return type.FindProperty(name)?.GetColumnName() ?? name;
    }

    private static readonly MethodInfo _ConfigureQueryFiltersMethod = typeof(HeadlessEntityModelProcessor).GetMethod(
        nameof(ConfigureQueryFilters),
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
    )!;
}
