// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Business lineage supplied by an application or subsystem adapter for subsequently raised facts.</summary>
/// <param name="CorrelationId">The root business correlation, independent of Activity trace identifiers.</param>
/// <param name="CausationId">The immediate parent message or occurrence.</param>
/// <param name="TenantId">Tenant identity, or null for system scope.</param>
[PublicAPI]
public sealed record EventEmissionContext(string CorrelationId, string? CausationId = null, string? TenantId = null)
{
    /// <summary>Validated root business correlation.</summary>
    public string CorrelationId { get; } = Argument.IsNotNullOrWhiteSpace(CorrelationId);

    /// <summary>Immediate parent identity.</summary>
    public string? CausationId { get; } = CausationId is null ? null : Argument.IsNotNullOrWhiteSpace(CausationId);

    /// <summary>Tenant identity, or null for system scope.</summary>
    public string? TenantId { get; } = TenantId is null ? null : Argument.IsNotNullOrWhiteSpace(TenantId);
}
