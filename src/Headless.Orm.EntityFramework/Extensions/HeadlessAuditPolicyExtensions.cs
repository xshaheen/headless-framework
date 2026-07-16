// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>Configures automatic audit capture policy on the Entity Framework model.</summary>
[PublicAPI]
public static class HeadlessAuditPolicyExtensions
{
    /// <summary>Explicitly includes an entity type in automatic audit capture.</summary>
    /// <param name="builder">The entity type builder.</param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static EntityTypeBuilder IsAudited(this EntityTypeBuilder builder)
    {
        return builder.HasAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited, value: true);
    }

    /// <summary>Explicitly includes an entity type in automatic audit capture.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static EntityTypeBuilder<TEntity> IsAudited<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.HasAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited, value: true);
        return builder;
    }

    /// <summary>Explicitly excludes an entity type from automatic audit capture.</summary>
    /// <param name="builder">The entity type builder.</param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static EntityTypeBuilder ExcludeFromAudit(this EntityTypeBuilder builder)
    {
        return builder.HasAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited, value: false);
    }

    /// <summary>Explicitly excludes an entity type from automatic audit capture.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static EntityTypeBuilder<TEntity> ExcludeFromAudit<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.HasAnnotation(HeadlessAuditPolicyAnnotations.EntityIsAudited, value: false);
        return builder;
    }

    /// <summary>Excludes a property from captured values and changed fields.</summary>
    /// <param name="builder">The property builder.</param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static PropertyBuilder ExcludeFromAudit(this PropertyBuilder builder)
    {
        return builder.HasAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsExcluded, value: true);
    }

    /// <summary>Excludes a property from captured values and changed fields.</summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="builder">The property builder.</param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static PropertyBuilder<TProperty> ExcludeFromAudit<TProperty>(this PropertyBuilder<TProperty> builder)
    {
        builder.HasAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsExcluded, value: true);
        return builder;
    }

    /// <summary>
    /// Marks a property as sensitive and optionally overrides the global sensitive-data strategy.
    /// </summary>
    /// <param name="builder">The property builder.</param>
    /// <param name="strategy">
    /// The property-specific strategy, or <see langword="null"/> to use
    /// <see cref="AuditLogOptions.SensitiveDataStrategy"/>.
    /// </param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static PropertyBuilder IsAuditSensitive(this PropertyBuilder builder, SensitiveDataStrategy? strategy = null)
    {
        _ConfigureSensitiveProperty(builder, strategy);
        return builder;
    }

    /// <summary>
    /// Marks a property as sensitive and optionally overrides the global sensitive-data strategy.
    /// </summary>
    /// <typeparam name="TProperty">The property type.</typeparam>
    /// <param name="builder">The property builder.</param>
    /// <param name="strategy">
    /// The property-specific strategy, or <see langword="null"/> to use
    /// <see cref="AuditLogOptions.SensitiveDataStrategy"/>.
    /// </param>
    /// <returns>The same builder so additional configuration can be chained.</returns>
    public static PropertyBuilder<TProperty> IsAuditSensitive<TProperty>(
        this PropertyBuilder<TProperty> builder,
        SensitiveDataStrategy? strategy = null
    )
    {
        _ConfigureSensitiveProperty(builder, strategy);
        return builder;
    }

    private static void _ConfigureSensitiveProperty(PropertyBuilder builder, SensitiveDataStrategy? strategy)
    {
        builder.HasAnnotation(HeadlessAuditPolicyAnnotations.PropertyIsSensitive, value: true);

        if (strategy is null)
        {
            builder.Metadata.RemoveAnnotation(HeadlessAuditPolicyAnnotations.PropertySensitiveStrategy);
            return;
        }

        builder.HasAnnotation(HeadlessAuditPolicyAnnotations.PropertySensitiveStrategy, (int)strategy.Value);
    }
}
