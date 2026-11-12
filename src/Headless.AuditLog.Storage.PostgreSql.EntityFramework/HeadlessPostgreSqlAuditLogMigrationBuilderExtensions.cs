// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless;
using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Headless.AuditLog.PostgreSql;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// Makes the audit log table of an EF Core model append-only or partitioned in a PostgreSQL migration, with the same
/// objects the raw PostgreSQL audit log provider creates.
/// </summary>
/// <remarks>
/// <para>
/// Pass the schema and table name the model maps <c>AuditLogEntry</c> to: <c>AuditLogStorageOptions.Schema</c> and
/// <c>AuditLogStorageOptions.TableName</c>. A <see langword="null"/> table means the conventional name,
/// <c>audit_log_entries</c>.
/// </para>
/// <para>
/// The SQL Server Entity Framework audit package declares helpers of the same names. An application that references
/// both packages calls a helper through its class, because the compiler cannot choose between them.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessPostgreSqlAuditLogMigrationBuilderExtensions
{
    private const string _Package = "Headless.AuditLog.Storage.PostgreSql.EntityFramework";

    /// <summary>
    /// Makes the audit log table append-only: a trigger refuses every <c>UPDATE</c>, <c>DELETE</c>, and
    /// <c>TRUNCATE</c>, on the table and on each of its partitions, except the retention purge of
    /// <see cref="IAuditLogRetention{TContext}"/>.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for <c>audit_log_entries</c>.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <param name="purgeAuthority">
    /// Who may purge. <see cref="AuditLogPurgeAuthority.Owner"/> also creates the function <c>{table}_purge</c>,
    /// which runs with its owner's rights and is closed to <c>PUBLIC</c>: grant <c>EXECUTE</c> on it to the login
    /// that runs the retention, and set <c>AuditLogStorageOptions.PurgeAuthority</c> to match so the retention calls
    /// it.
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
    /// A name is not a valid PostgreSQL identifier, <paramref name="minimumRetention"/> is given without
    /// <see cref="AuditLogPurgeAuthority.Owner"/>, or it is shorter than one second.
    /// </exception>
    /// <exception cref="NotSupportedException">The migration's database is not PostgreSQL.</exception>
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
        AuditLogMigrationNames.EnsureProvider(migrationBuilder.ActiveProvider, StorageProvider.PostgreSql, _Package);

        var ownerPurge = purgeAuthority is AuditLogPurgeAuthority.Owner;

        Argument.IsTrue(
            ownerPurge || minimumRetention is null,
            $"A minimum retention is enforced by the owner's purge function, so it needs {nameof(AuditLogPurgeAuthority)}.{nameof(AuditLogPurgeAuthority.Owner)}.",
            nameof(minimumRetention)
        );
        _ValidateMinimumRetention(minimumRetention);

        var tableName = AuditLogMigrationNames.Resolve(StorageProvider.PostgreSql, schema, table);
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
    /// <param name="table">The audit log table; <see langword="null"/> for <c>audit_log_entries</c>.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <returns>The operation builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name is not a valid PostgreSQL identifier.</exception>
    /// <exception cref="NotSupportedException">The migration's database is not PostgreSQL.</exception>
    public static OperationBuilder<SqlOperation> DropHeadlessAuditLogAppendOnly(
        this MigrationBuilder migrationBuilder,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNull(migrationBuilder);
        AuditLogMigrationNames.EnsureProvider(migrationBuilder.ActiveProvider, StorageProvider.PostgreSql, _Package);

        var tableName = AuditLogMigrationNames.Resolve(StorageProvider.PostgreSql, schema, table);

        return migrationBuilder.Sql(PostgreSqlAuditLogSql.DropAppendOnly(schema, tableName));
    }

    /// <summary>
    /// Partitions the audit log table by month: creates the functions
    /// <c>{table}_ensure_partitions</c> and <c>{table}_drop_partitions</c> that
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
    /// Call it in the migration that creates the table, after the scaffolded <c>CreateTable</c> of the audit log, and
    /// before <see cref="AddHeadlessAuditLogAppendOnly"/>. EF Core has no partitioning API, so this changes that
    /// <c>CreateTable</c>: it creates the scaffolded columns as a plain table named <c>{table}_template</c>, has
    /// PostgreSQL create the audit log table from it with <c>PARTITION BY RANGE ("created_at")</c>, drops the
    /// template, and adds the scaffolded primary key, unique constraints, and foreign keys to the partitioned table.
    /// Columns the application added to the entity are carried over like the rest. A partition carries the table's
    /// identity only from PostgreSQL 17: on PostgreSQL 16 a row inserted through the audit log table, as the store
    /// inserts it, still gets its <c>id</c>, and only an insert aimed at one partition has to supply it.
    /// </para>
    /// <para>
    /// In a later migration the table exists already, and PostgreSQL cannot partition a table in place: the migration
    /// then fails unless an earlier migration created the table partitioned.
    /// </para>
    /// <para>
    /// Both functions run with their owner's rights and are closed to <c>PUBLIC</c>: grant <c>EXECUTE</c> on them to
    /// the application's login with <see cref="PostgreSqlAuditLogPartitionFunctions.GrantExecuteStatement"/>. A row
    /// whose <c>created_at</c> falls in no partition fails its insert, so schedule
    /// <see cref="IAuditLogPartitions{TContext}.EnsureAsync"/>. Use <see cref="DropHeadlessAuditLogPartitions"/> in
    /// <c>Down</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// A name is not a valid PostgreSQL identifier, <paramref name="table"/> leaves no room for the template table's
    /// name, or <paramref name="minimumRetention"/> is shorter than one second.
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

        AuditLogMigrationNames.EnsureProvider(migrationBuilder.ActiveProvider, StorageProvider.PostgreSql, _Package);

        var tableName = AuditLogMigrationNames.Resolve(StorageProvider.PostgreSql, schema, table);

        _CreatePartitioned(migrationBuilder, schema, tableName);

        return migrationBuilder.Sql(
            new StringBuilder()
                .AppendLine(PostgreSqlAuditLogSql.RequirePartitioned(schema, tableName))
                .AppendLine(PostgreSqlAuditLogSql.PartitionFunctions(schema, tableName, minimumRetention))
                .Append(PostgreSqlAuditLogSql.EnsurePartitionsNow(schema, tableName, monthsAhead))
                .ToString()
        );
    }

    /// <summary>
    /// Removes the functions <see cref="AddHeadlessAuditLogPartitions"/> created. Use it in
    /// <c>Down</c>; the partitions go with the table when the migration drops it.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for <c>audit_log_entries</c>.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <returns>The operation builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name is not a valid PostgreSQL identifier.</exception>
    /// <exception cref="NotSupportedException">The migration's database is not PostgreSQL.</exception>
    public static OperationBuilder<SqlOperation> DropHeadlessAuditLogPartitions(
        this MigrationBuilder migrationBuilder,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNull(migrationBuilder);

        AuditLogMigrationNames.EnsureProvider(migrationBuilder.ActiveProvider, StorageProvider.PostgreSql, _Package);

        var tableName = AuditLogMigrationNames.Resolve(StorageProvider.PostgreSql, schema, table);

        return migrationBuilder.Sql(PostgreSqlAuditLogSql.DropPartitionFunctions(schema, tableName));
    }

    // When this migration also creates the table, its CreateTable becomes the template PostgreSQL copies into the
    // partitioned table. The new operations go right after it, so the scaffolded CreateIndex calls that follow find
    // the partitioned table wherever in Up the helper is called.
    private static void _CreatePartitioned(MigrationBuilder migrationBuilder, string schema, string tableName)
    {
        var operations = migrationBuilder.Operations;
        var index = operations.FindIndex(operation =>
            operation is CreateTableOperation create
            && string.Equals(create.Name, tableName, StringComparison.Ordinal)
            && string.Equals(create.Schema, schema, StringComparison.Ordinal)
        );

        if (index < 0)
        {
            return;
        }

        var template = PostgreSqlAuditLogSql.TemplateTable(tableName);

        Argument.IsTrue(
            template.Length <= StorageIdentifier.PostgreSql.IdentifierMaxLength,
            $"'{tableName}' leaves no room for the template table '{template}' the partitioned audit log is created from.",
            "table"
        );

        var create = (CreateTableOperation)operations[index];
        List<MigrationOperation> added =
        [
            new SqlOperation { Sql = PostgreSqlAuditLogSql.PartitionedFromTemplate(schema, tableName) },
        ];

        // LIKE names a copied key by its own rules, and copies no foreign key. They keep the model's names when the
        // operations EF scaffolded add them to the partitioned table instead.
        if (create.PrimaryKey is { } primaryKey)
        {
            added.Add(primaryKey);
            create.PrimaryKey = null;
        }

        added.AddRange(create.UniqueConstraints);
        added.AddRange(create.ForeignKeys);
        create.UniqueConstraints.Clear();
        create.ForeignKeys.Clear();
        create.Name = template;

        operations.InsertRange(index + 1, added);
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
