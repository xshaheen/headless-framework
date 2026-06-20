// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Checks;

namespace Headless.DistributedLocks.SqlServer;

/// <summary>
/// Helpers for building and safely quoting SQL Server identifiers used by the distributed-lock
/// fencing-token storage (the fence sequence object name).
/// </summary>
internal static partial class SqlServerIdentifier
{
    private const string _DefaultSequenceName = "headless_distlocks_fence";

    /// <summary>
    /// Derives the fence sequence object name for a given <paramref name="keyPrefix"/>: starts from the
    /// default name, appends the prefix with unsafe characters collapsed to <c>_</c>, and truncates the
    /// result to <see cref="SqlServerDistributedLockFieldLimits.MaxIdentifierLength"/> when needed.
    /// </summary>
    /// <param name="keyPrefix">The lock key prefix to fold into the sequence name. Must be non-null and non-whitespace.</param>
    /// <returns>A SQL Server-safe, length-bounded sequence object name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keyPrefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="keyPrefix"/> is empty or whitespace.</exception>
    public static string FenceSequenceName(string keyPrefix)
    {
        Argument.IsNotNullOrWhiteSpace(keyPrefix);

        var builder = new StringBuilder(_DefaultSequenceName);
        var normalized = _UnsafeIdentifierCharacters().Replace(keyPrefix, "_").Trim('_');

        if (!string.IsNullOrWhiteSpace(normalized))
        {
            builder.Append('_').Append(normalized);
        }

        var value = builder.ToString();

        return value.Length <= SqlServerDistributedLockFieldLimits.MaxIdentifierLength
            ? value
            : value[..SqlServerDistributedLockFieldLimits.MaxIdentifierLength].TrimEnd('_');
    }

    /// <summary>
    /// Bracket-quotes a SQL Server identifier, escaping any embedded <c>]</c> as <c>]]</c> so the value is
    /// safe to embed in dynamic SQL without delimiter injection.
    /// </summary>
    /// <param name="identifier">The identifier to quote. Must be non-null and non-whitespace.</param>
    /// <returns>The identifier wrapped in <c>[</c>…<c>]</c> with embedded brackets escaped.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="identifier"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="identifier"/> is empty or whitespace.</exception>
    public static string Quote(string identifier)
    {
        Argument.IsNotNullOrWhiteSpace(identifier);

        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    [GeneratedRegex("[^A-Za-z0-9_]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex _UnsafeIdentifierCharacters();
}
