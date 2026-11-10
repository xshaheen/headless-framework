// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>Normalizes the base path the dashboard mounts at.</summary>
internal static class DashboardSpaHelper
{
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
}
