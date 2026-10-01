// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Headless.Sql;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlUpsertCommandTests : TestBase
{
    [Theory]
    [InlineData(SqlUpsertOutcome.Inserted)]
    [InlineData(SqlUpsertOutcome.Updated)]
    public async Task should_report_what_an_applied_upsert_wrote(SqlUpsertOutcome outcome)
    {
        using var results = _Decision((short)outcome, 7L);

        var upserted = await _ExecuteAsync(results);

        upserted.Outcome.Should().Be(outcome);
        upserted.Match((value, how) => $"{how}:{value}", () => "refused").Should().Be($"{outcome}:7");
    }

    [Fact]
    public async Task should_report_a_refusal_without_reading_the_written_columns()
    {
        using var results = _Decision((short)SqlUpsertOutcome.Refused, DBNull.Value);

        var upserted = await _ExecuteAsync(results);

        upserted.Match((_, _) => "applied", () => "refused").Should().Be("refused");
    }

    [Theory]
    [InlineData(SqlUpsertOutcome.Updated, 0)]
    [InlineData(SqlUpsertOutcome.Inserted, 1)]
    [InlineData(SqlUpsertOutcome.Refused, 2)]
    public async Task should_read_the_first_row_of_any_result_set_when_each_branch_reports_its_own(
        SqlUpsertOutcome outcome,
        int resultSet
    )
    {
        // SQLite's shape: update, insert, and refusal each return a result set, and only one of them has a row.
        using var results = new System.Data.DataSet();

        for (var i = 0; i < 3; i++)
        {
            var table = results.Tables.Add($"branch{i}");
            table.Columns.Add("outcome", typeof(short));
            table.Columns.Add("value", typeof(long));

            if (i == resultSet)
            {
                table.Rows.Add((short)outcome, 7L);
            }
        }

        var upserted = await _ExecuteAsync(results);

        upserted.Outcome.Should().Be(outcome);
    }

    [Fact]
    public async Task should_refuse_a_statement_that_returned_no_decision()
    {
        using var results = new System.Data.DataSet();
        var table = results.Tables.Add("decision");
        table.Columns.Add("outcome", typeof(short));
        table.Columns.Add("value", typeof(long));

        var act = async () => await _ExecuteAsync(results);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no decision row*");
    }

    private static async Task<SqlUpserted<long>> _ExecuteAsync(System.Data.DataSet results)
    {
        await using var command = new FakeCommand(results);

        return await SqlUpsertCommand.ExecuteAsync(
            command,
            static (r, _) => ValueTask.FromResult(r.GetInt64(1)),
            AbortToken
        );
    }

    private static System.Data.DataSet _Decision(short outcome, object value)
    {
        var results = new System.Data.DataSet();
        var table = results.Tables.Add("decision");
        table.Columns.Add("outcome", typeof(short));
        table.Columns.Add("value", typeof(long));
        table.Rows.Add(outcome, value);

        return results;
    }

    /// <summary>Returns the tables of <paramref name="results" /> as the batch's result sets, in order.</summary>
    private sealed class FakeCommand(System.Data.DataSet results) : DbCommand
    {
        public int Executions { get; private set; }

        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();

        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }

        public override int ExecuteNonQuery() => throw new NotSupportedException();

        public override object ExecuteScalar() => throw new NotSupportedException();

        public override void Prepare() { }

        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            Executions++;

            return results.CreateDataReader();
        }
    }
}
