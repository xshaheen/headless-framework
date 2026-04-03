// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Extensions for setting up a test <see cref="HttpContext"/> on <see cref="IHttpContextAccessor"/>.
/// </summary>
public static class TestHttpContextExtensions
{
    /// <summary>
    /// Creates a <see cref="DefaultHttpContext"/> wired to the given <paramref name="serviceProvider"/>
    /// and assigns it to the resolved <see cref="IHttpContextAccessor"/>.
    /// </summary>
    /// <remarks>
    /// <paramref name="serviceProvider"/> should be a scoped provider (e.g., from
    /// <see cref="HeadlessTestServer{TProgram}.ExecuteScopeAsync(Func{IServiceProvider, Task})"/>)
    /// so that scoped services resolve correctly from <see cref="HttpContext.RequestServices"/>.
    /// <para/>
    /// <see cref="DefaultHttpContext"/> has a minimal feature collection. Code that reads
    /// <c>IHttpConnectionFeature</c> directly (rather than via <see cref="HttpContext.Connection"/>)
    /// may get <c>null</c>.
    /// </remarks>
    /// <returns>The created <see cref="HttpContext"/> for further customization.</returns>
    public static HttpContext SetHttpContext(
        this IServiceProvider serviceProvider,
        ClaimsPrincipal? principal = null,
        IPAddress? remoteIp = null,
        string? userAgent = null
    )
    {
        var accessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();

        var context = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity()),
        };

        if (remoteIp is not null)
        {
            context.Connection.RemoteIpAddress = remoteIp;
        }

        if (userAgent is not null)
        {
            context.Request.Headers.UserAgent = userAgent;
        }

        accessor.HttpContext = context;

        return context;
    }
}
