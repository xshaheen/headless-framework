// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Headless.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Headless.Dashboard;

/// <summary>
/// Shared SPA helpers used by Jobs and Messaging dashboards.
/// </summary>
internal static partial class DashboardSpaHelper
{
    private static readonly FileExtensionContentTypeProvider _ContentTypeProvider = new();

    [GeneratedRegex(@"(?is)<head\b[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex HeadOpenRegex { get; }

    /// <summary>
    /// Normalizes a dashboard base path for the host's endpoint routing and refuses the root: there the SPA route
    /// would answer every unmatched GET in the host with the shell, hiding the host's own 404s.
    /// </summary>
    /// <exception cref="InvalidOperationException">The base path is empty or the root.</exception>
    internal static string NormalizeMountBasePath(string basePath, string mapMethodName)
    {
        var normalized = NormalizeBasePath(basePath);

        if (normalized is "" or "/")
        {
            throw new InvalidOperationException(
                $"{mapMethodName}() cannot mount the dashboard at the root path, because its SPA route would answer "
                    + "every unmatched GET in the host. Call SetBasePath(...) with a path such as \"/dashboard\"."
            );
        }

        return normalized;
    }

    /// <summary>
    /// Maps the anonymous SPA route under the dashboard's group: an embedded asset when one matches the path, else the
    /// shell, so client-side routes deep-link. Paths under <c>api</c> never get the shell: an unmatched API route is a
    /// 404, not an anonymous HTML page. The shell carries only what <paramref name="injectConfig"/> adds.
    /// </summary>
    internal static RouteHandlerBuilder MapDashboardSpa(
        this IEndpointRouteBuilder group,
        string routeName,
        IFileProvider fileProvider,
        Func<string, HttpContext, string> injectConfig
    )
    {
        return group
            .MapMethods(
                "/{**path}",
                [HttpMethods.Get, HttpMethods.Head],
                (HttpContext context, string? path) => _ServeSpaAsync(context, path, fileProvider, injectConfig)
            )
            .WithName(routeName)
            .AllowAnonymous()
            .ExcludeFromDescription();
    }

    private static async Task<IResult> _ServeSpaAsync(
        HttpContext context,
        string? path,
        IFileProvider fileProvider,
        Func<string, HttpContext, string> injectConfig
    )
    {
        if (
            path is not null
            && (
                string.Equals(path, "api", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return Results.NotFound();
        }

        if (!string.IsNullOrEmpty(path) && !string.Equals(path, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            var file = fileProvider.GetFileInfo(path);
            if (file is { Exists: true, IsDirectory: false })
            {
                if (!_ContentTypeProvider.TryGetContentType(file.Name, out var contentType))
                {
                    contentType = "application/octet-stream";
                }

                _ApplyAssetCacheHeaders(context, file.Name);
                return Results.Stream(file.CreateReadStream(), contentType, lastModified: file.LastModified);
            }
        }

        var shell = fileProvider.GetFileInfo("index.html");
        if (!shell.Exists)
        {
            return Results.NotFound();
        }

        string html;
        await using (var stream = shell.CreateReadStream())
        {
            using var reader = new StreamReader(stream);
            html = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);
        }

        return Results.Content(injectConfig(html, context), ContentTypes.Texts.Html);
    }

    // Built assets carry a content hash in their names, so an hour of caching never serves a stale bundle.
    private static void _ApplyAssetCacheHeaders(HttpContext context, string fileName)
    {
        if (
            fileName.EndsWith(".js", StringComparison.Ordinal)
            || fileName.EndsWith(".css", StringComparison.Ordinal)
            || fileName.EndsWith(".ico", StringComparison.Ordinal)
            || fileName.EndsWith(".png", StringComparison.Ordinal)
        )
        {
            context.Response.Headers.CacheControl = "public,max-age=3600";
        }
    }

    internal static string NormalizeBasePath(string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return "/";
        }

        if (!basePath.StartsWith('/'))
        {
            basePath = "/" + basePath;
        }

        return basePath.TrimEnd('/');
    }

    internal static string CombinePathBase(string? pathBase, string? basePath)
    {
        pathBase ??= string.Empty;
        basePath ??= "/";

        if (string.IsNullOrEmpty(basePath) || string.Equals(basePath, "/", StringComparison.Ordinal))
        {
            return string.IsNullOrEmpty(pathBase) ? "/" : pathBase;
        }

        if (string.IsNullOrEmpty(pathBase))
        {
            return basePath;
        }

        // If basePath already includes the pathBase prefix, treat it as the full frontend path.
        // This prevents /cool-app/cool-app/... and similar double-prefix issues when users
        // configure BasePath with the full URL segment.
        if (basePath.StartsWith(pathBase, StringComparison.OrdinalIgnoreCase))
        {
            return basePath;
        }

        // Normalize to avoid double slashes
        if (pathBase.EndsWith('/'))
        {
            pathBase = pathBase.TrimEnd('/');
        }

        // basePath is already normalized to start with '/'
        return pathBase + basePath;
    }

    /// <summary>
    /// Builds the shell's <c>&lt;base&gt;</c> tag for the frontend base path, attribute-encoded: the path base can come
    /// from a forwarded prefix header, so it may carry a quote or markup.
    /// </summary>
    internal static string BuildBaseTag(string frontendBasePath)
    {
        return $"""<base href="{HtmlEncoder.Default.Encode(frontendBasePath + "/")}" />""";
    }

    /// <summary>
    /// Prevents &lt;/script&gt; in JSON strings from prematurely closing the inline script.
    /// </summary>
    internal static string SanitizeForInlineScript(string json)
    {
        return json.Replace("</script", "<\\/script", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Injects a base tag and script block into HTML content immediately after the opening
    /// &lt;head&gt; tag (preferred), before &lt;/head&gt; (fallback), or prepended (last resort).
    /// </summary>
    internal static string InjectIntoHead(string htmlContent, string fullInjection)
    {
        // Prefer inject immediately after opening <head ...>
        var headOpen = HeadOpenRegex.Match(htmlContent);
        if (headOpen.Success)
        {
            return htmlContent.Insert(headOpen.Index + headOpen.Length, fullInjection);
        }

        // Fallback: just before </head>
        var closeIdx = htmlContent.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (closeIdx >= 0)
        {
            return htmlContent.Insert(closeIdx, fullInjection);
        }

        // Last resort: prepend (ensures script runs early)
        return fullInjection + htmlContent;
    }
}
