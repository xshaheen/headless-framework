// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using System.Text.Json;
using Headless.Checks;

namespace Headless.Hosting.Initialization;

/// <summary>The identifier casing a database's conventions call for.</summary>
[PublicAPI]
public enum StorageNamingStyle
{
    /// <summary>PascalCase tables, columns, and constraint names (<c>FeatureValues</c>, <c>PK_FeatureValues</c>): SQL Server's convention.</summary>
    PascalCase = 0,

    /// <summary>
    /// Lower-case snake_case names (<c>feature_values</c>, <c>pk_feature_values</c>): PostgreSQL's convention, which
    /// folds unquoted identifiers to lower case, so the names stay usable in hand-written SQL without quotes.
    /// </summary>
    SnakeCase = 1,
}

/// <summary>
/// Maps the PascalCase names a Headless relational feature declares for its tables, columns, keys, and indexes onto
/// the casing of the database it runs on, so the raw-SQL provider and the EF Core mapping of one feature produce the
/// same database objects.
/// </summary>
[PublicAPI]
public static class HeadlessStorageNaming
{
    /// <summary>The EF Core provider name of Npgsql (<c>DatabaseFacade.ProviderName</c>).</summary>
    public const string NpgsqlEfProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>
    /// Returns the naming style for an EF Core provider: <see cref="StorageNamingStyle.SnakeCase"/> for Npgsql and
    /// <see cref="StorageNamingStyle.PascalCase"/> for every other provider, including an unknown one.
    /// </summary>
    /// <param name="efProviderName">The value of <c>DbContext.Database.ProviderName</c>.</param>
    /// <returns>The naming style the provider's database expects.</returns>
    public static StorageNamingStyle ForProvider(string? efProviderName)
    {
        return string.Equals(efProviderName, NpgsqlEfProviderName, StringComparison.Ordinal)
            ? StorageNamingStyle.SnakeCase
            : StorageNamingStyle.PascalCase;
    }

    /// <summary>
    /// Converts one PascalCase identifier to <paramref name="style"/>: returned unchanged for
    /// <see cref="StorageNamingStyle.PascalCase"/>, lower snake_case for <see cref="StorageNamingStyle.SnakeCase"/>
    /// (<c>TenantId</c> becomes <c>tenant_id</c>, <c>HTTPStatus</c> becomes <c>http_status</c>).
    /// </summary>
    /// <remarks>
    /// Pass one name part at a time. A composite such as an index name is built from converted parts
    /// (<c>"IX_" + table + "_" + column</c>), not converted whole, because a user-configured table name inside it must
    /// stay verbatim.
    /// </remarks>
    /// <param name="style">The target naming style.</param>
    /// <param name="pascalName">A PascalCase identifier.</param>
    /// <returns>The identifier in <paramref name="style"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="pascalName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="pascalName"/> is empty or white space.</exception>
    public static string Apply(StorageNamingStyle style, string pascalName)
    {
        Argument.IsNotNullOrWhiteSpace(pascalName);

        return style switch
        {
            StorageNamingStyle.PascalCase => pascalName,
            // System.Text.Json's lower snake-case policy already handles acronyms and digits, so reuse it rather than
            // carry a second word splitter whose edge cases could drift from it.
            StorageNamingStyle.SnakeCase => JsonNamingPolicy.SnakeCaseLower.ConvertName(pascalName),
            _ => throw new ArgumentOutOfRangeException(nameof(style), style, "Unknown storage naming style."),
        };
    }

    /// <summary>
    /// Resolves a configurable object name: a configured name is used verbatim, and <see langword="null"/> falls back
    /// to <paramref name="pascalDefault"/> converted to <paramref name="style"/>.
    /// </summary>
    /// <param name="configured">The name the application configured, or <see langword="null"/> for the default.</param>
    /// <param name="style">The naming style of the target database.</param>
    /// <param name="pascalDefault">The PascalCase default name.</param>
    /// <returns>The object name to use.</returns>
    public static string Resolve(string? configured, StorageNamingStyle style, string pascalDefault)
    {
        return configured ?? Apply(style, pascalDefault);
    }

    /// <summary>
    /// Returns the primary key constraint name for <paramref name="tableName"/>: <c>PK_{table}</c> or
    /// <c>pk_{table}</c>. The table name is used as given, since it is already resolved for the database.
    /// </summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <param name="tableName">The resolved table name.</param>
    /// <returns>The primary key constraint name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tableName"/> is empty or white space.</exception>
    public static string PrimaryKeyName(StorageNamingStyle style, string tableName)
    {
        Argument.IsNotNullOrWhiteSpace(tableName);

        return Apply(style, "PK") + "_" + tableName;
    }

    /// <summary>
    /// Returns an index name for <paramref name="tableName"/> built from PascalCase parts, each converted to
    /// <paramref name="style"/>: <c>IX_{table}_ProviderName_ProviderKey</c> or
    /// <c>ix_{table}_provider_name_provider_key</c>. The table name is used as given.
    /// </summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <param name="tableName">The resolved table name.</param>
    /// <param name="pascalParts">The PascalCase parts after the table name, usually the indexed columns.</param>
    /// <returns>The index name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tableName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tableName"/> or one of <paramref name="pascalParts"/> is empty or white space.
    /// </exception>
    public static string IndexName(StorageNamingStyle style, string tableName, params ReadOnlySpan<string> pascalParts)
    {
        Argument.IsNotNullOrWhiteSpace(tableName);

        var builder = new StringBuilder(Apply(style, "IX")).Append('_').Append(tableName);

        foreach (var part in pascalParts)
        {
            builder.Append('_').Append(Apply(style, part));
        }

        return builder.ToString();
    }
}
