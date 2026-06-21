// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

/// <summary>Resolves the absolute path of an executable by searching the <c>PATH</c> environment variable.</summary>
[PublicAPI]
public static class ExecutableFinder
{
    /// <summary>
    /// Searches for <paramref name="executableName"/> across the entries of the <c>PATH</c> environment variable
    /// (and the optional <paramref name="workingDirectory"/> first), trying the registered <c>PATHEXT</c> extensions
    /// on Windows when the name has no extension.
    /// </summary>
    /// <param name="executableName">The executable file name to locate, with or without an extension.</param>
    /// <param name="workingDirectory">An optional directory searched before the <c>PATH</c> entries.</param>
    /// <returns>
    /// The full path of the first matching executable, or <see langword="null"/> when no match is found on any search path.
    /// </returns>
    // https://docs.microsoft.com/en-us/windows-server/administration/windows-commands/path
    public static string? GetFullExecutablePath(string executableName, string? workingDirectory = null)
    {
        var isWindows = OperatingSystem.IsWindows();
        var separator = isWindows ? ';' : ':';
        var extensions = isWindows ? (Environment.GetEnvironmentVariable("PATHEXT") ?? "").Split(separator) : [];

        IEnumerable<string> searchPaths = Environment.GetEnvironmentVariable("PATH")?.Split(separator) ?? [];

        if (workingDirectory is not null)
        {
            searchPaths = searchPaths.Prepend(workingDirectory);
        }

        foreach (var searchPath in searchPaths)
        {
            var result = tryFindInDirectory(executableName, searchPath, extensions);

            if (result is not null)
            {
                return Path.GetFullPath(result);
            }
        }

        return null;

        static string? tryFindInDirectory(string executableName, string directory, string[] extensions)
        {
            var fullPath = Path.Combine(directory, executableName);

            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            if (executableName.Contains('.', StringComparison.Ordinal))
            {
                return null;
            }

            foreach (var extension in extensions)
            {
                var pathWithExt = fullPath + extension;

                if (File.Exists(pathWithExt))
                {
                    return pathWithExt;
                }
            }

            return null;
        }
    }
}
