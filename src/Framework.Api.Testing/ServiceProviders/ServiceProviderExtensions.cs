// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Net;
using System.Security.Claims;
using Framework.Kernel.BuildingBlocks.Abstractions;
using Framework.Testing.Helpers;
using Mediator;
using Microsoft.AspNetCore.Http;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceProviderExtensions
{
    #region Get Service

    public static IClock GetClock(this IServiceProvider provider)
    {
        return provider.GetRequiredService<IClock>();
    }

    public static ISender GetSender(this IServiceProvider provider)
    {
        return provider.GetRequiredService<ISender>();
    }

    #endregion

    #region Create Scope

    public static AsyncServiceScope CreateAsyncScope(this IServiceScopeFactory factory, ClaimsPrincipal? principal)
    {
        var scope = factory.CreateAsyncScope();
        scope.SetHttpContext(principal);

        return scope;
    }

    public static void SetHttpContext(this AsyncServiceScope scope, ClaimsPrincipal? principal)
    {
        scope.ServiceProvider.SetHttpContext(principal is null ? null : new DefaultHttpContext { User = principal });
    }

    #endregion

    #region HttpContext

    public static void SetHttpContext(this IServiceProvider provider, DefaultHttpContext? httpContext)
    {
        var accessor = provider.GetRequiredService<IHttpContextAccessor>();

        accessor.HttpContext = httpContext;
    }

    public static (string IpAddress, string UserAgent) SetHttpContext(
        this IServiceProvider provider,
        ClaimsPrincipal? principal,
        string? ipAddress = null,
        string? userAgent = null
    )
    {
        ipAddress ??= TestConstants.F.Internet.Ip();
        userAgent ??= TestConstants.F.Internet.UserAgent();

        var accessor = provider.GetRequiredService<IHttpContextAccessor>();

        var httpContext = new DefaultHttpContext();

        if (principal is not null)
        {
            httpContext.User = principal;
        }

        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
        httpContext.Request.Headers.UserAgent = userAgent;

        accessor.HttpContext = httpContext;

        return (ipAddress, userAgent);
    }

    #endregion
}
