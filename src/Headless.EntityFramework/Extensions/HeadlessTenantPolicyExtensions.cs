// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Domain;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Declares and reads hierarchy-wide tenant ownership.</summary>
[PublicAPI]
public static class HeadlessTenantPolicyExtensions
{
    /// <summary>Includes a hierarchy in tenant isolation, using a mapped or shadow string property.</summary>
    /// <exception cref="ArgumentException">The property name is empty or whitespace.</exception>
    public static EntityTypeBuilder IsTenantOwned(this EntityTypeBuilder builder, string propertyName = "TenantId")
    {
        Argument.IsNotNullOrWhiteSpace(propertyName);
        builder.HasAnnotation(HeadlessTenantPolicyAnnotations.IsOwned, true);
        return builder.HasAnnotation(HeadlessTenantPolicyAnnotations.PropertyName, propertyName);
    }

    /// <summary>Includes a hierarchy in tenant isolation, using a mapped or shadow string property.</summary>
    /// <exception cref="ArgumentException">The property name is empty or whitespace.</exception>
    public static EntityTypeBuilder<TEntity> IsTenantOwned<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string propertyName = "TenantId"
    )
        where TEntity : class
    {
        IsTenantOwned((EntityTypeBuilder)builder, propertyName);
        return builder;
    }

    /// <summary>Explicitly excludes a root and its hierarchy from tenant isolation.</summary>
    public static EntityTypeBuilder IsNotTenantOwned(this EntityTypeBuilder builder)
    {
        builder.Metadata.RemoveAnnotation(HeadlessTenantPolicyAnnotations.PropertyName);
        return builder.HasAnnotation(HeadlessTenantPolicyAnnotations.IsOwned, false);
    }

    /// <summary>Explicitly excludes a root and its hierarchy from tenant isolation.</summary>
    public static EntityTypeBuilder<TEntity> IsNotTenantOwned<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        IsNotTenantOwned((EntityTypeBuilder)builder);
        return builder;
    }

    /// <summary>Returns whether the root policy includes this entity, including owned dependents.</summary>
    public static bool IsTenantOwned(this IReadOnlyEntityType entityType)
    {
        var root = entityType.GetTenantOwnerEntityType();
        return root[HeadlessTenantPolicyAnnotations.IsOwned] as bool?
            ?? typeof(IMultiTenant).IsAssignableFrom(root.ClrType);
    }

    /// <summary>Returns the tenant property name on the owning root, or null for an excluded entity.</summary>
    public static string? GetTenantPropertyName(this IReadOnlyEntityType entityType)
    {
        var root = entityType.GetTenantOwnerEntityType();
        return root.IsTenantOwned()
            ? root[HeadlessTenantPolicyAnnotations.PropertyName] as string ?? nameof(IMultiTenant.TenantId)
            : null;
    }

    /// <summary>Returns the hierarchy root that supplies this entity's tenant policy.</summary>
    public static IReadOnlyEntityType GetTenantOwnerEntityType(this IReadOnlyEntityType entityType)
    {
        var root = entityType.GetRootType();
        while (root.FindOwnership() is { } ownership)
        {
            root = ownership.PrincipalEntityType.GetRootType();
        }

        return root;
    }
}
