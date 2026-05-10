// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.EntityFramework.MultiTenancy;

/// <summary>
/// Thrown when the EF tenant write guard detects a tenant-owned write that does not match
/// the current tenant context.
/// </summary>
public sealed class CrossTenantWriteException : Exception
{
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

    /// <summary>Gets the EF write state that failed the guard.</summary>
    public string WriteState { get; }

    /// <summary>Gets a value indicating whether the current operation had an ambient tenant.</summary>
    public bool CurrentTenantAvailable { get; }

    /// <summary>Gets a value indicating whether the entity exposed tenant ownership.</summary>
    public bool EntityTenantAvailable { get; }

    /// <summary>Gets a value indicating whether the entity ownership matched the current tenant.</summary>
    public bool TenantMatches { get; }
}
