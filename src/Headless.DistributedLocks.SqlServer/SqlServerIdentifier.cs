// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Checks;

namespace Headless.DistributedLocks.SqlServer;

internal static partial class SqlServerIdentifier
{
    private const string _DefaultSequenceName = "headless_distlocks_fence";

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

    public static string Quote(string identifier)
    {
        Argument.IsNotNullOrWhiteSpace(identifier);

        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    [GeneratedRegex("[^A-Za-z0-9_]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex _UnsafeIdentifierCharacters();
}
