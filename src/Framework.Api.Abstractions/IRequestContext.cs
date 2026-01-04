// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Abstractions;

public interface IRequestContext
{
    /// <summary>Get user information.</summary>
    ICurrentUser User { get; }

    /// <summary>Get tenant information.</summary>
    ICurrentTenant Tenant { get; }

    /// <summary>Get locale information.</summary>
    ICurrentLocale Locale { get; }

    /// <summary>Get time zone information.</summary>
    ICurrentTimeZone TimeZone { get; }

    /// <summary>Get web client information.</summary>
    IWebClientInfoProvider WebClient { get; }

    /// <summary>Get Unique request identifier.</summary>
    string TraceIdentifier { get; }

    /// <summary>Get request CorrelatedId.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    string? CorrelationId { get; }

    /// <summary>Get EndpointName.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    string? EndpointName { get; }

    /// <summary>Returns <see langword="true"/> if the HttpContext is available, otherwise false.</summary>
    /// <exception cref="InvalidOperationException">HttpContext not available.</exception>
    bool IsAvailable { get; }

    /// <summary>Get DateTimeOffset at the moment of the request.</summary>
    DateTimeOffset DateStarted { get; }
}
