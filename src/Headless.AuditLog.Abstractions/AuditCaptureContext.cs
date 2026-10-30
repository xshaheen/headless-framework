// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>The ambient values <see cref="IAuditChangeCapture"/> stamps on every entry of one save.</summary>
[PublicAPI]
public sealed class AuditCaptureContext
{
    /// <summary>Gets the current user's ID.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the current user's account ID.</summary>
    public string? AccountId { get; init; }

    /// <summary>Gets the current tenant ID.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the correlation ID of the operation.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the client IP address of the current request.</summary>
    public string? IpAddress { get; init; }

    /// <summary>Gets the <c>User-Agent</c> of the current request.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Gets the UTC timestamp every entry of the save carries.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return nameof(AuditCaptureContext);
    }
}
