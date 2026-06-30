// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Testing.AspNetCore;

/// <summary>
/// Extensions for setting up a test <see cref="HttpContext"/> on <see cref="IHttpContextAccessor"/>.
/// </summary>
[PublicAPI]
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
    /// may get <see langword="null"/>.
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

    /// <summary>
    /// Overload of <see cref="SetHttpContext(IServiceProvider, ClaimsPrincipal?, IPAddress?, string?)"/>
    /// that accepts the remote IP as a string and parses it via <see cref="System.Net.IPAddress.Parse(string)"/>.
    /// </summary>
    /// <param name="serviceProvider">The scoped service provider to attach to the context.</param>
    /// <param name="principal">The claims principal for the simulated request. Defaults to an empty principal.</param>
    /// <param name="remoteIp">A non-empty string parseable as an IP address.</param>
    /// <param name="userAgent">Optional value written to <c>Request.Headers.UserAgent</c>.</param>
    /// <returns>The created <see cref="HttpContext"/> for further customization.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="remoteIp"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="remoteIp"/> is empty.
    /// </exception>
    public static HttpContext SetHttpContext(
        this IServiceProvider serviceProvider,
        ClaimsPrincipal? principal,
        string remoteIp,
        string? userAgent = null
    )
    {
        Argument.IsNotNullOrEmpty(remoteIp);

        var ip = IPAddress.Parse(remoteIp);
        return serviceProvider.SetHttpContext(principal, ip, userAgent);
    }
}
