// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework;

/// <summary>
/// The audit policy rules shared by automatic change capture and the public policy readers, so what a test reads is
/// what a save captures.
/// </summary>
internal static class HeadlessAuditPolicyResolver
{
    /// <summary>Returns the nearest explicit entity policy on <paramref name="entityType"/> or a base type.</summary>
    public static bool? FindConfiguredIsAudited(IReadOnlyEntityType entityType)
    {
        for (var current = entityType; current is not null; current = current.BaseType)
        {
            if (current.FindAnnotation(HeadlessAuditAnnotations.EntityIsAudited) is { Value: bool isAudited })
            {
                return isAudited;
            }
        }

        return null;
    }

    /// <summary>Returns the root owner the ownership metadata of <paramref name="entityType"/> names.</summary>
    public static IReadOnlyEntityType GetOwnerFromMetadata(IReadOnlyEntityType entityType)
    {
        while (entityType.IsOwned())
        {
            entityType = entityType.FindOwnership()!.PrincipalEntityType;
        }

        return entityType;
    }

    public static AuditPropertyPolicy ResolveProperty(IReadOnlyProperty property, Type clrType, AuditLogOptions options)
    {
        var isSensitive = property.FindAnnotation(HeadlessAuditAnnotations.PropertyIsSensitive) is { Value: true };
        var isExcluded = property.FindAnnotation(HeadlessAuditAnnotations.PropertyIsExcluded) is { Value: true };

        var isCaptured = !(
            (property.PropertyInfo is null && !isSensitive) // Unconfigured shadow and field-only properties.
            || options.DefaultExcludedProperties.Contains(property.Name)
            || isExcluded
            || options.PropertyFilter?.Invoke(clrType, property.Name) == true
        );

        SensitiveDataStrategy? strategy = null;

        if (isSensitive)
        {
            strategy = property.FindAnnotation(HeadlessAuditAnnotations.PropertySensitiveStrategy)
                is { Value: int value }
                ? (SensitiveDataStrategy)value
                : options.SensitiveDataStrategy;
        }

        return new AuditPropertyPolicy
        {
            Property = property,
            IsSensitive = isSensitive,
            SensitiveDataStrategy = strategy,
            IsCaptured = isCaptured,
        };
    }
}
