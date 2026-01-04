// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Framework.Api.Abstractions;

public sealed class HttpRequestContext(
    IHttpContextAccessor accessor,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICurrentLocale currentLocale,
    ICurrentTimeZone currentTimeZone,
    IWebClientInfoProvider webClientInfoProvider,
    IClock clock
) : IRequestContext
{
    private HttpContext HttpContext =>
        accessor.HttpContext ?? throw new InvalidOperationException("User context is not available");

    public ICurrentUser User => currentUser;

    public ICurrentTenant Tenant => currentTenant;

    public ICurrentLocale Locale => currentLocale;

    public ICurrentTimeZone TimeZone => currentTimeZone;

    public IWebClientInfoProvider WebClient => webClientInfoProvider;

    public string TraceIdentifier => HttpContext.TraceIdentifier;

    public string? CorrelationId => HttpContext.GetCorrelationId();

    public string? EndpointName => HttpContext.GetEndpoint()?.DisplayName;

    public bool IsAvailable => accessor.HttpContext is not null;

    public DateTimeOffset DateStarted { get; } = clock.UtcNow;
}
