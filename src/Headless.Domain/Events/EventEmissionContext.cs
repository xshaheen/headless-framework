// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Domain;

/// <summary>Represents business lineage supplied by an application or subsystem adapter for subsequently raised facts.</summary>
/// <param name="CorrelationId">The root business correlation identifier, independent of distributed tracing identifiers.</param>
/// <param name="CausationId">The immediate parent message or occurrence identifier, or <see langword="null"/> if none exists.</param>
/// <param name="TenantId">The tenant identifier, or <see langword="null"/> for system scope.</param>
[PublicAPI]
public sealed record EventEmissionContext(string CorrelationId, string? CausationId = null, string? TenantId = null)
{
    /// <summary>Gets the root business correlation identifier.</summary>
    public string CorrelationId { get; } = Argument.IsNotNullOrWhiteSpace(CorrelationId);

    /// <summary>Gets the immediate parent occurrence identifier, or <see langword="null"/> if not specified.</summary>
    public string? CausationId { get; } = CausationId is null ? null : Argument.IsNotNullOrWhiteSpace(CausationId);

    /// <summary>Gets the tenant identifier, or <see langword="null"/> for system scope.</summary>
    public string? TenantId { get; } = TenantId is null ? null : Argument.IsNotNullOrWhiteSpace(TenantId);
}
