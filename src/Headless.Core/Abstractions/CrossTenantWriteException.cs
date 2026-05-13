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
[PublicAPI]
public sealed class CrossTenantWriteException : Exception
{
    /// <summary>Stable failure category name used in structured diagnostics and logs.</summary>
    public const string FailureCategoryName = "CrossTenantWrite";

    /// <summary>Initializes a new instance of the <see cref="CrossTenantWriteException"/> class.</summary>
    /// <param name="entityType">The CLR entity type name that failed the guard.</param>
    /// <param name="operation">The neutral operation name (for example <c>Add</c>, <c>Modified</c>, <c>Deleted</c>) that failed the guard.</param>
    public CrossTenantWriteException(string entityType, string operation)
        : base(
            $"Tenant-owned {operation} write for entity type '{entityType}' does not match the current tenant context."
        )
    {
        EntityType = entityType;
        Operation = operation;
    }

    /// <summary>Gets the CLR entity type name that failed the guard.</summary>
    public string EntityType { get; }

    /// <summary>Gets the neutral operation name that failed the guard.</summary>
    public string Operation { get; }
}
