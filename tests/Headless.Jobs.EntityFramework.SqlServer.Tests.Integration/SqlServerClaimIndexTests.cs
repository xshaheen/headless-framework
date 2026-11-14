// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Text;
using Headless.Sql;
using Headless.Sql.SqlServer;

namespace Tests;

/// <summary>Runs the claimable-index plan suite against SQL Server.</summary>
[Collection<SqlServerJobsCoordinationFixture>]
public sealed class SqlServerClaimIndexTests(SqlServerJobsCoordinationFixture fixture)
    : JobsClaimIndexConformanceTests<SqlServerJobsCoordinationFixture>(fixture)
{
    protected override ISqlDialect Dialect => SqlServerDialect.Instance;

    [Fact]
    public override Task time_job_peek_reads_the_claimable_index() => base.time_job_peek_reads_the_claimable_index();

    [Fact]
    public override Task native_fallback_claim_reads_the_claimable_index() =>
        base.native_fallback_claim_reads_the_claimable_index();

    protected override async Task AnalyzeAsync(DbConnection connection, string qualifiedTable, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE STATISTICS {qualifiedTable} WITH FULLSCAN;";
        await command.ExecuteNonQueryAsync(ct);
    }

    protected override async Task<string> ExplainAsync(DbCommand command, CancellationToken ct)
    {
        // STATISTICS XML runs the statement and appends its actual plan as an extra result set; the transaction is
        // rolled back so a claim statement leaves no trace.
        var connection = command.Connection!;
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var on = connection.CreateCommand())
        {
            on.Transaction = transaction;
            on.CommandText = "SET STATISTICS XML ON;";
            await on.ExecuteNonQueryAsync(ct);
        }

        command.Transaction = transaction;
        var plans = new StringBuilder();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            do
            {
                while (await reader.ReadAsync(ct))
                {
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        if (
                            reader.GetValue(i) is string text
                            && text.StartsWith("<ShowPlanXML", StringComparison.Ordinal)
                        )
                        {
                            plans.AppendLine(text);
                        }
                    }
                }
            } while (await reader.NextResultAsync(ct));
        }

        await transaction.RollbackAsync(ct);
        return plans.ToString();
    }
}
