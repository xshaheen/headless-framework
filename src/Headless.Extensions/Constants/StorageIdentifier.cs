// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;

namespace Headless.Constants;

/// <summary>
/// Database identifier rules (validation pattern and maximum length) for the supported SQL
/// providers, used to validate schema/table/column names before they are emitted into raw DDL.
/// </summary>
[PublicAPI]
public static partial class StorageIdentifier
{
    /// <summary>SQL Server identifier rules.</summary>
    public static partial class SqlServer
    {
        /// <summary>
        /// SQL Server regular-identifier pattern. Allows <c>@</c>, <c>$</c>, and <c>#</c> in
        /// non-first positions in addition to letters, digits, and underscores. <c>@</c> and
        /// <c>#</c> are intentionally rejected as the first character because they carry
        /// special T-SQL meaning (local variable / temporary table) that would mislead anyone
        /// reading raw DDL.
        /// </summary>
        [GeneratedRegex(
            pattern: "^[A-Za-z_][A-Za-z0-9_@$#]*$",
            options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
            matchTimeoutMilliseconds: RegexPatterns.MatchTimeoutMilliseconds
        )]
        public static partial Regex IdentifierPattern { get; }

        /// <summary>SQL Server regular identifier length cap.</summary>
        public const int IdentifierMaxLength = 128;
    }

    /// <summary>PostgreSQL identifier rules.</summary>
    public static partial class PostgreSql
    {
        /// <summary>
        /// PostgreSQL unquoted-identifier pattern: leading letter or underscore, then letters,
        /// digits, and underscores.
        /// </summary>
        [GeneratedRegex(
            pattern: "^[A-Za-z_][A-Za-z0-9_]*$",
            options: RegexOptions.Compiled | RegexOptions.ExplicitCapture,
            matchTimeoutMilliseconds: RegexPatterns.MatchTimeoutMilliseconds
        )]
        public static partial Regex IdentifierPattern { get; }

        /// <summary>PostgreSQL identifier length cap (NAMEDATALEN - 1 = 63).</summary>
        public const int IdentifierMaxLength = 63;
    }
}
