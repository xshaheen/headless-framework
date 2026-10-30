// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Checks;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Reads the automatic audit capture policy back from a built Entity Framework model.</summary>
/// <remarks>
/// The policy resolves the same way automatic capture resolves it, so an application test can assert coverage
/// instead of trusting that every <c>IsAudited()</c> and <c>IsAuditSensitive(...)</c> call was made:
/// <code>
/// var options = services.GetRequiredService&lt;IOptions&lt;AuditLogOptions&gt;&gt;().Value;
/// db.Model.GetAuditPolicies(options).Where(p => !p.IsAudited).Should().BeEmpty();
/// </code>
/// </remarks>
[PublicAPI]
public static class HeadlessAuditModelPolicyExtensions
{
    /// <summary>Returns the audit policy of every entity type in <paramref name="model"/>, owned types included.</summary>
    /// <param name="model">The model, typically <c>DbContext.Model</c>.</param>
    /// <param name="options">The audit options capture runs with.</param>
    /// <returns>One policy per entity type, in model order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<AuditEntityPolicy> GetAuditPolicies(this IReadOnlyModel model, AuditLogOptions options)
    {
        Argument.IsNotNull(model);
        Argument.IsNotNull(options);

        return [.. model.GetEntityTypes().Select(entityType => _GetAuditPolicy(entityType, options))];
    }

    /// <summary>Returns the audit policy of <paramref name="entityType"/>.</summary>
    /// <param name="entityType">The entity type.</param>
    /// <param name="options">The audit options capture runs with.</param>
    /// <returns>Whether the entity type is audited, and how each of its properties is treated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entityType"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    public static AuditEntityPolicy GetAuditPolicy(this IReadOnlyEntityType entityType, AuditLogOptions options)
    {
        Argument.IsNotNull(entityType);
        Argument.IsNotNull(options);

        return _GetAuditPolicy(entityType, options);
    }

    private static AuditEntityPolicy _GetAuditPolicy(IReadOnlyEntityType entityType, AuditLogOptions options)
    {
        var policyEntityType = HeadlessAuditPolicyResolver.GetOwnerFromMetadata(entityType);
        var configured = HeadlessAuditPolicyResolver.FindConfiguredIsAudited(policyEntityType);

        return new AuditEntityPolicy
        {
            EntityType = entityType,
            PolicyEntityType = policyEntityType,
            ConfiguredIsAudited = configured,
            // EntityFilter is a veto, so like capture it runs only for a type the policy would audit.
            IsAudited =
                (configured ?? options.AuditByDefault)
                && options.EntityFilter?.Invoke(policyEntityType.ClrType) != true,
            Properties =
            [
                .. entityType
                    .GetProperties()
                    .Select(property =>
                        HeadlessAuditPolicyResolver.ResolveProperty(property, entityType.ClrType, options)
                    ),
            ],
        };
    }
}
