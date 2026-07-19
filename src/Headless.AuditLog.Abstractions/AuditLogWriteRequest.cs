// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.AuditLog;

/// <summary>Describes an explicit audit event to record.</summary>
[PublicAPI]
public sealed class AuditLogWriteRequest
{
    /// <summary>Gets the dot-namespaced action name.</summary>
    /// <exception cref="ArgumentException">The initialized value is empty or consists only of white-space characters.</exception>
    public required string Action
    {
        get;
        init => field = Argument.IsNotNullOrWhiteSpace(value);
    }

    /// <summary>Gets the optional CLR type name of the related entity.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the optional string representation of the related entity's primary key.</summary>
    public string? EntityId { get; init; }

    /// <summary>Gets the optional payload stored in the audit entry's new values.</summary>
    public Dictionary<string, object?>? Data { get; init; }

    /// <summary>Gets whether the audited operation succeeded. The default is <see langword="true"/>.</summary>
    public bool Success { get; init; } = true;

    /// <summary>Gets the optional error code for an unsuccessful operation.</summary>
    public string? ErrorCode { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return nameof(AuditLogWriteRequest);
    }
}
