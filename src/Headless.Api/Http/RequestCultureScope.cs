// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;

namespace Headless.Api;

/// <summary>Re-applies the culture that request localization negotiated for a request.</summary>
/// <remarks>
/// <c>RequestLocalizationMiddleware</c> sets <see cref="CultureInfo.CurrentCulture"/> and
/// <see cref="CultureInfo.CurrentUICulture"/> only on the async flow below it. Once an exception or a bare
/// status code unwinds to the outer <c>UseHeadless</c> block, those cultures are back to the server default,
/// while the <see cref="IRequestCultureFeature"/> the middleware stored still records the request's choice.
/// Text written from the outer block must read that feature, or a localized request gets the server
/// culture's problem details.
/// </remarks>
internal static class RequestCultureScope
{
    /// <summary>
    /// Applies the request culture of <paramref name="context"/> until the returned scope is disposed.
    /// </summary>
    /// <returns>
    /// A scope that restores the previous cultures, or <see langword="null"/> when request localization did
    /// not run for the request and the current cultures already apply.
    /// </returns>
    [MustDisposeResource]
    public static IDisposable? Enter(HttpContext context)
    {
        return context.Features.Get<IRequestCultureFeature>()?.RequestCulture is { } requestCulture
            ? CultureHelper.Use(requestCulture.Culture, requestCulture.UICulture)
            : null;
    }
}
