// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Turns the standard 404, 409, and 422 problem responses on or off in the API document for the decorated controller
/// or action, overriding the surface and API-wide settings.
/// </summary>
/// <remarks>
/// <para>
/// The innermost setting wins: an action over its controller, a Minimal API endpoint over its route group, and either
/// over the surface (<c>ApiSurfaceOpenApiOptions.StandardProblemResponses</c>) and the API-wide default
/// (<c>HeadlessOpenApiOptions.StandardProblemResponses</c>). Minimal API endpoints and route groups apply it with
/// <c>WithStandardProblemResponses()</c> and <c>WithoutStandardProblemResponses()</c>.
/// </para>
/// <para>
/// It changes documentation only: the endpoint still returns these statuses when a handler or service raises them. The
/// <c>4XX</c> and <c>5XX</c> range responses stay documented either way.
/// </para>
/// </remarks>
/// <param name="enabled">Whether the endpoint documents the standard problem responses. Default is <see langword="true"/>.</param>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class StandardProblemResponsesAttribute(bool enabled = true) : Attribute
{
    /// <summary>Whether the endpoint documents the standard 404, 409, and 422 problem responses.</summary>
    public bool Enabled { get; } = enabled;
}
