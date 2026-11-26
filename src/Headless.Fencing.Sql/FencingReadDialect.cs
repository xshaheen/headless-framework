// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Fencing;

/// <summary>
/// The engine-specific SQL the inspector's reads need and the shared dialect kit does not offer: a byte length that
/// never ships the bytes, an ordinal, literal prefix match, and an unordered row cap.
/// </summary>
/// <param name="ByteLength">Renders the size in bytes of a binary column.</param>
/// <param name="StartsWith">
/// Renders a predicate that a key column starts with the prefix bound under a parameter, compared ordinally with every
/// character literal.
/// </param>
/// <param name="PrefixValue">Turns a prefix into the value bound for <paramref name="StartsWith" />.</param>
/// <param name="FirstRows">
/// Renders <c>SELECT 1</c> over a <c>FROM … WHERE …</c> clause, stopping after the row count bound under a parameter,
/// in no particular order: a count only needs any rows past its cap, so the database never sorts them.
/// </param>
internal sealed record FencingReadDialect(
    Func<string, string> ByteLength,
    Func<string, string, string> StartsWith,
    Func<string, string> PrefixValue,
    Func<string, string, string> FirstRows
)
{
    /// <summary>
    /// PostgreSQL: <c>starts_with</c> compares bytes under the key columns' <c>"C"</c> collation, so no character is a
    /// wildcard.
    /// </summary>
    public static FencingReadDialect PostgreSql { get; } =
        new(
            static column => $"octet_length({column})",
            static (column, parameter) => $"starts_with({column}, @{parameter})",
            static prefix => prefix,
            static (fromWhere, parameter) => $"SELECT 1 AS one_row {fromWhere} LIMIT @{parameter}"
        );

    /// <summary>
    /// SQL Server: <c>LIKE</c> under the key columns' binary collation is case-sensitive; every pattern character in
    /// the prefix is escaped so it matches literally.
    /// </summary>
    public static FencingReadDialect SqlServer { get; } =
        new(
            static column => $"DATALENGTH({column})",
            static (column, parameter) => $"{column} LIKE @{parameter} ESCAPE N'\\'",
            static prefix =>
                prefix
                    .Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("%", "\\%", StringComparison.Ordinal)
                    .Replace("_", "\\_", StringComparison.Ordinal)
                    .Replace("[", "\\[", StringComparison.Ordinal) + "%",
            // OFFSET/FETCH needs an ORDER BY; TOP does not.
            static (fromWhere, parameter) => $"SELECT TOP (@{parameter}) 1 AS one_row {fromWhere}"
        );

    /// <summary>
    /// SQLite: <c>LIKE</c> ignores ASCII case whatever the collation, so the prefix is compared as the column's leading
    /// characters under its binary collation.
    /// </summary>
    public static FencingReadDialect Sqlite { get; } =
        new(
            static column => $"length({column})",
            static (column, parameter) => $"substr({column}, 1, length(@{parameter})) = @{parameter}",
            static prefix => prefix,
            static (fromWhere, parameter) => $"SELECT 1 AS one_row {fromWhere} LIMIT @{parameter}"
        );
}
