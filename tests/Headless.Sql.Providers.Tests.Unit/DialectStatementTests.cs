// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sql;
using Headless.Sql.PostgreSql;
using Headless.Sql.SqlServer;
using Headless.Testing.Tests;

namespace Tests;

/// <summary>
/// Invariants every dialect's rendered statements keep, checked for both engines: the clock token is always replaced,
/// no statement catches errors, and each shape carries its engine's lock or skip-locked hint.
/// </summary>
public sealed class DialectStatementTests : TestBase
{
    public static TheoryData<string> Dialects => ["PostgreSQL", "SQL Server"];

    [Theory]
    [MemberData(nameof(Dialects))]
    public void should_replace_the_clock_token_and_never_catch_in_any_statement(string name)
    {
        var dialect = _Dialect(name);

        foreach (var sql in _Statements(dialect))
        {
            sql.Should().NotContain(SqlDialectTokens.Now);
            sql.Should().NotContain(SqlDialectTokens.Stored);
            sql.Should().NotContainEquivalentOf("TRY", "a caught error could still doom a caller's transaction");
            sql.Should().NotContainEquivalentOf("SET XACT_ABORT");
        }
    }

    [Fact]
    public void should_lock_rows_for_update_intent_and_skip_locked_rows_on_postgresql()
    {
        var dialect = PostgreSqlDialect.Instance;

        dialect.Render(_LockedRead(dialect)).Should().Contain("FOR NO KEY UPDATE");
        dialect.Render(_Claim(dialect)).Should().Contain("SKIP LOCKED");
        dialect.Render(_Delete(dialect)).Should().Contain("SKIP LOCKED");
        dialect.Render(_Insert(dialect)).Should().Contain("ON CONFLICT").And.Contain("DO NOTHING");
        dialect
            .Render(_Transition(dialect, set: "\"v\" = 1"))
            .Should()
            .Contain("clock_timestamp()")
            .And.NotContain("now()");
    }

    [Fact]
    public void should_lock_rows_and_key_ranges_and_read_past_locks_under_any_isolation_on_sql_server()
    {
        var dialect = SqlServerDialect.Instance;

        dialect.Render(_LockedRead(dialect)).Should().Contain("UPDLOCK, HOLDLOCK, ROWLOCK");
        dialect.Render(_Insert(dialect)).Should().Contain("UPDLOCK, HOLDLOCK");
        dialect.Render(_Claim(dialect)).Should().Contain("READPAST").And.Contain("READCOMMITTEDLOCK");
        dialect.Render(_Delete(dialect)).Should().Contain("READPAST").And.Contain("READCOMMITTEDLOCK");
    }

    [Fact]
    public void should_read_the_sql_server_clock_into_a_variable_after_the_locking_read()
    {
        // SYSUTCDATETIME() inline would be evaluated when the statement starts, before its lock wait.
        var dialect = SqlServerDialect.Instance;
        var sql = dialect.Render(_LockedRead(dialect)) + dialect.Render(_Transition(dialect, set: "[v] = 1"));

        sql.IndexOf("SYSUTCDATETIME()", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(sql.IndexOf("UPDLOCK", StringComparison.Ordinal));
        sql.Should().Contain("WHERE [k] = @K AND ([v] > @now)");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void should_evaluate_the_fence_without_writing_when_there_is_no_assignment(string name)
    {
        var dialect = _Dialect(name);

        dialect.Render(_Transition(dialect, set: null)).Should().NotContainEquivalentOf("UPDATE ");
    }

    private static IEnumerable<string> _Statements(ISqlDialect dialect)
    {
        yield return dialect.Render(_LockedRead(dialect));
        yield return dialect.Render(_Transition(dialect, set: $"{dialect.Quote("v")} = {SqlDialectTokens.Now}"));
        yield return dialect.Render(_Transition(dialect, set: null));
        yield return dialect.Render(_Insert(dialect));
        yield return dialect.Render(_Claim(dialect));
        yield return dialect.Render(_Delete(dialect));
        yield return dialect.Render(_Claim(dialect) with { BatchSizeParameter = "Batch" });
        yield return dialect.Render(
            _Upsert(dialect, guard: $"{SqlDialectTokens.Stored}.{dialect.Quote("v")} < @Limit")
        );
        yield return dialect.Render(_Upsert(dialect, guard: null));
        yield return dialect.Render(
            new SqlClockedStatement(
                $"DELETE FROM {dialect.Qualify("s", "t")} WHERE {dialect.Quote("v")} < {SqlDialectTokens.Now}"
            )
        );
    }

    [Fact]
    public void should_upsert_with_on_conflict_and_bound_claims_by_the_batch_parameter_on_postgresql()
    {
        var dialect = PostgreSqlDialect.Instance;

        dialect.Render(_Upsert(dialect, guard: "stored.\"v\" < 1")).Should().Contain("ON CONFLICT (\"k\") DO UPDATE");
        dialect.Render(_Claim(dialect) with { BatchSizeParameter = "Batch" }).Should().Contain("LIMIT @Batch");
        dialect.Render(_Claim(dialect)).Should().Contain("LIMIT 1");
    }

    [Fact]
    public void should_lock_the_key_before_reading_the_clock_in_an_upsert_on_sql_server()
    {
        var dialect = SqlServerDialect.Instance;
        var sql = dialect.Render(_Upsert(dialect, guard: null));

        sql.IndexOf("SYSUTCDATETIME()", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(sql.IndexOf("UPDLOCK, HOLDLOCK, ROWLOCK", StringComparison.Ordinal));
        dialect.Render(_Claim(dialect) with { BatchSizeParameter = "Batch" }).Should().Contain("TOP (@Batch)");
    }

    [Fact]
    public void should_compare_with_a_list_through_any_on_postgresql_and_typed_openjson_on_sql_server()
    {
        PostgreSqlDialect.Instance.InList("\"k\"", "Keys", SqlColumnType.KeyText(64)).Should().Be("\"k\" = ANY(@Keys)");
        SqlServerDialect
            .Instance.InList("[k]", "Keys", SqlColumnType.KeyText(64))
            .Should()
            .Be("[k] IN (SELECT [value] FROM OPENJSON(@Keys) WITH ([value] nvarchar(64) '$'))");
        SqlServerDialect.Instance.InList("[k]", "Ids", SqlColumnType.Guid).Should().Contain("uniqueidentifier");
        SqlServerDialect.Instance.InList("[k]", "Texts", SqlColumnType.Text(0)).Should().Contain("nvarchar(max)");
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public void should_refuse_a_binary_list(string name)
    {
        var dialect = _Dialect(name);

        var act = () => dialect.InList("x", "Values", SqlColumnType.Binary);

        act.Should().Throw<ArgumentException>();
    }

    private static SqlUpsert _Upsert(ISqlDialect d, string? guard) =>
        new(
            d.Qualify("s", "t"),
            [new(d.Quote("k"), "K")],
            [d.Quote("v")],
            [SqlDialectTokens.Now],
            $"{d.Quote("v")} = {SqlDialectTokens.Stored}.{d.Quote("v")} + 1",
            guard,
            [d.Quote("v")]
        );

    private static SqlLockedRead _LockedRead(ISqlDialect d) =>
        new(d.Qualify("s", "t"), [new(d.Quote("k"), "K")], [d.Quote("v")]);

    private static SqlFencedTransition _Transition(ISqlDialect d, string? set) =>
        new(
            d.Qualify("s", "t"),
            [new(d.Quote("k"), "K")],
            $"{d.Quote("v")} > {SqlDialectTokens.Now}",
            set,
            [d.Quote("v")]
        );

    private static SqlInsertIfAbsent _Insert(ISqlDialect d) =>
        new(d.Qualify("s", "t"), [new(d.Quote("k"), "K")], [d.Quote("v")], [SqlDialectTokens.Now], [d.Quote("v")]);

    private static SqlClaimNext _Claim(ISqlDialect d) =>
        new(
            d.Qualify("s", "t"),
            [d.Quote("k")],
            $"{d.Quote("v")} <= {SqlDialectTokens.Now}",
            [d.Quote("v")],
            $"{d.Quote("v")} = {SqlDialectTokens.Now}",
            [d.Quote("k")]
        );

    private static SqlDeleteBatch _Delete(ISqlDialect d) =>
        new(
            d.Qualify("s", "t"),
            [d.Quote("k")],
            $"{d.Quote("v")} <= {d.ShiftByDuration(SqlDialectTokens.Now, "Age", subtract: true)}",
            "BatchSize"
        );

    private static ISqlDialect _Dialect(string name) =>
        string.Equals(name, "PostgreSQL", StringComparison.Ordinal)
            ? PostgreSqlDialect.Instance
            : SqlServerDialect.Instance;
}
