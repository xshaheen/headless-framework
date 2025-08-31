// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using Framework.Abstractions;
using Framework.Domains;
using Framework.Linq;
using Framework.Orm.EntityFramework.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Framework.Orm.EntityFramework.Contexts;

public sealed class DbContextModelCreatingProcessor(
    ICurrentTenant currentTenant,
    IClock clock,
    DbContextGlobalFiltersStatus filtersStatus
)
{
    public string? CurrentTenantId => currentTenant.Id;

    public void ProcessModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var mutableEntityType in modelBuilder.Model.GetEntityTypes())
        {
            _ConfigureConvention(modelBuilder, mutableEntityType);
            _ConfigureValueConverter(modelBuilder, mutableEntityType);
            _InvokeConfigureQueryFilters(modelBuilder, mutableEntityType);
        }
    }

    #region Convension Model Creating Helpers

    private static void _ConfigureConvention(ModelBuilder builder, IMutableEntityType type)
    {
        if (!type.IsOwned() && type.ClrType.IsAssignableTo<IEntity>())
        {
            builder.Entity(type.ClrType).ConfigureFrameworkConvention();
        }
    }

    private void _ConfigureValueConverter(ModelBuilder builder, IMutableEntityType type)
    {
        if (
            type.BaseType is not null
            || type.IsOwned()
            || type.ClrType.IsDefined(typeof(OwnedAttribute), inherit: true)
        )
        {
            return;
        }

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

        var dateTimeConverter = new NormalizeDateTimeValueConverter(clock);
        var nullableDateTimeConverter = new NullableNormalizeDateTimeValueConverter(clock);

        foreach (var property in properties)
        {
            ValueConverter converter = property.ClrType == dateTimeType ? dateTimeConverter : nullableDateTimeConverter;
            builder.Entity(type.ClrType).Property(property.Name).HasConversion(converter);
        }
    }

    private void _InvokeConfigureQueryFilters(ModelBuilder builder, IMutableEntityType type)
    {
        // Note: this is executed once per db context instance
        _ConfigureQueryFiltersMethod.MakeGenericMethod(type.ClrType).Invoke(this, [builder, type]);
    }

    // Note: DeclaredOnly will not include overrides methods if you will make ConfigureQueryFilters virtual.
    private static readonly MethodInfo _ConfigureQueryFiltersMethod = typeof(DbContextModelCreatingProcessor).GetMethod(
        nameof(_ConfigureQueryFilters),
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
    )!;

    private void _ConfigureQueryFilters<TEntity>(ModelBuilder builder, IMutableEntityType type)
        where TEntity : class
    {
        if (type.BaseType is null)
        {
            var filter = _CreateFilterExpression<TEntity>(builder);

            if (filter is not null)
            {
                builder.Entity<TEntity>().AndHasQueryFilter(filter);
            }
        }
    }

    private Expression<Func<TEntity, bool>>? _CreateFilterExpression<TEntity>(ModelBuilder builder)
        where TEntity : class
    {
        // Note: filters are applied once, so the expressions is cached, and it should depend on a properties and not cached values.

        var entityType = typeof(TEntity);
        var isMultiTenant = entityType.IsAssignableTo<IMultiTenant>();
        var isDeleteAudit = entityType.IsAssignableTo<IDeleteAudit>();
        var isSuspendAudit = entityType.IsAssignableTo<ISuspendAudit>();

        if (!isMultiTenant && !isDeleteAudit && !isSuspendAudit)
        {
            return null;
        }

        var mutableEntityType = builder.Entity(entityType).Metadata;
        Expression<Func<TEntity, bool>>? expression = null;

        if (isMultiTenant)
        {
            var columnName = getColumnName(mutableEntityType, nameof(IMultiTenant.TenantId));
            expression = e =>
                !filtersStatus.IsTenantFilterEnabled || EF.Property<string?>(e, columnName) == CurrentTenantId;
        }

        if (isDeleteAudit)
        {
            var columnName = getColumnName(mutableEntityType, nameof(IDeleteAudit.IsDeleted));
            Expression<Func<TEntity, bool>> filter = e =>
                !filtersStatus.IsDeleteFilterEnabled || !EF.Property<bool>(e, columnName);
            expression = expression?.And(filter) ?? filter;
        }

        if (isSuspendAudit)
        {
            var columnName = getColumnName(mutableEntityType, nameof(ISuspendAudit.IsSuspended));

            Expression<Func<TEntity, bool>> filter = e =>
                !filtersStatus.IsSuspendedFilterEnabled || !EF.Property<bool>(e, columnName);

            expression = expression?.And(filter) ?? filter;
        }

        return expression;

        static string getColumnName(IMutableEntityType type, string name)
        {
            return type.FindProperty(name)?.GetColumnName() ?? name;
        }
    }

    #endregion
}
