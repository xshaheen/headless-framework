// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Microsoft.EntityFrameworkCore.Infrastructure;
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

    /// <summary>
    /// Returns every scalar property capture can read on <paramref name="entityType"/>: its own properties, then the
    /// properties of each single-valued complex property, depth first, each with its dotted path from the entity
    /// (<c>Address.City</c>) and the complex properties that lead to it.
    /// </summary>
    /// <remarks>
    /// Complex collections are skipped: an element has no stable identity to pair an old value with a new one, so a
    /// per-field diff of one would be guesswork.
    /// </remarks>
    public static IEnumerable<AuditablePropertyPath> GetAuditableProperties(IReadOnlyEntityType entityType)
    {
        return _GetAuditableProperties(entityType, prefix: null, chain: []);
    }

    private static IEnumerable<AuditablePropertyPath> _GetAuditableProperties(
        IReadOnlyTypeBase type,
        string? prefix,
        IReadOnlyComplexProperty[] chain
    )
    {
        foreach (var property in type.GetProperties())
        {
            yield return new AuditablePropertyPath(
                property,
                prefix is null ? property.Name : $"{prefix}.{property.Name}",
                chain
            );
        }

        foreach (var complexProperty in type.GetComplexProperties())
        {
            if (complexProperty.IsCollection)
            {
                continue;
            }

            var path = prefix is null ? complexProperty.Name : $"{prefix}.{complexProperty.Name}";

            foreach (
                var nested in _GetAuditableProperties(complexProperty.ComplexType, path, [.. chain, complexProperty])
            )
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Resolves the capture policy of one scalar property. A complex property's policy applies to every member below
    /// it: any excluded ancestor excludes the member, and the nearest sensitive marking, the member's own first, then
    /// each enclosing complex property from the innermost out, decides whether it is sensitive and with which strategy.
    /// Resolving here, not when the policy is configured, keeps the result independent of configuration order.
    /// </summary>
    public static AuditPropertyPolicy ResolveProperty(AuditablePropertyPath path, Type clrType, AuditLogOptions options)
    {
        var property = path.Property;
        var isExcluded = property.FindAnnotation(HeadlessAuditAnnotations.PropertyIsExcluded) is { Value: true };
        IReadOnlyAnnotatable? sensitiveSource = property.FindAnnotation(HeadlessAuditAnnotations.PropertyIsSensitive)
            is { Value: true }
            ? property
            : null;

        for (var i = path.ComplexChain.Length - 1; i >= 0; i--)
        {
            var ancestor = path.ComplexChain[i];
            isExcluded |= ancestor.FindAnnotation(HeadlessAuditAnnotations.PropertyIsExcluded) is { Value: true };

            if (
                sensitiveSource is null
                && ancestor.FindAnnotation(HeadlessAuditAnnotations.PropertyIsSensitive) is { Value: true }
            )
            {
                sensitiveSource = ancestor;
            }
        }

        var isSensitive = sensitiveSource is not null;

        var isCaptured = !(
            (property.PropertyInfo is null && !isSensitive) // Unconfigured shadow and field-only properties.
            || options.DefaultExcludedProperties.Contains(path.Path)
            || isExcluded
            || options.PropertyFilter?.Invoke(clrType, path.Path) == true
        );

        SensitiveDataStrategy? strategy = null;

        if (isSensitive)
        {
            strategy = sensitiveSource!.FindAnnotation(HeadlessAuditAnnotations.PropertySensitiveStrategy)
                is { Value: int value }
                ? (SensitiveDataStrategy)value
                : options.SensitiveDataStrategy;
        }

        return new AuditPropertyPolicy
        {
            Property = property,
            Path = path.Path,
            IsSensitive = isSensitive,
            SensitiveDataStrategy = strategy,
            IsCaptured = isCaptured,
        };
    }
}

/// <summary>A scalar property capture can read, with its dotted path from the entity and the complex properties that lead to it.</summary>
internal readonly record struct AuditablePropertyPath(
    IReadOnlyProperty Property,
    string Path,
    IReadOnlyComplexProperty[] ComplexChain
);
