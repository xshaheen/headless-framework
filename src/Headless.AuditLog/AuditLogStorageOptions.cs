// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.AuditLog;

/// <summary>
/// Shared storage options for the audit log table, applied by all providers
/// (EntityFramework, PostgreSql, and SqlServer).
/// </summary>
/// <remarks>
/// Configure these options via <c>HeadlessAuditLogSetupBuilder.ConfigureStorage</c> inside
/// the <c>AddHeadlessAuditLog(setup =&gt; …)</c> lambda. The EF Core provider validates the schema
/// and table name at startup; for the raw-SQL providers (PostgreSql, SqlServer) the deployment creates the table with
/// <c>resources provision</c>, and a host start verifies it.
/// </remarks>
[PublicAPI]
public sealed class AuditLogStorageOptions
{
    /// <summary>
    /// Database schema that contains the audit log table. Default: <see cref="HeadlessStorageDefaults.Schema"/>
    /// (<c>"headless"</c>), the schema every Headless feature shares.
    /// </summary>
    public string Schema { get; set; } = HeadlessStorageDefaults.Schema;

    /// <summary>
    /// Name of the audit log table within <see cref="Schema"/>. Default: <see langword="null"/>, which uses the
    /// database's conventional name: <c>audit_log_entries</c> on PostgreSQL, <c>AuditLogEntries</c> elsewhere. A
    /// configured name is used verbatim, and the table's key and index names derive from it.
    /// </summary>
    public string? TableName { get; set; }

    /// <summary>
    /// Override the column type used for JSON columns (<c>OldValues</c>, <c>NewValues</c>,
    /// <c>ChangedFields</c>). When <see langword="null"/>, each provider falls back to its own default:
    /// <see cref="AuditLogJsonColumnType.Jsonb"/> for PostgreSql and
    /// <see cref="AuditLogJsonColumnType.NvarcharMax"/> for SqlServer.
    /// </summary>
    /// <remarks>
    /// The PostgreSql provider rejects <see cref="AuditLogJsonColumnType.NvarcharMax"/>;
    /// the SqlServer provider rejects <see cref="AuditLogJsonColumnType.Jsonb"/> and
    /// <see cref="AuditLogJsonColumnType.Json"/>. Both are validated on startup.
    /// </remarks>
    public AuditLogJsonColumnType? JsonColumnType { get; set; }

    /// <summary>
    /// Override the SQL column type for the <c>CreatedAt</c> column (e.g.,
    /// <c>"timestamp with time zone"</c> for PostgreSql, <c>"datetime2"</c> for SqlServer).
    /// When <see langword="null"/> or empty, the provider uses its own default mapping.
    /// </summary>
    public string? CreatedAtColumnType { get; set; }

    /// <summary>
    /// Makes the audit table append-only on the PostgreSQL and SQL Server providers: <c>resources provision</c> adds a
    /// trigger that refuses every <c>UPDATE</c> and <c>DELETE</c> (and, on PostgreSQL, <c>TRUNCATE</c>) except the
    /// retention purge of <see cref="IAuditLogRetention{TContext}"/>. Default: <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The trigger stops application code and accidental statements, not a database principal who can run arbitrary
    /// SQL: the purge's bypass is a session setting any such principal can set too, and on SQL Server a principal
    /// with <c>ALTER</c> on the table can still <c>TRUNCATE</c> it. <see cref="PurgeAuthority"/> closes the first gap on
    /// PostgreSQL. Turning it on later adds the trigger at the next <c>resources provision</c>; turning it off later
    /// leaves the trigger in place until it is dropped by hand. The Entity Framework provider rejects it, because its
    /// table comes from the application's migrations: call <c>migrationBuilder.AddHeadlessAuditLogAppendOnly(...)</c>
    /// in a migration instead.
    /// </remarks>
    public bool AppendOnly { get; set; }

    /// <summary>
    /// Who may delete rows from an append-only table. Default: <see cref="AuditLogPurgeAuthority.SessionSetting"/>, any
    /// login that turns the purge's session setting on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AuditLogPurgeAuthority.Owner"/> is PostgreSQL only. On the PostgreSQL provider it needs
    /// <see cref="AppendOnly"/>, and <c>resources provision</c> then adds a schema step that makes the trigger also
    /// require the deleting role to hold the table owner's rights, and creates the function
    /// <c>{table}_purge</c> that runs with them. <see cref="IAuditLogRetention{TContext}"/> calls that function
    /// instead of deleting rows itself. The function is closed to <c>PUBLIC</c>: grant <c>EXECUTE</c> on it to the
    /// login that runs the retention. Turning it off later leaves the owner check in place.
    /// </para>
    /// <para>
    /// With Entity Framework storage the migration installs the same objects
    /// (<c>migrationBuilder.AddHeadlessAuditLogAppendOnly(purgeAuthority: AuditLogPurgeAuthority.Owner)</c>), and this
    /// option tells <see cref="IAuditLogRetention{TContext}"/> to call the function.
    /// </para>
    /// </remarks>
    public AuditLogPurgeAuthority PurgeAuthority { get; set; }

    /// <summary>
    /// The retention the database enforces whatever period a caller asks for: the owner's purge function and the
    /// partition drop function refuse a cutoff newer than now minus this period. Default: <see langword="null"/>, no
    /// floor.
    /// </summary>
    /// <remarks>
    /// PostgreSQL provider only, with <see cref="AuditLogPurgeAuthority.Owner"/> or
    /// <see cref="AuditLogPartitioning.Monthly"/>. It is compiled into those functions, so it shapes their schema
    /// steps: changing it after <c>resources provision</c> fails verification with a checksum mismatch. With Entity
    /// Framework storage, pass it to the migration helpers instead.
    /// </remarks>
    public TimeSpan? MinimumRetention { get; set; }

    /// <summary>
    /// Partitions the audit table by month on <c>CreatedAt</c>. Default: <see cref="AuditLogPartitioning.None"/>, one
    /// plain table.
    /// </summary>
    /// <remarks>
    /// PostgreSQL provider only. <c>resources provision</c> creates the table partitioned, the functions
    /// <c>{table}_ensure_partitions</c> and <c>{table}_drop_partitions</c>, and the partitions from the current month
    /// through <see cref="PartitionMonthsAhead"/> months ahead; <see cref="IAuditLogPartitions{TContext}"/> keeps them
    /// from then on. A row whose <c>CreatedAt</c> falls in no partition fails its insert: there is no default
    /// partition. PostgreSQL cannot partition a table in place, so turning this on for a table that already exists
    /// fails verification with a checksum mismatch. With Entity Framework storage, call
    /// create the table partitioned in a migration and call <c>migrationBuilder.AddHeadlessAuditLogPartitions(...)</c>
    /// instead.
    /// </remarks>
    public AuditLogPartitioning Partitioning { get; set; }

    /// <summary>
    /// How many months after the current one <see cref="IAuditLogPartitions{TContext}.EnsureAsync"/> and
    /// <c>resources provision</c> keep a partition for. Default: 3. Range: 0 to 120.
    /// </summary>
    public int PartitionMonthsAhead { get; set; } = 3;

    /// <summary>The most months ahead <see cref="PartitionMonthsAhead"/> accepts.</summary>
    internal const int MaxPartitionMonthsAhead = 120;

    /// <summary>Returns the audit log table name to use on a database with <paramref name="style"/>.</summary>
    /// <param name="style">The naming style of the target database.</param>
    /// <returns>The configured name, or the conventional default for <paramref name="style"/>.</returns>
    public string ResolveTableName(StorageNamingStyle style)
    {
        return HeadlessStorageNaming.Resolve(TableName, style, AuditLogStorageNames.DefaultTableName);
    }

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so
    /// adding a new property to this type only requires extending this single method — the
    /// setup pipeline picks it up automatically instead of silently dropping it.
    /// </summary>
    internal void CopyTo(AuditLogStorageOptions target)
    {
        target.Schema = Schema;
        target.TableName = TableName;
        target.JsonColumnType = JsonColumnType;
        target.CreatedAtColumnType = CreatedAtColumnType;
        target.AppendOnly = AppendOnly;
        target.PurgeAuthority = PurgeAuthority;
        target.MinimumRetention = MinimumRetention;
        target.Partitioning = Partitioning;
        target.PartitionMonthsAhead = PartitionMonthsAhead;
    }
}

/// <summary>SQL column type used for JSON columns in the audit log table.</summary>
[PublicAPI]
public enum AuditLogJsonColumnType
{
    /// <summary>
    /// PostgreSQL <c>jsonb</c> binary JSON column. Supports indexing and operator queries.
    /// The default for the PostgreSql provider.
    /// </summary>
    Jsonb = 0,

    /// <summary>
    /// PostgreSQL <c>json</c> text JSON column. Stored as-is; no binary parsing on write.
    /// Supported by the PostgreSql provider only.
    /// </summary>
    Json = 1,

    /// <summary>
    /// SQL Server <c>nvarchar(max)</c> column storing JSON as Unicode text.
    /// The default for the SqlServer provider and the EF Core provider when targeting SqlServer.
    /// </summary>
    NvarcharMax = 2,
}

/// <summary>Who may delete rows from an append-only audit log table.</summary>
[PublicAPI]
public enum AuditLogPurgeAuthority
{
    /// <summary>
    /// Any login that turns the session setting <c>headless.audit_log_purge</c> on. It stops application code and
    /// accidental statements, not a principal who can run arbitrary SQL.
    /// </summary>
    SessionSetting = 0,

    /// <summary>
    /// Only a role that holds the table owner's rights, reached through a purge function that runs with them. A login
    /// without those rights purges by calling that function, and cannot delete rows any other way. PostgreSQL only.
    /// </summary>
    Owner = 1,
}

/// <summary>How the audit log table is partitioned.</summary>
[PublicAPI]
public enum AuditLogPartitioning
{
    /// <summary>One plain table.</summary>
    None = 0,

    /// <summary>One range partition per calendar month of <c>CreatedAt</c>, in UTC. PostgreSQL only.</summary>
    Monthly = 1,
}

internal static class AuditLogJsonColumnTypeExtensions
{
    public static string ToSqlFragment(this AuditLogJsonColumnType columnType)
    {
        return columnType switch
        {
            AuditLogJsonColumnType.Jsonb => "jsonb",
            AuditLogJsonColumnType.Json => "json",
            AuditLogJsonColumnType.NvarcharMax => "nvarchar(max)",
            _ => throw new ArgumentOutOfRangeException(
                nameof(columnType),
                columnType,
                "Unknown AuditLogJsonColumnType."
            ),
        };
    }
}
