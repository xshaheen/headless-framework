// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Text;
using Headless.Sql;
using Headless.Sql.PostgreSql;

namespace Tests;

/// <summary>Runs the claimable-index plan suite against PostgreSQL.</summary>
[Collection<PostgreSqlJobsCoordinationFixture>]
public sealed class PostgreSqlClaimIndexTests(PostgreSqlJobsCoordinationFixture fixture)
    : JobsClaimIndexConformanceTests<PostgreSqlJobsCoordinationFixture>(fixture)
{
    protected override ISqlDialect Dialect => PostgreSqlDialect.Instance;

    [Fact]
    public override Task time_job_peek_reads_the_claimable_index() => base.time_job_peek_reads_the_claimable_index();

    [Fact]
    public override Task native_fallback_claim_reads_the_claimable_index() =>
        base.native_fallback_claim_reads_the_claimable_index();

    protected override async Task AnalyzeAsync(DbConnection connection, string qualifiedTable, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"ANALYZE {qualifiedTable};";
        await command.ExecuteNonQueryAsync(ct);
    }

    protected override async Task<string> ExplainAsync(DbCommand command, CancellationToken ct)
    {
        command.CommandText = "EXPLAIN " + command.CommandText;
        await using var reader = await command.ExecuteReaderAsync(ct);
        var plan = new StringBuilder();
        while (await reader.ReadAsync(ct))
        {
            plan.AppendLine(reader.GetString(0));
        }

        return plan.ToString();
    }
}
