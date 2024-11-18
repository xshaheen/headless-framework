// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public interface IRequestContext
{
    /// <summary>Get user information.</summary>
    ICurrentUser User { get; }

    /// <summary>Get tenant information.</summary>
    ICurrentTenant Tenant { get; }

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

public sealed class HttpRequestContext(
    IHttpContextAccessor accessor,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IWebClientInfoProvider webClientInfoProvider,
    IRequestTime requestTime
) : IRequestContext
{
    private HttpContext HttpContext =>
        accessor.HttpContext ?? throw new InvalidOperationException("User context is not available");

    public ICurrentUser User => currentUser;

    public ICurrentTenant Tenant => currentTenant;

    public IWebClientInfoProvider WebClient => webClientInfoProvider;

    public string TraceIdentifier => HttpContext.TraceIdentifier;

    public string? CorrelationId => HttpContext.GetCorrelationId();

    public string? EndpointName => HttpContext.GetEndpoint()?.DisplayName;

    public bool IsAvailable => accessor.HttpContext is not null;

    public DateTimeOffset DateStarted { get; } = requestTime.Now;
}
