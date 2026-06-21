// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Abstractions;
using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Filters;

/// <summary>
/// Resource filter that rejects requests whose path ends with a trailing slash by returning a 404 Not Found
/// problem-details response without invoking the action. Useful for endpoints that represent file-like
/// resources where trailing slashes are semantically invalid (e.g. <c>/robots.txt</c> vs <c>/robots.txt/</c>).
/// </summary>
/// <remarks>
/// The filter runs at the resource-execution stage, before model binding and action execution. It checks
/// only the raw request path; URL casing is not considered. Apply at the controller or action level via
/// <c>[NoTrailingSlash]</c>. Unlike <see cref="Headless.Api.Middlewares.RedirectToCanonicalUrlRule"/>, this filter does not
/// redirect — it simply terminates the request with a 404.
/// </remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class NoTrailingSlashAttribute : Attribute, IAsyncResourceFilter
{
    private const char _SlashCharacter = '/';

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        Argument.IsNotNull(context);

        var path = context.HttpContext.Request.Path;

        if (!path.HasValue || path.Value[^1] != _SlashCharacter)
        {
            return next();
        }

        return _HandleTrailingSlashRequest(context);
    }

    private static async Task _HandleTrailingSlashRequest(ResourceExecutingContext context)
    {
        var serviceProvider = context.HttpContext.RequestServices;
        var factory = serviceProvider.GetRequiredService<IProblemDetailsCreator>();
        var problemDetails = factory.EndpointNotFound();
        await Results.Problem(problemDetails).ExecuteAsync(context.HttpContext).ConfigureAwait(false);
    }
}
