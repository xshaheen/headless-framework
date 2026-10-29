// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting;
using Headless.Search;
using Headless.Search.PostgreSql;
using Headless.Sql.PostgreSql;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Microsoft.EntityFrameworkCore.Migrations;

/// <summary>Moves stored Arabic search keys to another rule version in an EF Core migration.</summary>
[PublicAPI]
public static class HeadlessArabicSearchMigrationBuilderExtensions
{
    /// <summary>
    /// Rebuilds a stored search key with <paramref name="version" />'s function:
    /// <c>ALTER TABLE … ALTER COLUMN … SET EXPRESSION AS (…)</c>. Use it in place of the drop-and-add EF Core scaffolds
    /// when a model's <c>HasHeadlessArabicSearch</c> version changes.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The table holding the key.</param>
    /// <param name="column">The stored key column.</param>
    /// <param name="sourceColumn">The text column the key is built from.</param>
    /// <param name="version">The rule version to rebuild the key with.</param>
    /// <param name="schema">The table's schema; <see langword="null" /> for the search path's default.</param>
    /// <param name="functionSchema">The schema holding the search function. The default is <see cref="HeadlessStorageDefaults.Schema" />.</param>
    /// <returns>The operation builder.</returns>
    /// <remarks>
    /// Needs PostgreSQL 17 or later. PostgreSQL rewrites every row and rebuilds the table's indexes under an
    /// <c>ACCESS EXCLUSIVE</c> lock, so plan it like any table rewrite. The previous version's function stays: another
    /// table may still use it.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">A name is <see langword="null" />, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version" /> is not a defined version.</exception>
    public static OperationBuilder<SqlOperation> SetArabicSearchKeyExpression(
        this MigrationBuilder migrationBuilder,
        string table,
        string column,
        string sourceColumn,
        ArabicSearchVersion version,
        string? schema = null,
        string functionSchema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNull(migrationBuilder);
        Argument.IsNotNullOrWhiteSpace(table);
        Argument.IsNotNullOrWhiteSpace(column);
        Argument.IsNotNullOrWhiteSpace(sourceColumn);
        Argument.IsNotNullOrWhiteSpace(functionSchema);

        var dialect = PostgreSqlDialect.Instance;
        var qualifiedTable = schema is null ? dialect.Quote(table) : dialect.Qualify(schema, table);
        var function = PostgreSqlArabicSearchFunction.Name(version);

        return migrationBuilder.Sql(
            $"ALTER TABLE {qualifiedTable} ALTER COLUMN {dialect.Quote(column)} SET EXPRESSION AS "
                + $"({dialect.Quote(functionSchema)}.{function}({dialect.Quote(sourceColumn)}));"
        );
    }
}
