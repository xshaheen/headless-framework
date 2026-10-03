// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc;

namespace Headless.Api.Controllers;

[PublicAPI]
public static class ControllerBaseExtensions
{
    /// <summary>
    /// Returns the appropriate action result for an unauthorized access attempt based on authentication state.
    /// Returns 403 Forbidden when the user is authenticated; returns 401 Unauthorized (challenge) when the user is not authenticated.
    /// </summary>
    /// <param name="controller">The controller handling the request.</param>
    /// <returns>A <see cref="ForbidResult"/> when the user is authenticated, or a <see cref="ChallengeResult"/> when not.</returns>
    public static ActionResult ChallengeOrForbid(this ControllerBase controller)
    {
        return controller.User.Identity?.IsAuthenticated ?? false ? controller.Forbid() : controller.Challenge();
    }

    /// <summary>
    /// Returns the appropriate action result for an unauthorized access attempt, targeting the specified authentication schemes.
    /// Returns 403 Forbidden when the user is authenticated; returns 401 Unauthorized (challenge) when the user is not authenticated.
    /// </summary>
    /// <param name="controller">The controller handling the request.</param>
    /// <param name="authenticationSchemes">The authentication schemes to use for the challenge or forbid result.</param>
    /// <returns>A <see cref="ForbidResult"/> when the user is authenticated, or a <see cref="ChallengeResult"/> scoped to <paramref name="authenticationSchemes"/> when not.</returns>
    public static ActionResult ChallengeOrForbid(this ControllerBase controller, params string[] authenticationSchemes)
    {
        return controller.User.Identity?.IsAuthenticated ?? false
            ? controller.Forbid(authenticationSchemes)
            : controller.Challenge(authenticationSchemes);
    }

    /// <summary>
    /// Creates a <see cref="LocalRedirectResult"/> that redirects to the specified local URL,
    /// optionally escaping the URL to a safe URI-component form.
    /// </summary>
    /// <param name="controller">The controller handling the request.</param>
    /// <param name="localUrl">The local URL to redirect to.</param>
    /// <param name="escapeUrl">When <see langword="true"/>, <paramref name="localUrl"/> is converted to URI-component form before redirecting.</param>
    /// <returns>A <see cref="LocalRedirectResult"/> targeting <paramref name="localUrl"/>.</returns>
    public static ActionResult LocalRedirect(this ControllerBase controller, string localUrl, bool escapeUrl)
    {
        if (!escapeUrl)
        {
            return controller.LocalRedirect(localUrl);
        }

        return controller.LocalRedirect(localUrl.ToUriComponents());
    }

    /// <summary>
    /// Creates a <see cref="RedirectResult"/> that redirects to the specified URL,
    /// optionally escaping the URL to a safe URI-component form.
    /// </summary>
    /// <param name="controller">The controller handling the request.</param>
    /// <param name="url">The URL to redirect to.</param>
    /// <param name="escapeUrl">When <see langword="true"/>, <paramref name="url"/> is converted to URI-component form before redirecting.</param>
    /// <returns>A <see cref="RedirectResult"/> targeting <paramref name="url"/>.</returns>
    public static ActionResult Redirect(this ControllerBase controller, string url, bool escapeUrl)
    {
        if (!escapeUrl)
        {
            return controller.Redirect(url);
        }

        return controller.Redirect(url.ToUriComponents());
    }
}
