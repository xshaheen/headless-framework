// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;

namespace Headless.UnitOfWork.Internal;

/// <summary>
/// The SQLite half of <see cref="RelationalDatabaseIdentity" />. A <c>Microsoft.Data.Sqlite</c> connection reports the
/// path from its connection string until it opens, then the path SQLite opened, which on Unix has its symbolic links
/// resolved. The same file reached through a symlinked directory (the macOS temp directory, <c>/var</c> →
/// <c>/private/var</c>) would read as two databases, so both sides are compared by their real path. The connection is
/// recognized by its full type name, because this package references no driver.
/// </summary>
internal static class SqliteFileIdentity
{
    private const string _SqliteConnectionTypeName = "Microsoft.Data.Sqlite.SqliteConnection";

    // Matches the kernel's own limit on links followed in one lookup (ELOOP); a longer chain is a loop.
    private const int _MaxLinksFollowed = 40;

    public static bool IsSqlite(DbConnection connection)
    {
        return string.Equals(connection.GetType().FullName, _SqliteConnectionTypeName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the real path of a SQLite file data source, or an empty string for an in-memory or URI source, which
    /// names no file the comparison can trust. On Windows the path is lower-cased, because its file system ignores case
    /// and the comparison before real-path resolution already did.
    /// </summary>
    public static string NormalizeDataSource(string? dataSource)
    {
        return NormalizeDataSource(dataSource, ignoreCase: OperatingSystem.IsWindows());
    }

    internal static string NormalizeDataSource(string? dataSource, bool ignoreCase)
    {
        if (
            string.IsNullOrWhiteSpace(dataSource)
            || string.Equals(dataSource, ":memory:", StringComparison.Ordinal)
            || dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
        )
        {
            return string.Empty;
        }

        var path = ResolveRealPath(dataSource);

        return ignoreCase ? path.ToLowerInvariant() : path;
    }

    /// <summary>
    /// Resolves every symbolic link along <paramref name="path" />, segment by segment. A segment that does not exist
    /// ends the walk and the rest of the path is kept as written, so a database file not created yet still resolves
    /// through its existing parent directories; a link that cannot be read is kept as written.
    /// </summary>
    internal static string ResolveRealPath(string path)
    {
        var current = Path.GetFullPath(path);

        for (var followed = 0; followed < _MaxLinksFollowed; followed++)
        {
            var next = _ResolveFirstLink(current);
            if (next is null)
            {
                return current;
            }

            current = next;
        }

        return current;
    }

    // The path with its first symbolic link replaced by the link's target, or null when no existing segment is a link.
    private static string? _ResolveFirstLink(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var segments = fullPath[root.Length..]
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries
            );
        var walked = root;

        for (var i = 0; i < segments.Length; i++)
        {
            walked = Path.Combine(walked, segments[i]);

            FileSystemInfo info = Directory.Exists(walked) ? new DirectoryInfo(walked) : new FileInfo(walked);
            if (!info.Exists)
            {
                return null;
            }

            string? target;
            try
            {
                target = info.LinkTarget;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            if (target is null)
            {
                continue;
            }

            // A relative target is relative to the directory that holds the link.
            var resolved = Path.GetFullPath(target, Path.GetDirectoryName(walked) ?? root);

            return Path.Join([resolved, .. segments[(i + 1)..]]);
        }

        return null;
    }
}
