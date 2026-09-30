// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Sql;

namespace Headless.Fencing;

/// <summary>
/// The lease table, sequence, and their column names, described once and named by the dialect: snake_case on
/// PostgreSQL, PascalCase on SQL Server.
/// </summary>
internal sealed class FencingTable
{
    // Stored as smallint. Expired is never stored: it is Active with an expiry at or before the database clock.
    public const short Active = 0;
    public const short Settled = 1;
    public const short Released = 2;
    public const short Abandoned = 3;

    public FencingTable(ISqlDialect dialect, string schema)
    {
        Schema = schema;
        TableName = dialect.Name("FencingLeases");
        SequenceName = dialect.Name("FencingLeaseGenerations");
        Table = dialect.Qualify(schema, TableName);
        Sequence = dialect.Qualify(schema, SequenceName);

        string column(string pascal) => dialect.Quote(dialect.Name(pascal));

        TenantId = column("TenantId");
        Kind = column("Kind");
        Resource = column("Resource");
        Generation = column("Generation");
        State = column("State");
        GrantedAt = column("GrantedAt");
        ExpiresAt = column("ExpiresAt");
        EndedAt = column("EndedAt");
        TakeoverCount = column("TakeoverCount");
        Progress = column("Progress");
        ProgressContract = column("ProgressContract");

        Key = [new(TenantId, "TenantId"), new(Kind, "Kind"), new(Resource, "Resource")];
        KeyColumns = [TenantId, Kind, Resource];
        Script = _Script(dialect);
    }

    public string Schema { get; }

    public string TableName { get; }

    public string SequenceName { get; }

    public string Table { get; }

    public string Sequence { get; }

    public string TenantId { get; }

    public string Kind { get; }

    public string Resource { get; }

    public string Generation { get; }

    public string State { get; }

    public string GrantedAt { get; }

    public string ExpiresAt { get; }

    public string EndedAt { get; }

    public string TakeoverCount { get; }

    public string Progress { get; }

    public string ProgressContract { get; }

    public IReadOnlyList<SqlKeyColumn> Key { get; }

    public IReadOnlyList<string> KeyColumns { get; }

    public SqlSchemaScript Script { get; }

    /// <summary>The lock resource replicas creating this table serialize on; per table, so schemas never wait on each other.</summary>
    public string InitializationLock => $"headless_fencing_init:{Schema}.{TableName}";

    public static string StateLiteral(short state) => state.ToString(CultureInfo.InvariantCulture);

    private SqlSchemaScript _Script(ISqlDialect dialect)
    {
        var active = StateLiteral(Active);

        // Key columns compare in binary / "C" order, so kinds, resources, and tenant ids match ordinally whatever the
        // database's default collation is. The clustered primary key over exactly (tenant, kind, resource) is
        // load-bearing on SQL Server: a grant's HOLDLOCK read takes its key-range lock on it, which is what serializes
        // concurrent first grants of a new key. The key-part limits total 448 characters, under SQL Server's 900-byte
        // clustered key limit. One store-wide sequence issues every generation, so a lease granted again after its
        // row was purged still gets a generation above every earlier one. The active index serves the sweep's keyset
        // walk in (expires_at, tenant_id, resource) order; the ended index serves purge.
        var table = new SqlTable(
            TableName,
            [
                new(TenantId, SqlColumnType.KeyText(FencingFieldLimits.TenantIdMaxLength)),
                new(Kind, SqlColumnType.KeyText(FencingFieldLimits.KindMaxLength)),
                new(Resource, SqlColumnType.KeyText(FencingFieldLimits.ResourceMaxLength)),
                new(Generation, SqlColumnType.Int64),
                new(State, SqlColumnType.Int16),
                new(GrantedAt, SqlColumnType.Timestamp),
                new(ExpiresAt, SqlColumnType.Timestamp),
                new(EndedAt, SqlColumnType.Timestamp, Nullable: true),
                new(TakeoverCount, SqlColumnType.Int32, Default: "0"),
                new(Progress, SqlColumnType.Binary, Nullable: true),
                new(ProgressContract, SqlColumnType.Text(FencingFieldLimits.ProgressContractMaxLength), Nullable: true),
            ],
            dialect.Name("PK_FencingLeases"),
            [TenantId, Kind, Resource],
            [
                (dialect.Name("CK_FencingLeases_Generation"), $"{Generation} > 0"),
                (dialect.Name("CK_FencingLeases_State"), $"{State} BETWEEN {active} AND {StateLiteral(Abandoned)}"),
                (
                    dialect.Name("CK_FencingLeases_EndedAt"),
                    $"({State} = {active} AND {EndedAt} IS NULL) OR ({State} <> {active} AND {EndedAt} IS NOT NULL)"
                ),
                (dialect.Name("CK_FencingLeases_TakeoverCount"), $"{TakeoverCount} >= 0"),
                (
                    dialect.Name("CK_FencingLeases_Progress"),
                    $"({Progress} IS NULL AND {ProgressContract} IS NULL) OR ({Progress} IS NOT NULL AND {ProgressContract} IS NOT NULL)"
                ),
            ],
            [
                new(
                    dialect.Name("IX_FencingLeases_ActiveExpiry"),
                    [Kind, ExpiresAt, TenantId, Resource],
                    $"{State} = {active}"
                ),
                new(dialect.Name("IX_FencingLeases_Ended"), [Kind, EndedAt], $"{State} <> {active}"),
            ]
        );

        return new SqlSchemaScript(Schema, "LockResource", [SequenceName], [table]);
    }
}
