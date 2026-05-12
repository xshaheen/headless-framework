// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>
/// Thrown when a tenant write guard detects a tenant-owned write that does not match the current
/// tenant context.
/// </summary>
/// <remarks>
/// This exception is non-transient and must NOT be retried. Catch-all retry policies
/// (for example <c>Policy.Handle&lt;Exception&gt;()</c>) should exclude
/// <see cref="CrossTenantWriteException"/> explicitly — retrying a cross-tenant write would
/// either fail identically or, worse, persist the unsafe write if the ambient tenant context
/// changes between attempts.
/// </remarks>
public sealed class CrossTenantWriteException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="CrossTenantWriteException"/> class.</summary>
    /// <param name="entityType">The CLR entity type name that failed the guard.</param>
    /// <param name="writeState">The EF <c>EntityState</c> string (for example <c>Added</c>, <c>Modified</c>, <c>Deleted</c>) that failed the guard.</param>
    /// <param name="currentTenantAvailable">Whether the current operation had an ambient tenant.</param>
    /// <param name="entityTenantAvailable">Whether the entity exposed tenant ownership.</param>
    /// <param name="tenantMatches">Whether the entity ownership matched the current tenant.</param>
    public CrossTenantWriteException(
        string entityType,
        string writeState,
        bool currentTenantAvailable,
        bool entityTenantAvailable,
        bool tenantMatches
    )
        : base(
            $"Tenant-owned {writeState} write for entity type '{entityType}' does not match the current tenant context."
        )
    {
        EntityType = entityType;
        WriteState = writeState;
        CurrentTenantAvailable = currentTenantAvailable;
        EntityTenantAvailable = entityTenantAvailable;
        TenantMatches = tenantMatches;

        Data[nameof(EntityType)] = EntityType;
        Data[nameof(FailureCategory)] = FailureCategory;
        Data[nameof(WriteState)] = WriteState;
        Data[nameof(CurrentTenantAvailable)] = CurrentTenantAvailable;
        Data[nameof(EntityTenantAvailable)] = EntityTenantAvailable;
        Data[nameof(TenantMatches)] = TenantMatches;
    }

    /// <summary>Gets the CLR entity type name that failed the guard.</summary>
    public string EntityType { get; }

    /// <summary>Gets the stable failure category for structured diagnostics.</summary>
    public string FailureCategory { get; } = "CrossTenantWrite";

    /// <summary>Gets the write state that failed the guard.</summary>
    public string WriteState { get; }

    /// <summary>Gets a value indicating whether the current operation had an ambient tenant.</summary>
    public bool CurrentTenantAvailable { get; }

    /// <summary>Gets a value indicating whether the entity exposed tenant ownership.</summary>
    public bool EntityTenantAvailable { get; }

    /// <summary>Gets a value indicating whether the entity ownership matched the current tenant.</summary>
    public bool TenantMatches { get; }
}
