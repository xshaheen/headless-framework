// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Constants;
using Headless.Hosting.Initialization;

namespace FluentValidation;

/// <summary>FluentValidation rules for table names whose key and index names are derived from them.</summary>
[PublicAPI]
public static class HeadlessStorageNameValidators
{
    /// <summary>
    /// Refuses a table name whose longest derived PostgreSQL name, its <c>pk_</c> key or one of its <c>ix_</c>
    /// indexes built in snake_case by <see cref="HeadlessStorageNaming"/>, would exceed the 63-byte PostgreSQL
    /// identifier limit (<see cref="StorageIdentifier.PostgreSql.IdentifierMaxLength"/>).
    /// </summary>
    /// <remarks>
    /// PostgreSQL silently truncates a longer identifier, so two derived names could collide and a lookup by the full
    /// name would miss. It is the strictest supported database, so every provider applies this bound and a table name
    /// valid on one stays valid on the others. An empty name passes here and is left to the identifier rule.
    /// </remarks>
    /// <param name="rule">The rule builder for the table-name property.</param>
    /// <param name="indexes">The PascalCase parts of each index on the table, as the providers name them.</param>
    /// <returns>The rule builder options for chaining.</returns>
#nullable disable // keep the builder nullability-agnostic, matching the shared storage identifier validators
    public static IRuleBuilderOptions<T, string> FitsDerivedPostgreSqlNames<T>(
        this IRuleBuilder<T, string> rule,
        params string[][] indexes
    )
#nullable restore
    {
        return rule.Must(tableName => _LongestDerivedName(tableName, indexes) is not { } longest || _Fits(longest))
            .WithMessage(
                (_, tableName) =>
                    $"Table name '{tableName}' is too long: PostgreSQL truncates identifiers longer than "
                    + $"{StorageIdentifier.PostgreSql.IdentifierMaxLength} bytes, and the name "
                    + $"'{_LongestDerivedName(tableName, indexes)}' derived from it would exceed that."
            );
    }

    private static string? _LongestDerivedName(string? tableName, string[][] indexes)
    {
        return string.IsNullOrWhiteSpace(tableName)
            ? null
            : HeadlessStorageNaming.LongestDerivedName(StorageNamingStyle.SnakeCase, tableName, indexes);
    }

    private static bool _Fits(string name)
    {
        return Encoding.UTF8.GetByteCount(name) <= StorageIdentifier.PostgreSql.IdentifierMaxLength;
    }
}
