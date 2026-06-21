// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Provides ambient context about the current request, including identity, tenant, locale, and client metadata.</summary>
/// <remarks>
/// Implementations compose the per-request ambient state that cross-cutting concerns (logging, authorization,
/// multi-tenancy) rely on. The interface is intentionally broad so that a single injection point exposes all
/// request-scoped context rather than requiring multiple service dependencies.
/// </remarks>
public interface IRequestContext
{
    /// <summary>Gets identity information for the authenticated user of the current request.</summary>
    ICurrentUser User { get; }

    /// <summary>Gets tenant identity for the current request.</summary>
    ICurrentTenant Tenant { get; }

    /// <summary>Gets the locale (culture/language) resolved for the current request.</summary>
    ICurrentLocale Locale { get; }

    /// <summary>Gets the time zone resolved for the current request.</summary>
    ICurrentTimeZone TimeZone { get; }

    /// <summary>Gets network and client-identity information derived from the current HTTP request.</summary>
    /// <remarks>All members on <see cref="IWebClientInfoProvider"/> return <see langword="null"/> outside an HTTP scope.</remarks>
    IWebClientInfoProvider WebClient { get; }

    /// <summary>Gets a unique identifier for the current request, suitable for distributed tracing correlation.</summary>
    /// <remarks>Corresponds to <c>HttpContext.TraceIdentifier</c> in ASP.NET Core implementations.</remarks>
    string TraceIdentifier { get; }

    /// <summary>Gets the correlation ID propagated from an upstream caller via a request header, or <see langword="null"/> if absent.</summary>
    string? CorrelationId { get; }

    /// <summary>Gets the display name of the matched endpoint for the current request, or <see langword="null"/> when no endpoint was matched.</summary>
    string? EndpointName { get; }

    /// <summary>Gets a value indicating whether an HTTP context is available for the current execution scope.</summary>
    /// <remarks>
    /// Returns <see langword="false"/> in background workers, hosted services, and other non-HTTP execution
    /// contexts. Always check this before accessing HTTP-specific members if the code may run outside a request.
    /// </remarks>
    bool IsAvailable { get; }

    /// <summary>Gets the UTC timestamp captured at the start of the current request.</summary>
    DateTimeOffset DateStarted { get; }
}
