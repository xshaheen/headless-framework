// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: redacts and formats an object's payload on the server, before it leaves for the browser.
/// </summary>
/// <remarks>
/// <para>
/// Redaction in the browser is not redaction, so every payload the shell sends for one of
/// <see cref="ObjectKinds"/>, in a response or a stream event, passes through this formatter first. It may mask
/// fields, drop headers, or pretty-print a serializer's format. Without a formatter, the shell sends JSON as it is.
/// The formatter runs on request threads and stream connections, so keep it free of I/O.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request record grows instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardPayloadFormatter
{
    /// <summary>Gets the object kinds of this module whose payloads the formatter handles, such as <c>message</c>.</summary>
    IReadOnlyList<string> ObjectKinds { get; }

    /// <summary>Returns the payload as the browser may see it.</summary>
    /// <param name="request">The object, its raw payload, and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request or stream ends.</param>
    /// <returns>The redacted, formatted payload.</returns>
    ValueTask<DashboardPayload> FormatAsync(DashboardPayloadFormatRequest request, CancellationToken cancellationToken);
}

/// <summary>A request to format one object's payload.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardPayloadFormatRequest
{
    /// <summary>Gets the object the payload belongs to.</summary>
    public required ObjectRef Target { get; init; }

    /// <summary>Gets the raw payload, as the module's store holds it.</summary>
    public required DashboardPayload Payload { get; init; }

    /// <summary>Gets the signed-in user the payload is for.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>An object's payload: its body, the body's media type, and its headers.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardPayload
{
    /// <summary>Gets the payload's body as text.</summary>
    public required string Content { get; init; }

    /// <summary>Gets the body's media type, such as <c>application/json</c>.</summary>
    public required string ContentType { get; init; }

    /// <summary>Gets the payload's headers; empty when it has none.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
