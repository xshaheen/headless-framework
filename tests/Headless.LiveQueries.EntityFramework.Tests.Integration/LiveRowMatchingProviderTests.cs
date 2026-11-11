// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Row matching against a real server: the provider names and sends query parameters its own way, generates keys
/// its own way, and two contexts are two connections, so one can change a row behind the other. Each test owns a
/// host and tags its rows, and every watch filters on the tag.
/// </summary>
public abstract class LiveRowMatchingProviderTests(ILiveDatabase database) : TestBase
{
    private readonly Guid _test = Guid.NewGuid();

    [Fact]
    public async Task should_re_run_only_the_watch_of_the_owner_a_parcel_was_added_for()
    {
        // given - a watch per owner, filtered on a captured owner and on this test's tag
        await using var host = new LiveIntegrationHost(database, rowLevel: true);
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner2 = _WatchOwner(host, 2);
        await using var owner3 = _WatchOwner(host, 3);

        foreach (var watch in new[] { owner1, owner2, owner3 })
        {
            (await watch.NextAsync()).Should().BeEmpty();
        }

        // when
        await host.WriteAsync(async db =>
        {
            db.Parcels.Add(_Parcel(owner: 3, "for three"));
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(2);
        (await owner3.NextAsync()).Should().Equal("for three:1");
        owner1.Runs.Should().Be(1);
        owner2.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_re_run_a_watch_filtered_by_a_generated_key_only_for_that_row()
    {
        // given - two stored rows, and a watch on the key the database gave the first
        await using var host = new LiveIntegrationHost(database, rowLevel: true);
        var first = _Parcel(owner: 1, "first");
        var second = _Parcel(owner: 1, "second");
        await host.WriteAsync(async db =>
        {
            db.Parcels.AddRange(first, second);
            await db.SaveChangesAsync(AbortToken);
        });
        var firstId = first.Id;
        await using var watch = host.Watch(
            (db, cancellationToken) =>
                db
                    .Parcels.Where(parcel => parcel.Id == firstId)
                    .Select(parcel => parcel.Label)
                    .ToListAsync(cancellationToken),
            AbortToken
        );
        (await watch.NextAsync()).Should().Equal("first");

        // when - the other row is updated and then deleted
        await host.WriteAsync(async db =>
        {
            var other = await db.Parcels.SingleAsync(parcel => parcel.Id == second.Id, AbortToken);
            other.Label = "second, relabelled";
            await db.SaveChangesAsync(AbortToken);
            db.Parcels.Remove(other);
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(0);

        // when - the watched row is updated, then deleted through an instance that carries only its key
        await host.WriteAsync(async db =>
        {
            (await db.Parcels.SingleAsync(parcel => parcel.Id == firstId, AbortToken)).Label = "first, relabelled";
            await db.SaveChangesAsync(AbortToken);
        });
        host.Wakes.Should().Be(1);
        (await watch.NextAsync()).Should().Equal("first, relabelled");

        await host.WriteAsync(async db =>
        {
            db.Parcels.Remove(new Parcel { Id = firstId, Label = "" });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(2);
        (await watch.NextAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task should_re_run_the_watch_that_now_holds_a_row_another_connection_moved_after_this_one_loaded_it()
    {
        // given - a row outside the watch's filter on a changeable column, loaded by a first connection
        await using var host = new LiveIntegrationHost(database, rowLevel: true);
        var stored = _Parcel(owner: 1, "light");
        await host.WriteAsync(async db =>
        {
            db.Parcels.Add(stored);
            await db.SaveChangesAsync(AbortToken);
        });
        await using var heavy = host.Watch(
            (db, cancellationToken) =>
                db
                    .Parcels.Where(parcel => parcel.Test == _test && parcel.Weight == 5)
                    .Select(parcel => parcel.Label)
                    .ToListAsync(cancellationToken),
            AbortToken
        );
        (await heavy.NextAsync()).Should().BeEmpty();

        await using var scope = host.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();
        var loaded = await first.Parcels.SingleAsync(parcel => parcel.Id == stored.Id, AbortToken);

        // when - a second connection moves the row into the filter
        await host.WriteAsync(async db =>
        {
            (await db.Parcels.SingleAsync(parcel => parcel.Id == stored.Id, AbortToken)).Weight = 5;
            await db.SaveChangesAsync(AbortToken);
        });
        (await heavy.NextAsync()).Should().Equal("light");

        // and the first connection, which still holds the old weight as both the old and the new value, relabels it
        loaded.Label = "heavy";
        await first.SaveChangesAsync(AbortToken);

        // then
        host.Wakes.Should().Be(2);
        (await heavy.NextAsync()).Should().Equal("heavy");
    }

    [Fact]
    public async Task should_announce_the_rows_of_a_unit_of_work_after_its_commit_and_none_after_a_rollback()
    {
        // given
        await using var host = new LiveIntegrationHost(database, rowLevel: true);
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner2 = _WatchOwner(host, 2);
        (await owner1.NextAsync()).Should().BeEmpty();
        (await owner2.NextAsync()).Should().BeEmpty();

        // when - a unit saves a parcel for owner 1 and rolls back
        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();
            await using var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);
            db.Parcels.Add(_Parcel(owner: 1, "rolled back"));
            await db.SaveChangesAsync(AbortToken);
            await unit.RollbackAsync();
        }

        // then
        host.Wakes.Should().Be(0);

        // when - a unit saves two parcels for owner 2 in two saves and commits
        long wakesBeforeCommit;

        await using (var scope = host.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LiveOrdersDb>();
            await using var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);
            db.Parcels.Add(_Parcel(owner: 2, "first save"));
            await db.SaveChangesAsync(AbortToken);
            db.Parcels.Add(_Parcel(owner: 2, "second save"));
            await db.SaveChangesAsync(AbortToken);
            wakesBeforeCommit = host.Wakes;
            await unit.CompleteAsync(AbortToken);
        }

        // then - only the second owner's watch was signalled, once, after the commit
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await owner2.NextAsync()).Should().Equal("first save:1", "second save:1");
        owner1.Runs.Should().Be(1);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public async Task should_deliver_every_changed_result_of_a_generated_mix_of_writes_with_fewer_signals_than_by_table(
        int seed
    )
    {
        // given - watches on the owner, on a changeable column, and on the whole of this test's rows
        await using var host = new LiveIntegrationHost(database, rowLevel: true);
        var queries = _Queries();
        List<(Watch<string> Watch, string Delivered)> watches = [];

        try
        {
            foreach (var query in queries)
            {
                var watch = host.Watch(query, AbortToken);
                watches.Add((watch, await watch.NextAsync()));
            }

            var random = new Random(seed);
            List<int> ids = [];
            const int steps = 30;

            for (var step = 0; step < steps; step++)
            {
                // when - one committed write
                var kind = ids.Count == 0 ? 0 : random.Next(5);
                var owner = random.Next(3);
                var target = ids.Count == 0 ? 0 : ids[random.Next(ids.Count)];
                await host.WriteAsync(db => _ApplyAsync(db, kind, owner, target, step, ids));

                // then - every watch whose result the write changed delivers the new result
                for (var index = 0; index < queries.Length; index++)
                {
                    var expected = "";
                    await host.WriteAsync(async db => expected = await queries[index](db, AbortToken));
                    watches[index] = await _ShouldReachAsync(watches[index], expected, $"step {step}, watch {index}");
                }
            }

            // and fewer signals were needed than matching by table makes, which signals every watch for every write
            host.Wakes.Should().BeLessThan(steps * queries.Length);
            host.RowsRuledOut.Should().BePositive();
            host.Fallbacks.Should().Be(0);
        }
        finally
        {
            foreach (var (watch, _) in watches)
            {
                await watch.DisposeAsync();
            }
        }
    }

    // One committed write per call.
    private async Task _ApplyAsync(LiveOrdersDb db, int kind, int owner, int target, int step, List<int> ids)
    {
        switch (kind)
        {
            case 0 or 1:
                var parcel = _Parcel(owner, $"s{step}");
                db.Parcels.Add(parcel);
                await db.SaveChangesAsync(AbortToken);
                ids.Add(parcel.Id);

                break;
            case 2:
                (await db.Parcels.SingleAsync(parcel => parcel.Id == target, AbortToken)).Weight = step + 10;
                await db.SaveChangesAsync(AbortToken);

                break;
            case 3:
                // Through an instance that carries only the key; the owner it names is a guess.
                var stub = new Parcel
                {
                    Id = target,
                    Test = _test,
                    OwnerId = owner,
                    Label = "",
                };
                db.Parcels.Attach(stub);
                stub.Label = $"u{step}";
                await db.SaveChangesAsync(AbortToken);

                break;
            default:
                await db
                    .Parcels.Where(parcel => parcel.Test == _test && parcel.OwnerId == owner)
                    .ExecuteUpdateAsync(parcels => parcels.SetProperty(parcel => parcel.Weight, 2), AbortToken);

                break;
        }
    }

    private static async Task<(Watch<string> Watch, string Delivered)> _ShouldReachAsync(
        (Watch<string> Watch, string Delivered) watch,
        string expected,
        string because
    )
    {
        if (string.Equals(watch.Delivered, expected, StringComparison.Ordinal))
        {
            return watch;
        }

        // A watch that was not signalled for a write that changed its result never delivers, and fails here.
        var delivered = await watch.Watch.NextAsync();
        delivered.Should().Be(expected, because);

        return (watch.Watch, delivered);
    }

    private Func<LiveOrdersDb, CancellationToken, Task<string>>[] _Queries()
    {
        var test = _test;

        return
        [
            (db, cancellationToken) =>
                _ListAsync(db.Parcels.Where(x => x.Test == test && x.OwnerId == 0), cancellationToken),
            (db, cancellationToken) =>
                _ListAsync(db.Parcels.Where(x => x.Test == test && x.OwnerId == 1), cancellationToken),
            (db, cancellationToken) =>
                _ListAsync(db.Parcels.Where(x => x.Test == test && x.OwnerId == 2), cancellationToken),
            (db, cancellationToken) =>
                _ListAsync(db.Parcels.Where(x => x.Test == test && x.Weight == 2), cancellationToken),
            (db, cancellationToken) => _ListAsync(db.Parcels.Where(x => x.Test == test), cancellationToken),
        ];
    }

    private static async Task<string> _ListAsync(IQueryable<Parcel> parcels, CancellationToken cancellationToken)
    {
        var rows = await parcels
            .OrderBy(parcel => parcel.Id)
            .Select(parcel => new { parcel.Label, parcel.Weight })
            .ToListAsync(cancellationToken);

        return string.Join(';', rows.Select(row => $"{row.Label}:{row.Weight}")) + "|";
    }

    private Parcel _Parcel(int owner, string label)
    {
        return new Parcel
        {
            Test = _test,
            OwnerId = owner,
            Weight = 1,
            Label = label,
        };
    }

    private Watch<List<string>> _WatchOwner(LiveIntegrationHost host, int owner)
    {
        return host.Watch(
            async (db, cancellationToken) =>
                (
                    await db
                        .Parcels.Where(parcel => parcel.Test == _test && parcel.OwnerId == owner)
                        .OrderBy(parcel => parcel.Id)
                        .Select(parcel => new { parcel.Label, parcel.Weight })
                        .ToListAsync(cancellationToken)
                ).ConvertAll(parcel => $"{parcel.Label}:{parcel.Weight}"),
            AbortToken
        );
    }
}

[Collection<PostgreSqlLiveFixture>]
public sealed class PostgreSqlLiveRowMatchingTests(PostgreSqlLiveFixture fixture)
    : LiveRowMatchingProviderTests(fixture);

[Collection<SqlServerLiveFixture>]
public sealed class SqlServerLiveRowMatchingTests(SqlServerLiveFixture fixture) : LiveRowMatchingProviderTests(fixture);
