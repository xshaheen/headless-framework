// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.AuditLog;
using Headless.AuditLog.Internal;
using Headless.AuditLog.SqlServer;
using Headless.Checks;
using Headless.Hosting;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;

namespace Microsoft.EntityFrameworkCore.Migrations;

/// <summary>
/// Makes the audit log table of an EF Core model append-only in a SQL Server migration, with the same trigger the raw
/// SQL Server audit log provider creates.
/// </summary>
/// <remarks>
/// <para>
/// Pass the schema and table name the model maps <c>AuditLogEntry</c> to: <c>AuditLogStorageOptions.Schema</c> and
/// <c>AuditLogStorageOptions.TableName</c>. A <see langword="null"/> table means the conventional name,
/// <c>AuditLogEntries</c>.
/// </para>
/// <para>
/// The PostgreSQL Entity Framework audit package declares helpers of the same names. An application that references
/// both packages calls a helper through its class, because the compiler cannot choose between them.
/// </para>
/// </remarks>
[PublicAPI]
public static class HeadlessSqlServerAuditLogMigrationBuilderExtensions
{
    private const string _Package = "Headless.AuditLog.Storage.SqlServer.EntityFramework";

    /// <summary>
    /// Makes the audit log table append-only: a trigger refuses every <c>UPDATE</c> and <c>DELETE</c> except the
    /// retention purge of <see cref="IAuditLogRetention{TContext}"/>.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for <c>AuditLogEntries</c>.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <returns>The operation builder.</returns>
    /// <remarks>
    /// SQL Server fires no trigger on <c>TRUNCATE</c>: deny <c>ALTER</c> on the table to the application's login.
    /// It is safe to run again in a later migration.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name is not a valid SQL Server identifier.</exception>
    /// <exception cref="NotSupportedException">The migration's database is not SQL Server.</exception>
    public static OperationBuilder<SqlOperation> AddHeadlessSqlServerAuditLogAppendOnly(
        this MigrationBuilder migrationBuilder,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNull(migrationBuilder);
        AuditLogMigrationNames.EnsureProvider(migrationBuilder.ActiveProvider, StorageProvider.SqlServer, _Package);

        var tableName = AuditLogMigrationNames.Resolve(StorageProvider.SqlServer, schema, table);

        return migrationBuilder.Sql(SqlServerAuditLogSql.AppendOnly(schema, tableName));
    }

    /// <summary>
    /// Removes the trigger <see cref="AddHeadlessSqlServerAuditLogAppendOnly"/> created. Use it in <c>Down</c>.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The audit log table; <see langword="null"/> for <c>AuditLogEntries</c>.</param>
    /// <param name="schema">The table's schema. The default is <see cref="HeadlessStorageDefaults.Schema"/>.</param>
    /// <returns>The operation builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="migrationBuilder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A name is not a valid SQL Server identifier.</exception>
    /// <exception cref="NotSupportedException">The migration's database is not SQL Server.</exception>
    public static OperationBuilder<SqlOperation> DropHeadlessSqlServerAuditLogAppendOnly(
        this MigrationBuilder migrationBuilder,
        string? table = null,
        string schema = HeadlessStorageDefaults.Schema
    )
    {
        Argument.IsNotNull(migrationBuilder);
        AuditLogMigrationNames.EnsureProvider(migrationBuilder.ActiveProvider, StorageProvider.SqlServer, _Package);

        var tableName = AuditLogMigrationNames.Resolve(StorageProvider.SqlServer, schema, table);

        return migrationBuilder.Sql(SqlServerAuditLogSql.DropAppendOnly(schema, tableName));
    }
}
