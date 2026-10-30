// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// Supplies the client IP address and user agent that audit entries record when the caller leaves them unset.
/// </summary>
/// <remarks>
/// <c>Headless.Api</c> registers one that reads the current request (<c>IRequestContext.WebClient</c>). Without a
/// registration, as in a worker or console host, entries carry no client info unless the caller sets it. Both members
/// return <see langword="null"/> outside a request.
/// </remarks>
public interface IAuditClientInfoProvider
{
    /// <summary>Gets the client IP address of the current request, or <see langword="null"/> when unknown.</summary>
    string? IpAddress { get; }

    /// <summary>Gets the <c>User-Agent</c> of the current request, or <see langword="null"/> when unknown.</summary>
    string? UserAgent { get; }
}
