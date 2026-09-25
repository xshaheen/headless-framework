// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Sequences;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.SqlClient;

namespace Tests;

/// <summary>
/// SQL Server locking behavior the shared suite cannot express: first use of a key is serialized by a key-range
/// lock on the clustered primary key rather than by catching a duplicate-key error, so it holds under
/// <c>XACT_ABORT ON</c> and never surfaces an error the caller's transaction cannot survive.
/// </summary>
[Collection<SqlServerSequencesFixture>]
public sealed class SqlServerSequencesLockingTests(SqlServerSequencesFixture fixture) : TestBase
{
    [Fact]
    public async Task should_number_one_to_twenty_when_gap_free_units_race_on_a_new_key_under_xact_abort()
    {
        // Gap-free calls never retry, so a duplicate-key error (2627/2601) or a deadlock (1205) on first use would
        // fail a unit here, where the fast path's deadlock retry could hide it. XACT_ABORT ON makes any such error
        // doom the unit's transaction rather than just the statement.
        var name = $"race-{Guid.NewGuid():N}";
        await using var host = await _CreateGapFreeHostAsync(name);

        var values = await Task.WhenAll(
            Enumerable
                .Range(0, 20)
                .Select(_ =>
                    Task.Run(
                        async () =>
                        {
                            await using var connection = await _OpenWithXactAbortAsync();
                            await using var unit = await host.Factory.BeginAsync(
                                connection,
                                cancellationToken: AbortToken
                            );
                            var value = await unit.Sequences.NextAsync(name, cancellationToken: AbortToken);
                            await unit.CompleteAsync(AbortToken);

                            return value;
                        },
                        AbortToken
                    )
                )
        );

        values.Should().BeEquivalentTo(Enumerable.Range(1, 20).Select(static x => (long)x));
        (await fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(20);
    }

    [Fact]
    public async Task should_commit_a_gap_free_number_taken_on_a_new_key_under_xact_abort()
    {
        var name = $"xact-{Guid.NewGuid():N}";
        await using var host = await _CreateGapFreeHostAsync(name);
        await using var connection = await _OpenWithXactAbortAsync();
        await using var unit = await host.Factory.BeginAsync(connection, cancellationToken: AbortToken);

        (await unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(1);
        (await unit.Sequences.NextAsync(name, cancellationToken: AbortToken)).Should().Be(2);
        await unit.CompleteAsync(AbortToken);

        unit.State.Should().Be(UnitOfWorkState.Completed);
        (await fixture.ReadValueAsync(new SequenceKey("", name, ""), AbortToken)).Should().Be(2);
    }

    [Fact]
    public async Task should_hold_first_use_of_a_new_key_in_the_same_gap_until_a_gap_free_unit_commits()
    {
        // Both keys belong to a tenant no other row uses, so no existing key sorts between them: the fast call's key
        // falls in the gap the gap-free unit range-locked when it created its own key.
        var tenant = $"gap-{Guid.NewGuid():N}";
        const string gapFreeName = "a";
        const string fastName = "b";
        await using var host = await _CreateGapFreeHostAsync(gapFreeName);

        using (host.CurrentTenant.Change(tenant))
        {
            await using var unit = await fixture.BeginUnitAsync(host, AbortToken);
            (await unit.Unit.Sequences.NextAsync(gapFreeName, cancellationToken: AbortToken)).Should().Be(1);

            var pending = Task.Run(
                async () => await host.Generator.NextAsync(fastName, cancellationToken: AbortToken),
                AbortToken
            );
            var first = await Task.WhenAny(
                pending,
                Task.Delay(SequencesFixtureExtensions.BlockedObservationWindow, AbortToken)
            );

            first.Should().NotBeSameAs(pending, "first use of a key in a range-locked gap waits for the lock holder");

            await unit.CommitAsync(AbortToken);

            (await pending.WaitAsync(SequencesFixtureExtensions.ReleaseTimeout, AbortToken)).Should().Be(1);
        }

        (await fixture.ReadValueAsync(new SequenceKey(tenant, gapFreeName, ""), AbortToken)).Should().Be(1);
        (await fixture.ReadValueAsync(new SequenceKey(tenant, fastName, ""), AbortToken)).Should().Be(1);
    }

    private ValueTask<SequencesHost> _CreateGapFreeHostAsync(string name)
    {
        return fixture.CreateHostAsync(
            setup => setup.Policy(name, new SequencePolicy { Mode = SequenceMode.GapFree }),
            AbortToken
        );
    }

    private async Task<SqlConnection> _OpenWithXactAbortAsync()
    {
        var connection = new SqlConnection(fixture.CountersConnectionString);

        try
        {
            await connection.OpenAsync(AbortToken);
            await using var command = new SqlCommand("SET XACT_ABORT ON; SELECT @@OPTIONS & 16384;", connection);
            var xactAbort = Convert.ToInt32(await command.ExecuteScalarAsync(AbortToken), CultureInfo.InvariantCulture);
            xactAbort.Should().NotBe(0, "the session must run with XACT_ABORT ON for the scenario to mean anything");

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }
    }
}
