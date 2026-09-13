// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Headless.EntityFramework;

internal sealed class HeadlessIdentityTenantModel(Type[] entityTypes, string providerName) : IModelFinalizingConvention
{
    internal const string OptInAnnotation = "Headless:Identity:TenantOwned";

    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        var model = (IMutableModel)modelBuilder.Metadata;
        if (model[OptInAnnotation] is not true)
        {
            return;
        }

        var entities = entityTypes.Select(model.FindEntityType).ToArray();
        var configuredTenantLengths = entities
            .OfType<IMutableEntityType>()
            .Select(x => x.FindProperty(x.GetTenantPropertyName() ?? "TenantId"))
            .OfType<IMutableProperty>()
            .Select(_GetConfiguredStringLength)
            .OfType<int>()
            .Distinct()
            .ToArray();
        if (configuredTenantLengths.Length > 1)
        {
            throw new InvalidOperationException(
                "Tenant-owned Identity entities require one consistent tenant length for same-tenant relationships."
            );
        }

        var tenantLength = configuredTenantLengths.SingleOrDefault(DomainConstants.IdMaxLength);
        // Older Identity schemas deliberately ignore passkeys; finalization must not add them back.
        for (var i = 0; i < entities.Length; ++i)
        {
            if (entities[i] is not { } entity)
            {
                if (i != entities.Length - 1)
                {
                    throw new InvalidOperationException(
                        $"Tenant-owned Identity entity '{entityTypes[i].Name}' is missing from the model."
                    );
                }

                continue;
            }

            _ConfigureOwnership(entity, tenantLength);
            foreach (var property in entity.FindPrimaryKey()!.Properties)
            {
                if (property.Name is not ("UserId" or "RoleId"))
                {
                    _BoundStringKey(property);
                }
            }
        }

        var user = entities[0]!;
        var role = entities[1]!;
        var userKey = _AddTenantKey(user);
        var roleKey = _AddTenantKey(role);
        _ScopeNameIndex(user, "NormalizedUserName");
        _ScopeNameIndex(role, "NormalizedName");

        _ReplaceRelationship(entities[2]!, user, userKey, "UserId");
        _ReplaceRelationship(entities[3]!, user, userKey, "UserId");
        _ReplaceRelationship(entities[3]!, role, roleKey, "RoleId");
        _ReplaceRelationship(entities[4]!, user, userKey, "UserId");
        _ReplaceRelationship(entities[5]!, role, roleKey, "RoleId");
        _ReplaceRelationship(entities[6]!, user, userKey, "UserId");
        if (entities[7] is { } passkey)
        {
            _ReplaceRelationship(passkey, user, userKey, "UserId");
        }

        if (string.Equals(providerName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal))
        {
            foreach (var entity in entities.OfType<IMutableEntityType>())
            {
                foreach (var key in entity.GetKeys())
                {
                    // Identity's existing varbinary(1024) passkey PK is retained, including its upstream SQL Server limit.
                    if (entity != entities[7] || !key.IsPrimaryKey())
                    {
                        _ValidateKeyBudget(entity, key.Properties);
                    }
                }

                foreach (var foreignKey in entity.GetForeignKeys())
                {
                    _ValidateKeyBudget(entity, foreignKey.Properties);
                }
            }
        }
    }

    private static void _ConfigureOwnership(IMutableEntityType entity, int tenantLength)
    {
        if (
            entity.BaseType is not null
            || entity.IsOwned()
            || entity.FindPrimaryKey() is null
            || entity[HeadlessTenantPolicyAnnotations.IsOwned] is false
        )
        {
            throw new InvalidOperationException(
                $"Identity entity '{entity.Name}' must be a tenant-owned, keyed hierarchy root."
            );
        }

        var name = entity.GetTenantPropertyName() ?? "TenantId";
        entity.SetAnnotation(HeadlessTenantPolicyAnnotations.IsOwned, value: true);
        entity.SetAnnotation(HeadlessTenantPolicyAnnotations.PropertyName, name);
        var property = entity.FindProperty(name) ?? entity.AddProperty(name, typeof(string));
        if (property.ClrType != typeof(string))
        {
            throw new InvalidOperationException($"Identity tenant property '{entity.Name}.{name}' must be a string.");
        }

        property.IsNullable = false;
        if (property.GetMaxLength() is null)
        {
            property.SetMaxLength(tenantLength);
        }
    }

    private static void _BoundStringKey(IMutableProperty property)
    {
        if (property.ClrType == typeof(string))
        {
            property.SetMaxLength(_GetConfiguredStringLength(property) ?? 128);
        }
    }

    private static int? _GetConfiguredStringLength(IMutableProperty property)
    {
        var length = property.GetMaxLength();
        // A provider's inferred text mapping can become bounded; an explicit text override cannot.
        if (property[RelationalAnnotationNames.ColumnType] is string columnType)
        {
            var open = columnType.IndexOf('(', StringComparison.Ordinal);
            if (open < 0)
            {
                throw new InvalidOperationException(
                    $"Identity key '{property.DeclaringType.Name}.{property.Name}' requires an explicit bounded string column type when overriding its store mapping. Configure a length-qualified varchar/nvarchar mapping or remove the column-type override."
                );
            }

            if (
                !int.TryParse(columnType.AsSpan(open + 1).TrimEnd(')'), CultureInfo.InvariantCulture, out var declared)
                || declared <= 0
            )
            {
                throw new InvalidOperationException(
                    $"Identity string key '{property.DeclaringType.Name}.{property.Name}' requires a bounded column type."
                );
            }
            if (length is { } configured && configured != declared)
            {
                throw new InvalidOperationException(
                    $"Identity string key '{property.DeclaringType.Name}.{property.Name}' has conflicting maximum and column-type lengths."
                );
            }
            return declared;
        }
        return length;
    }

    private static IMutableKey _AddTenantKey(IMutableEntityType entity)
    {
        var primaryKey = entity.FindPrimaryKey()!;
        if (
            primaryKey.Properties.Count != 1
            || !string.Equals(primaryKey.Properties[0].Name, "Id", StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                $"Tenant-owned Identity principal '{entity.Name}' must retain its Id primary key."
            );
        }

        IMutableProperty[] properties =
        [
            entity.FindProperty(entity.GetTenantPropertyName()!)!,
            primaryKey.Properties[0],
        ];
        return entity.FindKey(properties) ?? entity.AddKey(properties);
    }

    private static void _ScopeNameIndex(IMutableEntityType entity, string propertyName)
    {
        var indexes = entity
            .GetIndexes()
            .Where(x =>
                x.Properties.Count == 1 && string.Equals(x.Properties[0].Name, propertyName, StringComparison.Ordinal)
            )
            .ToArray();
        if (indexes.Length != 1 || !indexes[0].IsUnique)
        {
            throw new InvalidOperationException(
                $"Tenant-owned Identity entity '{entity.Name}' requires one unique index on '{propertyName}'."
            );
        }

        indexes[0].SetAnnotation(HeadlessTenantPolicyAnnotations.ScopedIndex, value: true);
    }

    private static void _ReplaceRelationship(
        IMutableEntityType dependent,
        IMutableEntityType principal,
        IMutableKey principalKey,
        string idName
    )
    {
        var candidates = dependent.GetForeignKeys().Where(x => x.PrincipalEntityType == principal).ToArray();
        if (
            candidates.Length != 1
            || candidates[0].Properties.Count != 1
            || !string.Equals(candidates[0].Properties[0].Name, idName, StringComparison.Ordinal)
            || !candidates[0].PrincipalKey.IsPrimaryKey()
        )
        {
            throw new InvalidOperationException(
                $"Identity relationship '{dependent.Name}.{idName}' to '{principal.Name}' is ambiguous or no longer uses the original Identity key. Configure exactly one corresponding relationship before opting into tenant ownership."
            );
        }

        var foreignKey = candidates[0];
        var id = foreignKey.Properties[0];
        if (id.ClrType == typeof(string))
        {
            id.SetMaxLength(_GetConfiguredStringLength(id) ?? _GetConfiguredStringLength(principalKey.Properties[1]));
        }

        if (
            id.GetMaxLength() is { } dependentLength
            && principalKey.Properties[1].GetMaxLength() is { } principalLength
            && dependentLength < principalLength
        )
        {
            throw new InvalidOperationException(
                $"Identity foreign-key property '{dependent.Name}.{idName}' cannot be shorter than its principal Id."
            );
        }

        // Mutating the existing relationship retains both navigation objects, delete behavior, and annotations.
        foreignKey.SetProperties([dependent.FindProperty(dependent.GetTenantPropertyName()!)!, id], principalKey);
        foreignKey.IsRequired = true;
    }

    private static void _ValidateKeyBudget(IMutableEntityType entity, IReadOnlyList<IMutableProperty> properties)
    {
        var bytes = properties.Sum(_GetSqlServerKeyBytes);
        if (bytes > 900)
        {
            throw new InvalidOperationException(
                $"Identity key on '{entity.Name}' ({string.Join(", ", properties.Select(x => x.Name))}) requires {bytes.ToString(CultureInfo.CurrentCulture)} bytes, exceeding SQL Server's 900-byte primary/alternate/foreign-key budget. Configure compatible explicit key lengths before tenant opt-in."
            );
        }
    }

    private static long _GetSqlServerKeyBytes(IMutableProperty property)
    {
        var type = property.GetProviderClrType() ?? property.GetValueConverter()?.ProviderClrType ?? property.ClrType;
        if (type == typeof(string) || type == typeof(byte[]))
        {
            var length = property.GetMaxLength();
            var bytesPerCharacter = type == typeof(string) && property.IsUnicode() != false ? 2 : 1;
            if (property.GetColumnType() is { } columnType)
            {
                var parts = columnType.ToLowerInvariant().Split('(', 2);
                bytesPerCharacter = parts[0].Trim() switch
                {
                    "nvarchar" or "nchar" => 2,
                    "varchar" or "char" or "varbinary" or "binary" => 1,
                    _ => throw new InvalidOperationException(
                        $"Cannot verify SQL Server Identity key mapping '{property.DeclaringType.Name}.{property.Name}' with column type '{columnType}'."
                    ),
                };
                if (parts.Length == 2)
                {
                    length = int.TryParse(parts[1].TrimEnd(')'), CultureInfo.InvariantCulture, out var configured)
                        ? configured
                        : null;
                }
            }

            if (length is not > 0)
            {
                throw new InvalidOperationException(
                    $"SQL Server Identity key property '{property.DeclaringType.Name}.{property.Name}' requires a bounded length."
                );
            }

            return (long)length.Value * bytesPerCharacter;
        }

        if (type.IsEnum)
        {
            type = Enum.GetUnderlyingType(type);
        }

        return Type.GetTypeCode(type) switch
        {
            TypeCode.Boolean or TypeCode.Byte => 1,
            TypeCode.Int16 => 2,
            TypeCode.Int32 or TypeCode.Single => 4,
            TypeCode.Int64 or TypeCode.Double or TypeCode.DateTime => 8,
            TypeCode.Decimal => 17,
            _ when type == typeof(Guid) => 16,
            _ when type == typeof(DateTimeOffset) => 10,
            _ => throw new InvalidOperationException(
                $"Cannot verify SQL Server Identity key type '{type.Name}' for '{property.DeclaringType.Name}.{property.Name}'."
            ),
        };
    }
}
