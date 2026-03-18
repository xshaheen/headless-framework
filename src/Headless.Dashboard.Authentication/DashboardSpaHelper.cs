// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Shared SPA helpers used by Jobs and Messaging dashboards.
/// </summary>
internal static partial class DashboardSpaHelper
{
    [GeneratedRegex(@"(?is)<head\b[^>]*>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    internal static partial Regex HeadOpenRegex();

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
        var headOpen = HeadOpenRegex().Match(htmlContent);
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
