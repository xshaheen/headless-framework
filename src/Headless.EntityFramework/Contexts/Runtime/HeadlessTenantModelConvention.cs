// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Headless.EntityFramework.Contexts.Runtime;

internal sealed class HeadlessTenantModelConvention(DbContext db, string providerName) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        var model = (IMutableModel)modelBuilder.Metadata;

        foreach (var root in model.GetEntityTypes().Where(x => x.BaseType is null && !x.IsOwned()).ToArray())
        {
            _ValidateDeclarations(root);
            if (!root.IsTenantOwned())
            {
                continue;
            }

            _ValidateMapping(root);
            var name = root.GetTenantPropertyName()!;
            var property = root.FindProperty(name);
            var clrPropertyType =
                root.ClrType.GetProperty(name)?.PropertyType ?? root.ClrType.GetField(name)?.FieldType;
            if (
                property is not null && property.ClrType != typeof(string)
                || property is null && clrPropertyType is not null && clrPropertyType != typeof(string)
            )
            {
                throw new InvalidOperationException($"Tenant property '{root.Name}.{name}' must be a string.");
            }

            property ??= root.AddProperty(name, typeof(string));
            if (!typeof(IMultiTenant).IsAssignableFrom(root.ClrType))
            {
                property.IsNullable = false;
            }

            if (
                property.GetValueConverter() is not null
                || property.GetProviderClrType() is { } providerType && providerType != typeof(string)
                || property.IsFixedLength() == true
                || property.ValueGenerated != ValueGenerated.Never
            )
            {
                throw new InvalidOperationException(
                    $"Tenant property '{root.Name}.{name}' requires an unconverted, variable-length, non-generated string mapping."
                );
            }

            property.IsConcurrencyToken = true;
            _ConfigureCanonicalEquality(root, property);
            _ConfigureFilter(root, property);
        }

        foreach (var owned in model.GetEntityTypes().Where(x => x.IsOwned()))
        {
            if (owned.FindAnnotation(HeadlessTenantPolicyAnnotations.IsOwned) is not null)
            {
                throw new InvalidOperationException(
                    $"Owned entity '{owned.Name}' inherits tenant policy and cannot declare its own."
                );
            }

            if (!owned.IsTenantOwned())
            {
                continue;
            }

            var owner = owned.GetTenantOwnerEntityType();
            if (
                owned.GetTableName() != owner.GetTableName()
                || owned.GetSchema() != owner.GetSchema()
                || owned.GetMappingFragments(StoreObjectType.Table).Any()
            )
            {
                throw new InvalidOperationException(
                    $"Tenant-owned graph '{owner.Name}' contains separately stored owned entity '{owned.Name}'. Only shared-row and JSON ownership are supported."
                );
            }
        }
    }

    private static void _ValidateDeclarations(IMutableEntityType root)
    {
        foreach (var derived in root.GetDerivedTypes())
        {
            if (
                derived.FindAnnotation(HeadlessTenantPolicyAnnotations.IsOwned) is { Value: bool declared }
                && (
                    declared != root.IsTenantOwned()
                    || declared
                        && !string.Equals(
                            derived[HeadlessTenantPolicyAnnotations.PropertyName] as string,
                            root.GetTenantPropertyName(),
                            StringComparison.Ordinal
                        )
                )
            )
            {
                throw new InvalidOperationException(
                    $"Tenant policy on '{derived.Name}' conflicts with hierarchy root '{root.Name}'. Configure tenant ownership on the root."
                );
            }
        }
    }

    private static void _ValidateMapping(IMutableEntityType root)
    {
        if (
            root.IsKeyless
            || root.HasSharedClrType
            || root.GetTableName() is null
            || root.GetMappingStrategy() is "TPT" or "TPC"
            || root.GetDerivedTypesInclusive().Any(x => x.GetMappingFragments(StoreObjectType.Table).Any())
        )
        {
            throw new InvalidOperationException(
                $"Tenant-owned entity '{root.Name}' requires a keyed, non-shared CLR type mapped to one table using TPH inheritance."
            );
        }

        if (
            root
                .Model.GetEntityTypes()
                .Any(x =>
                    x != root
                    && !x.IsOwned()
                    && x.BaseType is null
                    && x.GetTableName() == root.GetTableName()
                    && x.GetSchema() == root.GetSchema()
                )
        )
        {
            throw new InvalidOperationException(
                $"Tenant-owned entity '{root.Name}' cannot share its table with a separate non-owned root."
            );
        }
    }

    private void _ConfigureFilter(IMutableEntityType entity, IMutableProperty property)
    {
        var parameter = Expression.Parameter(entity.ClrType, "entity");
        var tenant = Expression.Property(Expression.Constant(db), nameof(IHeadlessDbContext.TenantId));
        var value = Expression.Call(
            typeof(EF),
            nameof(EF.Property),
            [typeof(string)],
            parameter,
            Expression.Constant(property.Name)
        );
        Expression predicate = Expression.Equal(value, tenant);
        if (!property.IsNullable)
        {
            predicate = Expression.AndAlso(
                Expression.NotEqual(tenant, Expression.Constant(null, typeof(string))),
                predicate
            );
        }

        entity.SetQueryFilter(HeadlessQueryFilters.MultiTenancyFilter, Expression.Lambda(predicate, parameter));
    }

    private void _ConfigureCanonicalEquality(IMutableEntityType entity, IMutableProperty property)
    {
        // Character padding and lossy encodings can merge distinct canonical IDs even under a binary collation.
        var storeType = property.GetColumnType()?.Split('(', 2)[0].Trim().ToLowerInvariant();
        var supportedStorage = providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => property.IsUnicode() != false
                && storeType is null or "nvarchar",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => storeType is null or "text" or "varchar" or "character varying",
            "Microsoft.EntityFrameworkCore.Sqlite" => storeType is null or "text",
            _ => false,
        };
        if (!supportedStorage)
        {
            throw new InvalidOperationException(
                $"Tenant property '{entity.Name}.{property.Name}' requires lossless, variable-length Unicode storage for provider '{providerName}'."
            );
        }

        var collation = providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => "Latin1_General_100_BIN2",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "C",
            "Microsoft.EntityFrameworkCore.Sqlite" => "BINARY",
            _ => throw new InvalidOperationException(
                $"Tenant ownership has no verified canonical string mapping for provider '{providerName}'."
            ),
        };
        if (
            property.GetCollation() is { } configured
            && !string.Equals(configured, collation, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Tenant property '{entity.Name}.{property.Name}' requires collation '{collation}', but '{configured}' was configured."
            );
        }

        property.SetCollation(collation);
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var column = property.GetColumnName(table)!;
        var quoted =
            providerName == "Microsoft.EntityFrameworkCore.SqlServer"
                ? "[" + column.Replace("]", "]]", StringComparison.Ordinal) + "]"
                : "\"" + column.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        var sql = providerName switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => $"DATALENGTH({quoted}) = DATALENGTH(RTRIM({quoted}))",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => $"right({quoted}, 1) <> ' '",
            _ => $"substr({quoted}, -1) <> ' '",
        };
        var constraintName = $"CK_{table.Name}_{column}_TenantCanonical";
        if (entity.FindCheckConstraint(constraintName) is { } existing)
        {
            if (existing.Sql != sql)
            {
                throw new InvalidOperationException(
                    $"Check constraint '{constraintName}' conflicts with canonical tenant validation."
                );
            }

            return;
        }

        entity.AddCheckConstraint(constraintName, sql);
    }

    internal static string? ValidateTenantId(string? tenantId)
    {
        if (tenantId?.EndsWith(' ') == true)
        {
            throw new InvalidOperationException(
                "Tenant IDs ending in U+0020 are not supported by EF tenant isolation. Canonical IDs must not be trimmed or normalized."
            );
        }

        return tenantId;
    }
}
