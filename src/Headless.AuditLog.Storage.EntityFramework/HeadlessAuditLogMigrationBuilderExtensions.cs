// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless;
using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// Makes the audit log table of an EF Core model append-only or partitioned in a migration, with the same objects the
/// raw audit log providers create.
/// </summary>
/// <remarks>
/// Pass the schema and table name the model maps <c>AuditLogEntry</c> to: <c>AuditLogStorageOptions.Schema</c> and
/// <c>AuditLogStorageOptions.TableName</c>. A <see langword="null"/> table means the conventional name of the
/// migration's database, <c>audit_log_entries</c> on PostgreSQL and <c>AuditLogEntries</c> on SQL Server.
/// </remarks>
[PublicAPI]
public static class HeadlessAuditLogMigrationBuilderExtensions
{
    /// <summary>
    /// Makes the audit log table append-only: a trigger refuses every <c>UPDATE</c> and <c>DELETE</c> (and, on
    /// PostgreSQL, <c>TRUNCATE</c>, on the table and on each of its partitions) except the retention purge of
    /// <see cref="IAuditLogRetention{TContext}"/>.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for the database's conventional name.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <param name="purgeAuthority">
    /// Who may purge. <see cref="AuditLogPurgeAuthority.Owner"/> (PostgreSQL only) also creates the function
    /// <c>{table}_purge</c>, which runs with its owner's rights and is closed to <c>PUBLIC</c>: grant
    /// <c>EXECUTE</c> on it to the login that runs the retention, and set
    /// <c>AuditLogStorageOptions.PurgeAuthority</c> to match so the retention calls it.
    /// </param>
    /// <param name="minimumRetention">
    /// With <see cref="AuditLogPurgeAuthority.Owner"/>, the retention the purge function enforces whatever period its
    /// caller asks for; <see langword="null"/> for none.
    /// </param>
    /// <returns>The operation builder.</returns>
    /// <remarks>
    /// It is safe to run again in a later migration, for example to change <paramref name="purgeAuthority"/>. On a
    /// partitioned table, call it after <see cref="AddHeadlessAuditLogPartitions"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A name is not a valid identifier for the database, <paramref name="minimumRetention"/> is given without
    /// <see cref="AuditLogPurgeAuthority.Owner"/>, or it is shorter than one second.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The migration's database is neither PostgreSQL nor SQL Server, or <paramref name="purgeAuthority"/> is
    /// <see cref="AuditLogPurgeAuthority.Owner"/> on SQL Server.
    /// </exception>
    public static OperationBuilder<SqlOperation> AddHeadlessAuditLogAppendOnly(
        this MigrationBuilder migrationBuilder,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema,
        AuditLogPurgeAuthority purgeAuthority = AuditLogPurgeAuthority.SessionSetting,
        TimeSpan? minimumRetention = null
    )
    {
        Argument.IsNotNull(migrationBuilder);
        Argument.IsInEnum(purgeAuthority);

        var provider = _Provider(migrationBuilder, "An append-only audit log table");
        var ownerPurge = purgeAuthority is AuditLogPurgeAuthority.Owner;

        Argument.IsTrue(
            ownerPurge || minimumRetention is null,
            $"A minimum retention is enforced by the owner's purge function, so it needs {nameof(AuditLogPurgeAuthority)}.{nameof(AuditLogPurgeAuthority.Owner)}.",
            nameof(minimumRetention)
        );
        _ValidateMinimumRetention(minimumRetention);

        var tableName = _Names(provider, ref schema, table);

        if (provider is StorageProvider.SqlServer)
        {
            if (ownerPurge)
            {
                throw new NotSupportedException(
                    $"{nameof(AuditLogPurgeAuthority)}.{nameof(AuditLogPurgeAuthority.Owner)} is supported on PostgreSQL only."
                );
            }

            return migrationBuilder.Sql(SqlServerAuditLogSql.AppendOnly(schema, tableName));
        }

        var sql = new StringBuilder(PostgreSqlAuditLogSql.AppendOnly(schema, tableName)).AppendLine();

        if (ownerPurge)
        {
            sql.AppendLine(PostgreSqlAuditLogSql.OwnerPurge(schema, tableName, minimumRetention));
        }

        // Partitions that exist already have no TRUNCATE guard of their own yet.
        sql.Append(PostgreSqlAuditLogSql.GuardPartitionsAgainstTruncate(schema, tableName));

        return migrationBuilder.Sql(sql.ToString());
    }

    /// <summary>
    /// Removes what <see cref="AddHeadlessAuditLogAppendOnly"/> created: the triggers, their function, and the owner's
    /// purge function. Use it in <c>Down</c>.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for the database's conventional name.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <returns>The operation builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name is not a valid identifier for the database.</exception>
    /// <exception cref="NotSupportedException">The migration's database is neither PostgreSQL nor SQL Server.</exception>
    public static OperationBuilder<SqlOperation> DropHeadlessAuditLogAppendOnly(
        this MigrationBuilder migrationBuilder,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNull(migrationBuilder);

        var provider = _Provider(migrationBuilder, "An append-only audit log table");
        var tableName = _Names(provider, ref schema, table);

        return migrationBuilder.Sql(
            provider is StorageProvider.SqlServer
                ? SqlServerAuditLogSql.DropAppendOnly(schema, tableName)
                : PostgreSqlAuditLogSql.DropAppendOnly(schema, tableName)
        );
    }

    /// <summary>
    /// Adds monthly partitions to an audit log table that the migration created partitioned (PostgreSQL only): creates
    /// the functions <c>{table}_ensure_partitions</c> and <c>{table}_drop_partitions</c> that
    /// <see cref="IAuditLogPartitions{TContext}"/> calls, and the partitions from the current month through
    /// <paramref name="monthsAhead"/> months ahead.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for <c>audit_log_entries</c>.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <param name="monthsAhead">How many months after the current one to create now. Range: 0 to 120.</param>
    /// <param name="minimumRetention">
    /// The retention the drop function enforces whatever period its caller asks for; <see langword="null"/> for none.
    /// </param>
    /// <returns>The operation builder.</returns>
    /// <remarks>
    /// <para>
    /// The table must already be partitioned by range on <c>created_at</c>: PostgreSQL cannot partition a table in
    /// place, and EF Core scaffolds a plain <c>CreateTable</c>. Create the table in the migration with
    /// <c>PARTITION BY RANGE ("created_at")</c>, then call this, then <see cref="AddHeadlessAuditLogAppendOnly"/>.
    /// The migration fails when the table is not partitioned.
    /// </para>
    /// <para>
    /// Both functions run with their owner's rights and are closed to <c>PUBLIC</c>: grant <c>EXECUTE</c> on them to
    /// the application's login. A row whose <c>created_at</c> falls in no partition fails its insert, so schedule
    /// <see cref="IAuditLogPartitions{TContext}.EnsureAsync"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A name is not a valid PostgreSQL identifier, or <paramref name="minimumRetention"/> is shorter than one second.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="monthsAhead"/> is outside 0 to 120.</exception>
    /// <exception cref="NotSupportedException">The migration's database is not PostgreSQL.</exception>
    public static OperationBuilder<SqlOperation> AddHeadlessAuditLogPartitions(
        this MigrationBuilder migrationBuilder,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema,
        int monthsAhead = 3,
        TimeSpan? minimumRetention = null
    )
    {
        Argument.IsNotNull(migrationBuilder);
        Argument.IsInclusiveBetween(monthsAhead, 0, PostgreSqlAuditLogSql.MaxMonthsAhead);
        _ValidateMinimumRetention(minimumRetention);

        if (_Provider(migrationBuilder, "A partitioned audit log table") is not StorageProvider.PostgreSql)
        {
            throw new NotSupportedException("A partitioned audit log table is supported on PostgreSQL only.");
        }

        var tableName = _Names(StorageProvider.PostgreSql, ref schema, table);

        return migrationBuilder.Sql(
            new StringBuilder()
                .AppendLine(PostgreSqlAuditLogSql.RequirePartitioned(schema, tableName))
                .AppendLine(PostgreSqlAuditLogSql.PartitionFunctions(schema, tableName, minimumRetention))
                .Append(PostgreSqlAuditLogSql.EnsurePartitionsNow(schema, tableName, monthsAhead))
                .ToString()
        );
    }

    private static StorageProvider _Provider(MigrationBuilder migrationBuilder, string feature)
    {
        return EfAuditLogDatabase.Provider(migrationBuilder.ActiveProvider)
            ?? throw new NotSupportedException(
                $"{feature} is supported on PostgreSQL and SQL Server, not on '{migrationBuilder.ActiveProvider}'."
            );
    }

    // The names are spliced into DDL, so they must pass the same identifier rules as the storage options.
    private static string _Names(StorageProvider provider, ref string schema, string? table)
    {
        var style =
            provider is StorageProvider.PostgreSql ? StorageNamingStyle.SnakeCase : StorageNamingStyle.PascalCase;
        var tableName = HeadlessStorageNaming.Resolve(table, style, AuditLogStorageNames.DefaultTableName);
        var (pattern, maxLength) = StorageIdentifier.For(provider);

        Argument.IsNotNullOrWhiteSpace(schema);
        Argument.IsTrue(
            schema.Length <= maxLength && pattern.IsMatch(schema),
            $"'{schema}' is not a valid {provider} identifier.",
            nameof(schema)
        );
        Argument.IsTrue(
            tableName.Length <= maxLength && pattern.IsMatch(tableName),
            $"'{tableName}' is not a valid {provider} identifier.",
            nameof(table)
        );

        return tableName;
    }

    private static void _ValidateMinimumRetention(TimeSpan? minimumRetention)
    {
        Argument.IsTrue(
            minimumRetention is null || minimumRetention >= TimeSpan.FromSeconds(1),
            "A minimum retention must be at least one second.",
            nameof(minimumRetention)
        );
    }
}
