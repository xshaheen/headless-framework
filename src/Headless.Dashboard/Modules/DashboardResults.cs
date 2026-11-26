// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard;

/// <summary>Results a module's endpoints return for the shell's SDK components.</summary>
[PublicAPI]
public static class DashboardResults
{
    // The CSP sandbox keeps the document in an opaque origin even when it is opened outside the frame, so its scripts,
    // forms, and same-origin reads never run; only inline styles and data: images and fonts load, so a captured email
    // never calls a tracker or a CDN from the operator's browser, and only the dashboard may embed it.
    private const string _PreviewPolicy =
        "sandbox; default-src 'none'; style-src 'unsafe-inline'; img-src data:; font-src data:; "
        + "base-uri 'none'; form-action 'none'; frame-ancestors 'self'";

    /// <summary>
    /// Returns captured HTML, such as an email a host sent in development, for the SDK's <c>SandboxedHtmlFrame</c> to
    /// show. The response carries its own restrictive policy: the document's <c>&lt;style&gt;</c> blocks and
    /// <c>style</c> attributes apply, and its scripts, forms, and remote loads do not run.
    /// </summary>
    /// <remarks>
    /// Only <c>data:</c> images and fonts load, so inline a <c>cid:</c> part as a <c>data:</c> URI before returning the
    /// HTML; a remote image shows as missing.
    /// </remarks>
    /// <param name="html">The captured document, written as it is.</param>
    /// <returns>The result to return from the module's endpoint handler.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is <see langword="null"/>.</exception>
    public static IResult HtmlPreview(string html)
    {
        return new HtmlPreviewResult(Argument.IsNotNull(html));
    }

    private sealed class HtmlPreviewResult(string html) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            var response = httpContext.Response;
            var headers = response.Headers;
            headers.ContentSecurityPolicy = _PreviewPolicy;
            headers.XContentTypeOptions = "nosniff";
            // The document holds captured content, such as reset links, that no cache may keep.
            headers.CacheControl = "no-store";
            headers["Referrer-Policy"] = "no-referrer";
            response.ContentType = "text/html; charset=utf-8";

            return response.WriteAsync(html, Encoding.UTF8, httpContext.RequestAborted);
        }
    }
}
