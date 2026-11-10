// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: reads an object's raw payload, such as a message body or a job's arguments, so the shell can
/// show it after the module's <see cref="IDashboardPayloadFormatter"/> redacts it.
/// </summary>
/// <remarks>
/// <para>
/// The shell asks for a payload only when an operator opens it, never for a list, and passes it through the module's
/// formatter before it leaves the server. Return <see langword="null"/> when the object does not exist or carries no
/// payload. Keep the read to one lookup by id; the shell bounds it with the same timeout as its other calls to modules.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardPayloadSource
{
    /// <summary>Returns the raw payload of <see cref="DashboardPayloadRequest.Target"/>.</summary>
    /// <param name="request">The object and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends or the shell's timeout passes.</param>
    /// <returns>The payload as the module's store holds it, or <see langword="null"/> when there is none.</returns>
    Task<DashboardPayload?> GetPayloadAsync(DashboardPayloadRequest request, CancellationToken cancellationToken);
}

/// <summary>A request for one object's raw payload.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardPayloadRequest
{
    /// <summary>Gets the object whose payload is read.</summary>
    public required ObjectRef Target { get; init; }

    /// <summary>Gets the signed-in user the payload is for.</summary>
    public required ClaimsPrincipal User { get; init; }
}
