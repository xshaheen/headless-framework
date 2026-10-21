// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework;

/// <summary>
/// The tenant policy of one entity type in a built model, resolved once: whether the type is tenant-owned, whether it
/// is an owned type (whose tenant lives on its owner), and the tenant property on its owning root.
/// </summary>
/// <remarks>
/// The save pipeline and the tenant stamp ask these questions for every tracked entry, and answering them walks the
/// hierarchy and ownership chain and reads annotations each time. A built model is immutable, so the answer is cached
/// as a runtime annotation on the entity type.
/// </remarks>
internal sealed class HeadlessTenantPlan(bool isTenantOwned, bool isOwnedType, IProperty? tenantProperty)
{
    private const string _PlanAnnotation = "Headless:Tenant:Plan";
    private const string _ModelAnnotation = "Headless:Tenant:ModelHasTenantOwned";

    public bool IsTenantOwned { get; } = isTenantOwned;

    public bool IsOwnedType { get; } = isOwnedType;

    /// <summary>The tenant property on the owning root; <see langword="null"/> when the type is not tenant-owned.</summary>
    public IProperty? TenantProperty { get; } = tenantProperty;

    public static HeadlessTenantPlan For(IEntityType entityType)
    {
        return entityType.GetOrAddRuntimeAnnotationValue(_PlanAnnotation, static type => _Create(type!), entityType);
    }

    /// <summary>Whether any entity type in <paramref name="model"/> is tenant-owned.</summary>
    public static bool ModelHasTenantOwnedTypes(IModel model)
    {
        return model.GetOrAddRuntimeAnnotationValue(
            _ModelAnnotation,
            static m => m!.GetEntityTypes().Any(static type => For(type).IsTenantOwned),
            model
        );
    }

    private static HeadlessTenantPlan _Create(IEntityType entityType)
    {
        if (!entityType.IsTenantOwned())
        {
            return new(isTenantOwned: false, entityType.IsOwned(), tenantProperty: null);
        }

        var owner = (IEntityType)entityType.GetTenantOwnerEntityType();

        return new(isTenantOwned: true, entityType.IsOwned(), owner.FindProperty(owner.GetTenantPropertyName()!));
    }
}
