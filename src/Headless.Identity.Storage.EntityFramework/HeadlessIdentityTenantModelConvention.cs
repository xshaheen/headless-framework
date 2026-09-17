// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace Headless.EntityFramework;

internal sealed class HeadlessIdentityTenantModelConvention(Type[] entityTypes) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context
    )
    {
        var model = (IMutableModel)modelBuilder.Metadata;

        if (model[HeadlessModelAnnotations.Identity.TenantOwned] is not true)
        {
            return;
        }

        var entities = entityTypes.Select(model.FindEntityType).ToArray();

        _ConfigureEntities(entities, _GetTenantLength(entities));
        _ConfigureRelationships(entities);
    }

    private static int _GetTenantLength(IEnumerable<IMutableEntityType?> entities)
    {
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

        return configuredTenantLengths.SingleOrDefault(DomainConstants.IdMaxLength);
    }

    private void _ConfigureEntities(IMutableEntityType?[] entities, int tenantLength)
    {
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
                    _ConfigureStringKeyLength(property);
                }
            }
        }
    }

    private static void _ConfigureRelationships(IMutableEntityType?[] entities)
    {
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
    }

    private static void _ConfigureOwnership(IMutableEntityType entity, int tenantLength)
    {
        if (
            entity.BaseType is not null
            || entity.IsOwned()
            || entity.FindPrimaryKey() is null
            || entity[HeadlessModelAnnotations.Tenancy.IsOwned] is false
        )
        {
            throw new InvalidOperationException(
                $"Identity entity '{entity.Name}' must be a tenant-owned, keyed hierarchy root."
            );
        }

        var name = entity.GetTenantPropertyName() ?? "TenantId";
        entity.SetAnnotation(HeadlessModelAnnotations.Tenancy.IsOwned, value: true);
        entity.SetAnnotation(HeadlessModelAnnotations.Tenancy.PropertyName, name);
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

    private static void _ConfigureStringKeyLength(IMutableProperty property)
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

        indexes[0].SetAnnotation(HeadlessModelAnnotations.Tenancy.ScopedIndex, value: true);
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
}
