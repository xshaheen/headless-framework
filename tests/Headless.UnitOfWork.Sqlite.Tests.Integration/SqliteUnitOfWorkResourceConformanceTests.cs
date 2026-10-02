// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>Runs the resource-backed conformance suite against a real SQLite transaction.</summary>
[Collection<SqliteUnitOfWorkFixture>]
public sealed class SqliteUnitOfWorkResourceConformanceTests(SqliteUnitOfWorkFixture sqlite)
    : UnitOfWorkResourceConformanceTests<SqliteUnitOfWorkFixture>(sqlite)
{
    private readonly SqliteUnitOfWorkFixture _fixture = sqlite;

    /// <summary>
    /// SQLite admits one write transaction per database file and an owned unit begins <c>IMMEDIATE</c>, so the second
    /// unit cannot begin while the first is open: it waits at its begin. The two units are then still independent:
    /// each keeps its own registrations and outcome.
    /// </summary>
    [Fact]
    public override async Task should_keep_two_owned_units_independent_when_each_commits_its_own_row()
    {
        await _fixture.ResetAsync(AbortToken);
        await using var session = _fixture.CreateSession();
        var order = new List<string>();
        await using var first = await _fixture.BeginOwnedAsync(session.Factory, AbortToken);
        first.UnitOfWork.OnCompleted(() =>
        {
            order.Add("first");

            return ValueTask.CompletedTask;
        });
        await _fixture.InsertProbeRowAsync(first.UnitOfWork, "first-row", AbortToken);

        var beginSecond = Task.Run(() => _fixture.BeginOwnedAsync(session.Factory, AbortToken).AsTask(), AbortToken);
        var waited = await Task.WhenAny(beginSecond, Task.Delay(TimeSpan.FromMilliseconds(750), AbortToken));

        waited.Should().NotBeSameAs(beginSecond, "the second unit waits for the database write lock the first holds");

        await first.UnitOfWork.RollbackAsync();
        await using var second = await beginSecond.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
        second.UnitOfWork.OnCompleted(() =>
        {
            order.Add("second");

            return ValueTask.CompletedTask;
        });
        await _fixture.InsertProbeRowAsync(second.UnitOfWork, "second-row", AbortToken);
        await second.UnitOfWork.CompleteAsync(AbortToken);

        order.Should().Equal(["second"], "the rolled-back unit never drains its completions");
        (await _fixture.CountProbeRowsAsync(AbortToken)).Should().Be(1, "only the completed unit's row is durable");
    }
}
