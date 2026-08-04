// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Middlewares;
using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api;

[PublicAPI]
public static class SetupCanonicalUrl
{
    /// <summary>
    /// Adds <see cref="RedirectToCanonicalUrlRule"/> to the pipeline through the rewrite middleware, reading
    /// <c>AppendTrailingSlash</c> and <c>LowercaseUrls</c> from the registered <c>RouteOptions</c>. Non-canonical
    /// GET requests are answered with a 301 Permanent Redirect to their canonical URL.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="application"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Place this after <c>UseRouting()</c>. The rule reads the <see cref="Filters.NoTrailingSlashAttribute"/> and
    /// <see cref="Filters.NoLowercaseQueryStringAttribute"/> opt-outs from endpoint metadata, so requests that have
    /// not been routed yet are left untouched rather than redirected past an opt-out.
    /// </remarks>
    public static IApplicationBuilder UseRedirectToCanonicalUrl(this IApplicationBuilder application)
    {
        Argument.IsNotNull(application);

        var routeOptions = application.ApplicationServices.GetRequiredService<IOptions<RouteOptions>>();

        return application.UseRewriter(new RewriteOptions().Add(new RedirectToCanonicalUrlRule(routeOptions)));
    }

    /// <summary>
    /// Adds <see cref="RedirectToCanonicalUrlRule"/> to the pipeline through the rewrite middleware using explicit
    /// canonicalization settings instead of the registered <c>RouteOptions</c>.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <param name="appendTrailingSlash">When <see langword="true"/>, a trailing slash is appended; when <see langword="false"/>, it is stripped.</param>
    /// <param name="lowercaseUrls">When <see langword="true"/>, the path and query string are lower-cased.</param>
    /// <returns>The same application builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="application"/> is <see langword="null"/>.</exception>
    /// <remarks>Same ordering requirement as <see cref="UseRedirectToCanonicalUrl(IApplicationBuilder)"/>.</remarks>
    public static IApplicationBuilder UseRedirectToCanonicalUrl(
        this IApplicationBuilder application,
        bool appendTrailingSlash,
        bool lowercaseUrls
    )
    {
        Argument.IsNotNull(application);

        var rule = new RedirectToCanonicalUrlRule(appendTrailingSlash, lowercaseUrls);

        return application.UseRewriter(new RewriteOptions().Add(rule));
    }
}
