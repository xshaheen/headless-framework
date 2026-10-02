// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Headless.Sql;
using Headless.Testing.Tests;

namespace Tests;

public sealed class SqlFencedTests : TestBase
{
    [Fact]
    public async Task should_report_an_applied_transition_with_the_row_it_found_and_what_it_wrote()
    {
        using var results = _Batch(row: ["before"], decision: [true, "after"]);

        var fenced = await _ExecuteAsync(results, lockedRead: true);

        fenced.IsAccepted.Should().BeTrue();
        fenced.Match((written, before) => $"{before!.Value}->{written}", _ => "rejected").Should().Be("before->after");
    }

    [Fact]
    public async Task should_report_a_refusal_with_the_row_the_fence_found()
    {
        using var results = _Batch(row: ["held"], decision: [false, DBNull.Value]);

        var fenced = await _ExecuteAsync(results, lockedRead: true);

        fenced.IsAccepted.Should().BeFalse();
        fenced.Match((_, _) => "accepted", before => $"rejected:{before!.Value}").Should().Be("rejected:held");
    }

    [Fact]
    public async Task should_report_no_row_when_the_locked_read_found_none()
    {
        using var results = _Batch(row: null, decision: [false, DBNull.Value]);

        var fenced = await _ExecuteAsync(results, lockedRead: true);

        fenced.Before.Should().BeNull();
    }

    [Fact]
    public async Task should_read_only_the_decision_when_there_is_no_locked_read()
    {
        using var results = new System.Data.DataSet();
        var decided = results.Tables.Add("decision");
        decided.Columns.Add("applied", typeof(bool));
        decided.Columns.Add("value", typeof(string));
        decided.Rows.Add(true, "inserted");

        var fenced = await _ExecuteAsync(results, lockedRead: false);

        fenced.Match((written, before) => $"{before is null}:{written}", _ => "rejected").Should().Be("True:inserted");
    }

    [Fact]
    public async Task should_refuse_a_statement_that_returned_no_decision()
    {
        using var results = new System.Data.DataSet();
        results.Tables.Add("decision").Columns.Add("applied", typeof(bool));

        var act = async () => await _ExecuteAsync(results, lockedRead: false);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no decision row*");
    }

    private static async Task<SqlFenced<Row, string>> _ExecuteAsync(System.Data.DataSet results, bool lockedRead)
    {
        await using var command = new FakeCommand(results);

        var fenced = await SqlFencedCommand.ExecuteAsync(
            command,
            lockedRead,
            static (r, _) => ValueTask.FromResult(new Row(r.GetString(0))),
            static (r, _) => ValueTask.FromResult(r.GetString(1)),
            AbortToken
        );

        command.Executions.Should().Be(1);

        return fenced;
    }

    private static System.Data.DataSet _Batch(object[]? row, object[] decision)
    {
        var results = new System.Data.DataSet();
        var locked = results.Tables.Add("locked");
        locked.Columns.Add("value", typeof(string));

        if (row is not null)
        {
            locked.Rows.Add(row);
        }

        var decided = results.Tables.Add("decision");
        decided.Columns.Add("applied", typeof(bool));
        decided.Columns.Add("value", typeof(string));
        decided.Rows.Add(decision);

        return results;
    }

    private sealed record Row(string Value);

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
