// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.Metadata;

namespace Headless.EntityFramework;

/// <summary>
/// The automatic audit capture policy of one entity type: whether its changes are captured and how each property is
/// treated, resolved from the Entity Framework model and the <see cref="Headless.AuditLog.AuditLogOptions"/> a capture
/// runs with.
/// </summary>
/// <remarks>
/// Read it with <c>model.GetAuditPolicies(options)</c> or <c>entityType.GetAuditPolicy(options)</c>, for example
/// in an application test that asserts every entity is audited and every personal-data property is sensitive.
/// </remarks>
[PublicAPI]
public sealed class AuditEntityPolicy
{
    /// <summary>Gets the entity type this policy describes.</summary>
    public required IReadOnlyEntityType EntityType { get; init; }

    /// <summary>
    /// Gets the entity type whose configuration decides <see cref="IsAudited"/>: the entity type itself, or for an
    /// owned type the root owner its ownership metadata names.
    /// </summary>
    /// <remarks>
    /// At save time an owned entry takes the policy of its runtime owner, so an owned type declared on a base entity
    /// follows each derived owner's own override, which this metadata view cannot see.
    /// </remarks>
    public required IReadOnlyEntityType PolicyEntityType { get; init; }

    /// <summary>
    /// Gets the explicit model configuration: <see langword="true"/> for <c>IsAudited()</c>, <see langword="false"/>
    /// for <c>ExcludeFromAudit()</c>, on <see cref="PolicyEntityType"/> or its nearest configured base type, or
    /// <see langword="null"/> when neither is configured and <see cref="Headless.AuditLog.AuditLogOptions.AuditByDefault"/>
    /// decides.
    /// </summary>
    public required bool? ConfiguredIsAudited { get; init; }

    /// <summary>
    /// Gets whether automatic capture records this entity type's changes: the configured value, else
    /// <see cref="Headless.AuditLog.AuditLogOptions.AuditByDefault"/>, unless
    /// <see cref="Headless.AuditLog.AuditLogOptions.EntityFilter"/> excludes the type.
    /// </summary>
    public required bool IsAudited { get; init; }

    /// <summary>Gets the policy of every property of <see cref="EntityType"/>, in model order.</summary>
    public required IReadOnlyList<AuditPropertyPolicy> Properties { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return EntityType.DisplayName();
    }
}

/// <summary>How automatic audit capture treats one property.</summary>
[PublicAPI]
public sealed class AuditPropertyPolicy
{
    /// <summary>Gets the property this policy describes.</summary>
    public required IReadOnlyProperty Property { get; init; }

    /// <summary>Gets whether the property is marked sensitive with <c>IsAuditSensitive(...)</c>.</summary>
    public required bool IsSensitive { get; init; }

    /// <summary>
    /// Gets the strategy applied to a sensitive property's values: the one passed to <c>IsAuditSensitive(...)</c>,
    /// else <see cref="Headless.AuditLog.AuditLogOptions.SensitiveDataStrategy"/>. <see langword="null"/> when the
    /// property is not sensitive.
    /// </summary>
    public required Headless.AuditLog.SensitiveDataStrategy? SensitiveDataStrategy { get; init; }

    /// <summary>
    /// Gets whether capture reads the property at all. <see langword="false"/> for a property excluded with
    /// <c>ExcludeFromAudit()</c>, listed in <see cref="Headless.AuditLog.AuditLogOptions.DefaultExcludedProperties"/>,
    /// vetoed by <see cref="Headless.AuditLog.AuditLogOptions.PropertyFilter"/>, or a shadow or field-only property not
    /// marked sensitive. A captured sensitive property whose strategy is
    /// <see cref="Headless.AuditLog.SensitiveDataStrategy.Exclude"/> still records no value.
    /// </summary>
    public required bool IsCaptured { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return Property.Name;
    }
}
