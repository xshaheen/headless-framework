// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Sql;

namespace Headless.Fencing;

/// <summary>
/// The lease table, sequence, and column names the store's statements use, named by the dialect: snake_case on
/// PostgreSQL, PascalCase on SQL Server. They are the names each provider's schema contribution creates.
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

    public static string StateLiteral(short state) => state.ToString(CultureInfo.InvariantCulture);
}
