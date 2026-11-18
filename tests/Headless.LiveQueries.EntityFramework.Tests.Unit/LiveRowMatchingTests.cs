// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>What row matching spares: a saved row leaves alone the watches whose filter it cannot pass.</summary>
public sealed class LiveRowMatchingTests : TestBase
{
    private static readonly Guid _TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task should_re_run_only_the_watch_of_the_owner_a_ticket_was_added_for()
    {
        // given - four watches, each filtered on the owner column, which is fixed at insert
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner2 = _WatchOwner(host, 2);
        await using var owner3 = _WatchOwner(host, 3);
        await using var owner4 = _WatchOwner(host, 4);

        foreach (var watch in new[] { owner1, owner2, owner3, owner4 })
        {
            (await watch.NextAsync()).Should().BeEmpty();
        }

        // when
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 3, Title = "for three" });
            await db.SaveChangesAsync(AbortToken);
        });

        // then - one watch was signalled, and the row was ruled out for the other three
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(3);
        (await owner3.NextAsync()).Should().Equal("for three");
        owner1.Runs.Should().Be(1);
        owner2.Runs.Should().Be(1);
        owner4.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_match_by_table_when_row_level_matching_is_left_off()
    {
        // given
        await using var host = await LiveEfTestHost.CreateAsync();
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner3 = _WatchOwner(host, 3);
        (await owner1.NextAsync()).Should().BeEmpty();
        (await owner3.NextAsync()).Should().BeEmpty();

        // when
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 3, Title = "for three" });
            await db.SaveChangesAsync(AbortToken);
        });

        // then - both watches read the table, so both were signalled
        host.Wakes.Should().Be(2);
        host.RowsRuledOut.Should().Be(0);
    }

    [Fact]
    public async Task should_re_run_a_watch_filtered_by_key_when_that_row_is_updated_or_deleted_and_not_for_another_row()
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(
            host,
            new Ticket { OwnerId = 1, Title = "first" },
            new Ticket { OwnerId = 1, Title = "second" }
        );
        await using var first = _WatchKey(host, 1);
        (await first.NextAsync()).Should().Equal("first");

        // when - the other row is updated, then deleted
        await host.WriteAsync(async db =>
        {
            var second = await db.Tickets.SingleAsync(ticket => ticket.Id == 2, AbortToken);
            second.Title = "second, renamed";
            await db.SaveChangesAsync(AbortToken);
            db.Tickets.Remove(second);
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(0);

        // when - the watched row is updated
        await host.WriteAsync(async db =>
        {
            var ticket = await db.Tickets.SingleAsync(ticket => ticket.Id == 1, AbortToken);
            ticket.Title = "first, renamed";
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await first.NextAsync()).Should().Equal("first, renamed");

        // when - the watched row is deleted
        await host.WriteAsync(async db =>
        {
            db.Tickets.Remove(await db.Tickets.SingleAsync(ticket => ticket.Id == 1, AbortToken));
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(2);
        (await first.NextAsync()).Should().BeEmpty();
        first.Runs.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_match_a_loaded_row_on_its_owner_when_it_is_updated_or_deleted(bool delete)
    {
        // given - a row per owner, each read by its owner's watch
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 1, Title = "one" }, new Ticket { OwnerId = 2, Title = "two" });
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner2 = _WatchOwner(host, 2);
        (await owner1.NextAsync()).Should().Equal("one");
        (await owner2.NextAsync()).Should().Equal("two");

        // when - a request loads the second owner's row and changes it
        await host.WriteAsync(async db =>
        {
            var ticket = await db.Tickets.SingleAsync(ticket => ticket.Id == 2, AbortToken);

            if (delete)
            {
                db.Tickets.Remove(ticket);
            }
            else
            {
                ticket.Title = "two, renamed";
            }

            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await owner2.NextAsync()).Should().Equal(delete ? [] : ["two, renamed"]);
        owner1.Runs.Should().Be(1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task should_match_an_added_row_on_the_key_the_database_generated(int rowsInTheSave)
    {
        // given - a watch on the key the first insert will be given, and one on a key no insert here reaches
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var first = _WatchKey(host, 1);
        await using var tenth = _WatchKey(host, 10);
        (await first.NextAsync()).Should().BeEmpty();
        (await tenth.NextAsync()).Should().BeEmpty();

        // when - one row is one command; two rows run in a transaction EF Core opens for the save
        await host.WriteAsync(async db =>
        {
            for (var row = 1; row <= rowsInTheSave; row++)
            {
                db.Tickets.Add(new Ticket { OwnerId = row, Title = $"row {row}" });
            }

            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await first.NextAsync()).Should().Equal("row 1");
        tenth.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_match_an_added_row_when_the_save_does_not_accept_its_changes()
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner3 = _WatchOwner(host, 3);
        await using var owner4 = _WatchOwner(host, 4);
        (await owner3.NextAsync()).Should().BeEmpty();
        (await owner4.NextAsync()).Should().BeEmpty();

        // when
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 3, Title = "kept as added" });
            await db.SaveChangesAsync(acceptAllChangesOnSuccess: false, AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await owner3.NextAsync()).Should().Equal("kept as added");
    }

    public static TheoryData<string> NarrowingFilters => [.. _Filters.Keys];

    [Theory]
    [MemberData(nameof(NarrowingFilters))]
    public async Task should_narrow_a_watch_by_a_filter_on_a_key_or_a_column_fixed_at_insert(string filter)
    {
        // given - a watch whose filter the first ticket below passes and the second does not
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var watch = host.Watch(_Filters[filter], AbortToken);
        (await watch.NextAsync()).Should().Be(0);

        // when - a ticket the filter excludes is added
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(
                new Ticket
                {
                    OwnerId = 4,
                    Kind = TicketKind.Bug,
                    Tenant = _TenantB,
                    Urgent = false,
                    Region = 6,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(0);
        host.RowsRuledOut.Should().Be(1);

        // when - a ticket the filter includes is added
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(
                new Ticket
                {
                    OwnerId = 3,
                    Kind = TicketKind.Feature,
                    Tenant = _TenantA,
                    Urgent = true,
                    Region = 5,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await watch.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_keep_narrowing_an_entity_whose_query_filter_reads_only_its_own_row()
    {
        // given - memos are hidden by a column of their own, which no other table decides
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner1 = host.Watch(
            (db, cancellationToken) => db.Memos.CountAsync(memo => memo.OwnerId == 1, cancellationToken),
            AbortToken
        );
        (await owner1.NextAsync()).Should().Be(0);

        // when
        await host.WriteAsync(async db =>
        {
            db.Memos.Add(new Memo { Id = 1, OwnerId = 2 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(0);

        // when
        await host.WriteAsync(async db =>
        {
            db.Memos.Add(new Memo { Id = 2, OwnerId = 1 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await owner1.NextAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_re_run_a_watch_that_ran_two_filtered_queries_when_a_row_passes_either()
    {
        // given - one run reads the tickets of owner 3 and the ticket with key 2
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 9, Title = "one" }, new Ticket { OwnerId = 9, Title = "two" });
        await using var watch = host.Watch(
            async (db, cancellationToken) =>
            {
                var ofOwner = await db.Tickets.CountAsync(ticket => ticket.OwnerId == 3, cancellationToken);
                var byKey = await db
                    .Tickets.Where(ticket => ticket.Id == 2)
                    .Select(ticket => ticket.Title)
                    .SingleAsync(cancellationToken);

                return $"{ofOwner} {byKey}";
            },
            AbortToken
        );
        (await watch.NextAsync()).Should().Be("0 two");

        // when - a row neither query reads changes
        await host.WriteAsync(async db =>
        {
            (await db.Tickets.SingleAsync(ticket => ticket.Id == 1, AbortToken)).Title = "one, renamed";
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(0);

        // when - the row the second query reads changes
        await host.WriteAsync(async db =>
        {
            (await db.Tickets.SingleAsync(ticket => ticket.Id == 2, AbortToken)).Title = "two, renamed";
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await watch.NextAsync()).Should().Be("0 two, renamed");

        // when - a row the first query reads is added
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 3 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(2);
        (await watch.NextAsync()).Should().Be("1 two, renamed");
    }

    [Fact]
    public async Task should_announce_the_rows_of_every_save_of_a_transaction_once_it_commits()
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner3 = _WatchOwner(host, 3);
        await using var owner4 = _WatchOwner(host, 4);
        await using var owner5 = _WatchOwner(host, 5);

        foreach (var watch in new[] { owner3, owner4, owner5 })
        {
            (await watch.NextAsync()).Should().BeEmpty();
        }

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);

        // when - two saves, then the commit
        db.Tickets.Add(new Ticket { OwnerId = 3, Title = "first save" });
        await db.SaveChangesAsync(AbortToken);
        db.Tickets.Add(new Ticket { OwnerId = 4, Title = "second save" });
        await db.SaveChangesAsync(AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await transaction.CommitAsync(AbortToken);

        // then - the two owners written were signalled, after the commit, and the third was not
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(2);
        (await owner3.NextAsync()).Should().Equal("first save");
        (await owner4.NextAsync()).Should().Equal("second save");
        owner5.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_announce_the_rows_of_a_unit_of_work_once_it_completes()
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner3 = _WatchOwner(host, 3);
        await using var owner4 = _WatchOwner(host, 4);
        (await owner3.NextAsync()).Should().BeEmpty();
        (await owner4.NextAsync()).Should().BeEmpty();

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);

        // when
        db.Tickets.Add(new Ticket { OwnerId = 3, Title = "in the unit" });
        await db.SaveChangesAsync(AbortToken);
        var wakesBeforeCommit = host.Wakes;
        await unit.CompleteAsync(AbortToken);

        // then
        wakesBeforeCommit.Should().Be(0);
        host.Wakes.Should().Be(1);
        (await owner3.NextAsync()).Should().Equal("in the unit");
        owner4.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_narrow_each_hosts_watches_by_their_own_filters_when_two_hosts_run_the_same_queries()
    {
        // given - two hosts in one process, which share what EF Core compiled; each runs the two queries in the
        // opposite order, so a per-host numbering of the queries would give one number to two different filters
        await using var hostA = await LiveEfTestHost.CreateRowLevelAsync();
        await using var hostB = await LiveEfTestHost.CreateRowLevelAsync();
        await using var ownerOnA = _WatchOwner(hostA, 3);
        (await ownerOnA.NextAsync()).Should().BeEmpty();
        await using var keyOnB = _WatchKey(hostB, 1);
        (await keyOnB.NextAsync()).Should().BeEmpty();
        await using var keyOnA = _WatchKey(hostA, 1);
        (await keyOnA.NextAsync()).Should().BeEmpty();
        await using var ownerOnB = _WatchOwner(hostB, 3);
        (await ownerOnB.NextAsync()).Should().BeEmpty();

        // when - each host adds a ticket with key 1 that belongs to another owner
        foreach (var host in new[] { hostA, hostB })
        {
            await host.WriteAsync(async db =>
            {
                db.Tickets.Add(new Ticket { OwnerId = 8, Title = "keyed" });
                await db.SaveChangesAsync(AbortToken);
            });
        }

        // then - on each host the key watch was signalled and the owner watch was not
        hostA.Wakes.Should().Be(1);
        hostB.Wakes.Should().Be(1);
        (await keyOnA.NextAsync()).Should().Equal("keyed");
        (await keyOnB.NextAsync()).Should().Equal("keyed");
        ownerOnA.Runs.Should().Be(1);
        ownerOnB.Runs.Should().Be(1);

        // when - each host adds a ticket for the watched owner
        foreach (var host in new[] { hostA, hostB })
        {
            await host.WriteAsync(async db =>
            {
                db.Tickets.Add(new Ticket { OwnerId = 3, Title = "owned" });
                await db.SaveChangesAsync(AbortToken);
            });
        }

        // then
        hostA.Wakes.Should().Be(2);
        hostB.Wakes.Should().Be(2);
        (await ownerOnA.NextAsync()).Should().Equal("owned");
        (await ownerOnB.NextAsync()).Should().Equal("owned");
    }

    // Each counts the tickets a filter keeps. The tickets the theory adds differ in every filtered column.
    private static readonly Dictionary<string, Func<LiveDb, CancellationToken, Task<int>>> _Filters = new(
        StringComparer.Ordinal
    )
    {
        ["a captured integer"] = (db, cancellationToken) => _CountAsync(db, owner: 3, cancellationToken),
        ["a constant integer"] = (db, cancellationToken) =>
            db.Tickets.Where(ticket => ticket.OwnerId == 3).CountAsync(cancellationToken),
        ["the value on the left"] = (db, cancellationToken) =>
#pragma warning disable RCS1098 // The fix would make it worse: the constant on the left is the case under test.
            db.Tickets.Where(ticket => 3 == ticket.OwnerId).CountAsync(cancellationToken),
#pragma warning restore RCS1098
        ["a constant enum"] = (db, cancellationToken) =>
            db.Tickets.Where(ticket => ticket.Kind == TicketKind.Feature).CountAsync(cancellationToken),
        ["a captured enum"] = (db, cancellationToken) => _CountAsync(db, TicketKind.Feature, cancellationToken),
        ["a captured guid"] = (db, cancellationToken) =>
        {
            var tenant = _TenantA;

            return db.Tickets.Where(ticket => ticket.Tenant == tenant).CountAsync(cancellationToken);
        },
        ["a bool column"] = (db, cancellationToken) =>
            db.Tickets.Where(ticket => ticket.Urgent).CountAsync(cancellationToken),
        ["a captured bool"] = (db, cancellationToken) => _CountAsync(db, urgent: true, cancellationToken),
        ["a nullable column and a captured value"] = (db, cancellationToken) =>
            _CountInRegionAsync(db, region: 5, cancellationToken),
        ["a nullable column and a captured nullable value"] = (db, cancellationToken) =>
        {
            int? region = 5;

            return db.Tickets.Where(ticket => ticket.Region == region).CountAsync(cancellationToken);
        },
        ["a predicate on the terminal operator"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => ticket.OwnerId == 3, cancellationToken),
        ["two filters in a row"] = (db, cancellationToken) =>
            db
                .Tickets.Where(ticket => ticket.OwnerId == 3)
                .Where(ticket => ticket.Kind == TicketKind.Feature)
                .CountAsync(cancellationToken),
        ["a filter joined to one it cannot match"] = (db, cancellationToken) =>
            db.Tickets.Where(ticket => ticket.OwnerId == 3 && ticket.Title != "x").CountAsync(cancellationToken),
        ["a filter under sorting, paging and projection"] = async (db, cancellationToken) =>
            (
                await db
                    .Tickets.AsNoTracking()
                    .Where(ticket => ticket.OwnerId == 3)
                    .OrderBy(ticket => ticket.Id)
                    .Take(10)
                    .Select(ticket => ticket.Title)
                    .ToListAsync(cancellationToken)
            ).Count,
        ["a key lookup through Find"] = async (db, cancellationToken) =>
            await db.Tickets.FindAsync([2], cancellationToken) is null ? 0 : 1,
    };

    // The value arrives as an argument, so the query captures it instead of holding it as a constant.
    private static Task<int> _CountAsync(LiveDb db, int owner, CancellationToken cancellationToken)
    {
        return db.Tickets.Where(ticket => ticket.OwnerId == owner).CountAsync(cancellationToken);
    }

    private static Task<int> _CountAsync(LiveDb db, TicketKind kind, CancellationToken cancellationToken)
    {
        return db.Tickets.Where(ticket => ticket.Kind == kind).CountAsync(cancellationToken);
    }

    private static Task<int> _CountAsync(LiveDb db, bool urgent, CancellationToken cancellationToken)
    {
        return db.Tickets.Where(ticket => ticket.Urgent == urgent).CountAsync(cancellationToken);
    }

    private static Task<int> _CountInRegionAsync(LiveDb db, int region, CancellationToken cancellationToken)
    {
        return db.Tickets.Where(ticket => ticket.Region == region).CountAsync(cancellationToken);
    }

    private static Watch<List<string>> _WatchOwner(LiveEfTestHost host, int owner)
    {
        return host.Watch(
            (db, cancellationToken) =>
                db
                    .Tickets.Where(ticket => ticket.OwnerId == owner)
                    .OrderBy(ticket => ticket.Id)
                    .Select(ticket => ticket.Title)
                    .ToListAsync(cancellationToken),
            AbortToken
        );
    }

    private static Watch<List<string>> _WatchKey(LiveEfTestHost host, int id)
    {
        return host.Watch(
            (db, cancellationToken) =>
                db
                    .Tickets.Where(ticket => ticket.Id == id)
                    .Select(ticket => ticket.Title)
                    .ToListAsync(cancellationToken),
            AbortToken
        );
    }

    private static Task _SeedAsync(LiveEfTestHost host, params Ticket[] tickets)
    {
        return host.WriteAsync(async db =>
        {
            // One save each, so the generated keys follow the order given.
            foreach (var ticket in tickets)
            {
                db.Tickets.Add(ticket);
                await db.SaveChangesAsync(AbortToken);
            }
        });
    }
}
