// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

[PublicAPI]
public static class ExecutableFinder
{
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
