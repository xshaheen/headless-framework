// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Runs one generated sequence of writes against two hosts, one matching by row and one by table, with the same
/// watches open on both. Matching by table is the reference: it signals every watch that read a written table.
/// </summary>
public sealed class LiveRowMatchingEquivalenceTests : TestBase
{
    private const int _Steps = 80;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task should_deliver_every_result_table_matching_delivers_and_signal_fewer_watches(int seed)
    {
        // given - the same watches on a host that matches by row and on one that matches by table
        await using var byRow = await LiveEfTestHost.CreateRowLevelAsync();
        await using var byTable = await LiveEfTestHost.CreateAsync();
        var rowWatches = await _OpenAsync(byRow);
        var tableWatches = await _OpenAsync(byTable);
        var random = new Random(seed);

        try
        {
            for (var step = 0; step < _Steps; step++)
            {
                // when - the same write runs on both hosts
                var write = await _NextWriteAsync(byRow, random, step);
                await _ApplyAsync(byRow, write);
                await _ApplyAsync(byTable, write);

                // then - every watch whose result the write changed delivers the new result, on both hosts
                for (var index = 0; index < _Queries.Length; index++)
                {
                    var expected = await _ReadAsync(byRow, _Queries[index]);
                    (await _ReadAsync(byTable, _Queries[index])).Should().Be(expected);
                    await rowWatches[index].ShouldReachAsync(expected, $"step {step} ({write}), watch {index}, by row");
                    await tableWatches[index].ShouldReachAsync(expected, $"step {step} ({write}), watch {index}");
                }
            }

            // and matching by row signalled fewer watches to get there
            byRow.Wakes.Should().BeLessThan(byTable.Wakes);
            byRow.RowsRuledOut.Should().BePositive();
            byRow.Fallbacks.Should().Be(0);
        }
        finally
        {
            foreach (var watch in rowWatches.Concat(tableWatches))
            {
                await watch.DisposeAsync();
            }
        }
    }

    // Filters on the key and on columns fixed at insert, which rows are matched on; a filter on a changeable column
    // and an unfiltered read, which are matched by table; and combinations of them.
    private static readonly Func<LiveDb, CancellationToken, Task<string>>[] _Queries =
    [
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.OwnerId == 0), cancellationToken),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.OwnerId == 1), cancellationToken),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.OwnerId == 2), cancellationToken),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.OwnerId == 3), cancellationToken),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.Id == 1), cancellationToken),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.Id == 2), cancellationToken),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.Id == 5), cancellationToken),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.Id == 9), cancellationToken),
        (db, cancellationToken) =>
            _ListAsync(db.Tickets.Where(ticket => ticket.Kind == TicketKind.Feature), cancellationToken),
        (db, cancellationToken) =>
            _ListAsync(
                db.Tickets.Where(ticket => ticket.OwnerId == 1 && ticket.Kind == TicketKind.Bug),
                cancellationToken
            ),
        (db, cancellationToken) => _ListAsync(db.Tickets.Where(ticket => ticket.Priority == 1), cancellationToken),
        (db, cancellationToken) =>
            _ListAsync(db.Tickets.Where(ticket => ticket.OwnerId == 2 && ticket.Priority == 1), cancellationToken),
        async (db, cancellationToken) => $"{await db.Tickets.CountAsync(cancellationToken)}",
        async (db, cancellationToken) =>
            await _ListAsync(db.Tickets.Where(ticket => ticket.OwnerId == 3), cancellationToken)
            + await _ListAsync(db.Tickets.Where(ticket => ticket.Id == 4), cancellationToken),
    ];

    private static async Task<string> _ListAsync(IQueryable<Ticket> tickets, CancellationToken cancellationToken)
    {
        var rows = await tickets
            .OrderBy(ticket => ticket.Id)
            .Select(ticket => new
            {
                ticket.Id,
                ticket.Title,
                ticket.Priority,
            })
            .ToListAsync(cancellationToken);

        return string.Join(';', rows.Select(row => $"{row.Id}:{row.Title}:{row.Priority}")) + "|";
    }

    private static async Task<DeliveredWatch[]> _OpenAsync(LiveEfTestHost host)
    {
        var watches = new DeliveredWatch[_Queries.Length];

        for (var index = 0; index < watches.Length; index++)
        {
#pragma warning disable CA2000 // False positive: the DeliveredWatch built from it owns the watch and disposes it.
            var watch = host.Watch(_Queries[index], AbortToken);
#pragma warning restore CA2000
            watches[index] = new DeliveredWatch(watch, await watch.NextAsync());
        }

        return watches;
    }

    private static async Task<string> _ReadAsync(
        LiveEfTestHost host,
        Func<LiveDb, CancellationToken, Task<string>> query
    )
    {
        await using var scope = host.CreateScope();

        return await query(scope.ServiceProvider.GetRequiredService<LiveDb>(), AbortToken);
    }

    private static async Task<Write> _NextWriteAsync(LiveEfTestHost host, Random random, int step)
    {
        List<int> ids = [];
        await host.WriteAsync(async db => ids = await db.Tickets.Select(ticket => ticket.Id).ToListAsync(AbortToken));

        // A write that needs a stored row becomes an insert while the table is empty.
        var kind = ids.Count == 0 ? WriteKind.Add : (WriteKind)random.Next(Enum.GetValues<WriteKind>().Length);

        return new Write(
            kind,
            Id: ids.Count == 0 ? 0 : ids[random.Next(ids.Count)],
            Owner: random.Next(4),
            Value: random.Next(3),
            Step: step
        );
    }

    private static async Task _ApplyAsync(LiveEfTestHost host, Write write)
    {
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var title = $"s{write.Step}";

        Ticket NewTicket(int owner)
        {
            return new Ticket
            {
                OwnerId = owner,
                Kind = (TicketKind)(write.Value % 2),
                Priority = write.Value,
                Title = title,
            };
        }

        switch (write.Kind)
        {
            case WriteKind.Add:
                db.Tickets.Add(NewTicket(write.Owner));
                await db.SaveChangesAsync(AbortToken);

                break;
            case WriteKind.AddTwo:
                db.Tickets.AddRange(NewTicket(write.Owner), NewTicket((write.Owner + 1) % 4));
                await db.SaveChangesAsync(AbortToken);

                break;
            case WriteKind.UpdateLoaded:
                var loaded = await db.Tickets.SingleAsync(ticket => ticket.Id == write.Id, AbortToken);
                loaded.Title = title;
                loaded.Priority = write.Value;
                await db.SaveChangesAsync(AbortToken);

                break;
            case WriteKind.DeleteLoaded:
                db.Tickets.Remove(await db.Tickets.SingleAsync(ticket => ticket.Id == write.Id, AbortToken));
                await db.SaveChangesAsync(AbortToken);

                break;
            case WriteKind.DeleteByKey:
                db.Tickets.Remove(new Ticket { Id = write.Id });
                await db.SaveChangesAsync(AbortToken);

                break;
            case WriteKind.UpdateByKey:
                // The instance's owner is a guess, as a request that carries only the key and the new title makes.
                var stub = new Ticket { Id = write.Id, OwnerId = write.Owner };
                db.Tickets.Attach(stub);
                stub.Title = title;
                await db.SaveChangesAsync(AbortToken);

                break;
            case WriteKind.UpdateSet:
                await db
                    .Tickets.Where(ticket => ticket.OwnerId == write.Owner)
                    .ExecuteUpdateAsync(
                        tickets => tickets.SetProperty(ticket => ticket.Priority, write.Value),
                        AbortToken
                    );

                break;
            case WriteKind.CommitTwoSaves:
            {
                await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
                db.Tickets.Add(NewTicket(write.Owner));
                await db.SaveChangesAsync(AbortToken);
                (await db.Tickets.SingleAsync(ticket => ticket.Id == write.Id, AbortToken)).Title = title;
                await db.SaveChangesAsync(AbortToken);
                await transaction.CommitAsync(AbortToken);

                break;
            }
            case WriteKind.RollBack:
            {
                await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
                db.Tickets.Add(NewTicket(write.Owner));
                db.Tickets.Remove(await db.Tickets.SingleAsync(ticket => ticket.Id == write.Id, AbortToken));
                await db.SaveChangesAsync(AbortToken);
                await transaction.RollbackAsync(AbortToken);

                break;
            }
            case WriteKind.UnitOfWork:
                await host.UnitOfWork.RunAsync(
                    db,
                    async (_, cancellationToken) =>
                    {
                        db.Tickets.Add(NewTicket(write.Owner));
                        db.Tickets.Remove(
                            await db.Tickets.SingleAsync(ticket => ticket.Id == write.Id, cancellationToken)
                        );
                        await db.SaveChangesAsync(cancellationToken);
                    },
                    cancellationToken: AbortToken
                );

                break;
            default:
                throw new InvalidOperationException($"Unknown write kind {write.Kind}.");
        }
    }

    private enum WriteKind
    {
        Add,
        AddTwo,
        UpdateLoaded,
        DeleteLoaded,
        DeleteByKey,
        UpdateByKey,
        UpdateSet,
        CommitTwoSaves,
        RollBack,
        UnitOfWork,
    }

    private sealed record Write(WriteKind Kind, int Id, int Owner, int Value, int Step);

    /// <summary>A watch together with the last result it delivered.</summary>
    private sealed class DeliveredWatch(Watch<string> watch, string first) : IAsyncDisposable
    {
        private string _delivered = first;

        /// <summary>
        /// Asserts that the watch holds <paramref name="expected" />: already, or as its next result. A watch that
        /// was not signalled for a write that changed its result never delivers, and fails here.
        /// </summary>
        public async Task ShouldReachAsync(string expected, string because)
        {
            if (string.Equals(_delivered, expected, StringComparison.Ordinal))
            {
                return;
            }

            _delivered = await watch.NextAsync();
            _delivered.Should().Be(expected, because);
        }

        public ValueTask DisposeAsync() => watch.DisposeAsync();
    }
}
