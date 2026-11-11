// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// What row matching must never cost: a write that can change a watch's result signals the watch, whatever the
/// write and the query look like. Each scenario signals by table where a row cannot be trusted to say enough.
/// </summary>
public sealed class LiveRowMatchingSoundnessTests : TestBase
{
    [Fact]
    public async Task should_re_run_every_watch_that_could_hold_a_row_deleted_without_loading_it()
    {
        // given - a row per owner, watched by key and by owner
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 1, Title = "one" }, new Ticket { OwnerId = 2, Title = "two" });
        await using var key1 = _WatchKey(host, 1);
        await using var key2 = _WatchKey(host, 2);
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner2 = _WatchOwner(host, 2);
        (await key1.NextAsync()).Should().Equal("one");
        (await key2.NextAsync()).Should().Equal("two");
        (await owner1.NextAsync()).Should().Equal("one");
        (await owner2.NextAsync()).Should().Equal("two");

        // when - the first row is deleted through an instance that carries only its key; its owner says 0
        await host.WriteAsync(async db =>
        {
            db.Tickets.Remove(new Ticket { Id = 1 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then - the row's own key is known, its owner is not: both owner watches run, the other key's does not
        host.Wakes.Should().Be(3);
        (await key1.NextAsync()).Should().BeEmpty();
        (await owner1.NextAsync()).Should().BeEmpty();
        key2.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_re_run_the_owners_watch_when_a_row_is_updated_through_an_instance_that_names_another_owner()
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 1, Title = "one" });
        await using var owner1 = _WatchOwner(host, 1);
        (await owner1.NextAsync()).Should().Equal("one");

        // when - the instance was never read from the database, and its owner is wrong
        await host.WriteAsync(async db =>
        {
            var stub = new Ticket { Id = 1, OwnerId = 2 };
            db.Tickets.Attach(stub);
            stub.Title = "one, renamed";
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await owner1.NextAsync()).Should().Equal("one, renamed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_re_run_the_stored_owners_watch_when_a_loaded_instance_is_changed_while_detached(
        bool changeKey
    )
    {
        // given - rows 1 and 2 belong to owners 1 and 2
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 1, Title = "one" }, new Ticket { OwnerId = 2, Title = "two" });
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner2 = _WatchOwner(host, 2);
        (await owner1.NextAsync()).Should().Equal("one");
        (await owner2.NextAsync()).Should().Equal("two");

        // when - row 1 is loaded and detached; then its owner, or its key, is overwritten before it is attached
        // again and saved, so the instance no longer says which stored row the update reaches
        await host.WriteAsync(async db =>
        {
            var ticket = await db.Tickets.SingleAsync(ticket => ticket.Id == 1, AbortToken);
            db.ChangeTracker.Clear();

            if (changeKey)
            {
                ticket.Id = 2;
            }
            else
            {
                ticket.OwnerId = 2;
            }

            db.Tickets.Attach(ticket);
            ticket.Title = "renamed";
            await db.SaveChangesAsync(AbortToken);
        });

        // then - the watch of the owner whose stored row changed has the new title
        var changed = changeKey ? owner2 : owner1;
        (await changed.NextAsync()).Should().Equal("renamed");
    }

    [Fact]
    public async Task should_re_run_the_stored_owners_watch_when_the_text_part_of_a_loaded_instances_key_is_changed()
    {
        // given - two badges of one shelf, of owners 3 and 4; the text part of their key is what tells them apart
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await host.WriteAsync(async db =>
        {
            db.Badges.Add(
                new Badge
                {
                    Shelf = 1,
                    Code = "A",
                    OwnerId = 3,
                    Note = "of three",
                }
            );
            db.Badges.Add(
                new Badge
                {
                    Shelf = 1,
                    Code = "B",
                    OwnerId = 4,
                    Note = "of four",
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });
        await using var owner4 = host.Watch(
            (db, cancellationToken) =>
                db.Badges.Where(badge => badge.OwnerId == 4).Select(badge => badge.Note).ToListAsync(cancellationToken),
            AbortToken
        );
        (await owner4.NextAsync()).Should().Equal("of four");

        // when - the first badge is loaded and detached, given the second one's code, attached and saved: the
        // update reaches the second badge, and the part of the key that says so is not one a row is matched on
        await host.WriteAsync(async db =>
        {
            var badge = await db.Badges.SingleAsync(badge => badge.Code == "A", AbortToken);
            db.ChangeTracker.Clear();
            badge.Code = "B";
            db.Badges.Attach(badge);
            badge.Note = "changed";
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        (await owner4.NextAsync())
            .Should()
            .Equal("changed");
    }

    [Fact]
    public async Task should_not_trust_a_filter_on_a_column_that_can_change_after_insert()
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var priority5 = _WatchPriority(host, 5);
        (await priority5.NextAsync()).Should().BeEmpty();

        // when - a row the filter excludes today is added
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 1, Priority = 1 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(0);
    }

    [Fact]
    public async Task should_re_run_the_watch_that_now_holds_a_row_another_context_moved_after_this_one_loaded_it()
    {
        // given - a row outside the watch's filter, loaded by a first context
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(
            host,
            new Ticket
            {
                OwnerId = 1,
                Priority = 1,
                Title = "one",
            }
        );
        await using var priority5 = _WatchPriority(host, 5);
        (await priority5.NextAsync()).Should().BeEmpty();

        await using var scope = host.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var loaded = await first.Tickets.SingleAsync(ticket => ticket.Id == 1, AbortToken);

        // when - a second context moves the row into the filter
        await host.WriteAsync(async db =>
        {
            (await db.Tickets.SingleAsync(ticket => ticket.Id == 1, AbortToken)).Priority = 5;
            await db.SaveChangesAsync(AbortToken);
        });
        (await priority5.NextAsync()).Should().Equal("one");

        // and the first context, which still holds priority 1 as both the old and the new value, renames it
        loaded.Title = "one, renamed";
        await first.SaveChangesAsync(AbortToken);

        // then
        host.Wakes.Should().Be(2);
        (await priority5.NextAsync()).Should().Equal("one, renamed");
    }

    public static TheoryData<string> TableLevelQueries => [.. _TableLevelQueries.Keys];

    [Theory]
    [MemberData(nameof(TableLevelQueries))]
    public async Task should_match_by_table_a_query_whose_filter_does_not_bound_what_it_reads(string query)
    {
        // given - a watch whose query holds an owner filter the row below does not pass
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 3, Code = "A" });
        await using var watch = host.Watch(_TableLevelQueries[query], AbortToken);
        await watch.NextAsync();

        // when
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(
                new Ticket
                {
                    OwnerId = 4,
                    Code = "B",
                    Priority = 9,
                    Region = 5,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(0);
    }

    [Fact]
    public async Task should_re_run_a_key_filtered_watch_when_the_row_its_auto_included_reference_loads_changes()
    {
        // given - folder 2 loads its shelf with every query
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await host.WriteAsync(async db =>
        {
            db.Shelves.Add(new Shelf { Id = 1, Name = "top" });
            db.Folders.Add(
                new Folder
                {
                    Id = 2,
                    ShelfId = 1,
                    Name = "letters",
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });
        await using var folder = host.Watch(
            async (db, cancellationToken) =>
            {
                var found = await db.Folders.SingleAsync(folder => folder.Id == 2, cancellationToken);

                return $"{found.Shelf!.Name}/{found.Name}";
            },
            AbortToken
        );
        (await folder.NextAsync()).Should().Be("top/letters");

        // when - the shelf row, which the key filter does not name, is renamed
        await host.WriteAsync(async db =>
        {
            (await db.Shelves.SingleAsync(shelf => shelf.Id == 1, AbortToken)).Name = "bottom";
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await folder.NextAsync()).Should().Be("bottom/letters");

        // when - another folder is added: a folder's result holds more than the row its filter names, so no
        // filter on folders is trusted
        await host.WriteAsync(async db =>
        {
            db.Folders.Add(
                new Folder
                {
                    Id = 3,
                    ShelfId = 1,
                    Name = "bills",
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(2);
    }

    [Fact]
    public async Task should_re_run_a_filtered_watch_when_a_row_its_entitys_query_filter_reads_changes()
    {
        // given - invoice 1 of owner 3, shown until another invoice voids it
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await host.WriteAsync(async db =>
        {
            db.Invoices.Add(new Invoice { Id = 1, OwnerId = 3 });
            await db.SaveChangesAsync(AbortToken);
        });
        await using var owner3 = host.Watch(
            (db, cancellationToken) => db.Invoices.CountAsync(invoice => invoice.OwnerId == 3, cancellationToken),
            AbortToken
        );
        (await owner3.NextAsync()).Should().Be(1);

        // when - an invoice of another owner voids it: a row the owner filter does not name decides the result
        await host.WriteAsync(async db =>
        {
            db.Invoices.Add(
                new Invoice
                {
                    Id = 2,
                    OwnerId = 4,
                    Voids = 1,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await owner3.NextAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_bind_a_precompiled_querys_filter_to_the_value_each_call_passes()
    {
        // given - one precompiled query, watched for two owners
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner3 = host.Watch((db, _) => _CountOfOwner(db, 3), AbortToken);
        await using var owner4 = host.Watch((db, _) => _CountOfOwner(db, 4), AbortToken);
        (await owner3.NextAsync()).Should().Be(0);
        (await owner4.NextAsync()).Should().Be(0);

        // when
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 3 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await owner3.NextAsync()).Should().Be(1);
        owner4.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_announce_nothing_for_a_save_another_interceptor_refused_to_start()
    {
        // given - an interceptor that runs after the tracker's and refuses the next save
        var refuses = new RefusesTheNextSave();
        await using var host = await LiveEfTestHost.CreateRowLevelAsync(
            options => options.AddInterceptors(refuses),
            trackerRunsFirst: true
        );
        await using var owner3 = _WatchOwner(host, 3);
        await using var memos = host.Watch(
            (db, cancellationToken) => db.Memos.CountAsync(memo => memo.OwnerId == 1, cancellationToken),
            AbortToken
        );
        (await owner3.NextAsync()).Should().BeEmpty();
        (await memos.NextAsync()).Should().Be(0);

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();

        // when - a save of a ticket is refused, the ticket is dropped, and the context saves a memo instead
        db.Tickets.Add(new Ticket { OwnerId = 4 });
        refuses.Armed = true;
        var save = () => db.SaveChangesAsync(AbortToken);
        await save.Should().ThrowAsync<InvalidOperationException>();
        db.ChangeTracker.Clear();
        db.Memos.Add(new Memo { Id = 1, OwnerId = 1 });
        await db.SaveChangesAsync(AbortToken);

        // then - the refused save announced nothing, then or later
        host.Wakes.Should().Be(1);
        (await memos.NextAsync()).Should().Be(1);
        owner3.Runs.Should().Be(1);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("raw")]
    public async Task should_match_by_table_a_write_that_is_not_saved_row_by_row(string statement)
    {
        // given - a watch of owner 3, and a row of owner 4
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 4, Title = "four" });
        await using var owner3 = _WatchOwner(host, 3);
        (await owner3.NextAsync()).Should().BeEmpty();

        // when - a statement changes owner 4's rows without loading them
        await host.WriteAsync(async db =>
        {
            var ofOwner4 = db.Tickets.Where(ticket => ticket.OwnerId == 4);

            _ = statement switch
            {
                "update" => await ofOwner4.ExecuteUpdateAsync(
                    tickets => tickets.SetProperty(ticket => ticket.Title, "renamed"),
                    AbortToken
                ),
                "delete" => await ofOwner4.ExecuteDeleteAsync(AbortToken),
                _ => await db.Database.ExecuteSqlRawAsync(
                    """UPDATE "Tickets" SET "OwnerId" = 3 WHERE "OwnerId" = 4""",
                    AbortToken
                ),
            };
        });

        // then - nothing says which rows the statement reached, so the watch runs
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(0);
    }

    [Fact]
    public async Task should_announce_the_table_when_no_watch_reads_it_at_the_time_of_the_save()
    {
        // given - a save inside a transaction, while nothing watches the table
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        db.Tickets.Add(new Ticket { OwnerId = 3, Title = "saved before the watch" });
        await db.SaveChangesAsync(AbortToken);

        // and a watch that opens before the commit, whose filter the saved row does not pass
        await using var owner4 = _WatchOwner(host, 4);
        (await owner4.NextAsync()).Should().BeEmpty();

        // when
        await transaction.CommitAsync(AbortToken);

        // then - the save kept no row to rule out, so the commit announced the table
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(0);
    }

    [Fact]
    public async Task should_re_run_a_watch_opened_between_a_save_and_its_commit_once_the_commit_lands()
    {
        // given - a watch that makes the save keep its rows
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner4 = _WatchOwner(host, 4);
        (await owner4.NextAsync()).Should().BeEmpty();

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
        db.Tickets.Add(new Ticket { OwnerId = 3, Title = "uncommitted" });
        await db.SaveChangesAsync(AbortToken);

        // and a watch of the saved row's owner that opens before the commit
        await using var owner3 = _WatchOwner(host, 3);
        await owner3.NextAsync();
        host.Wakes.Should().Be(0);

        // when
        await transaction.CommitAsync(AbortToken);

        // then - the late watch was signalled, and the watch of the other owner was not
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(1);
        owner4.Runs.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_announce_nothing_for_rows_whose_transaction_rolled_back(bool unitOfWork)
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner3 = _WatchOwner(host, 3);
        (await owner3.NextAsync()).Should().BeEmpty();

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();

        // when
        if (unitOfWork)
        {
            await using var unit = await host.UnitOfWork.BeginAsync(db, AbortToken);
            db.Tickets.Add(new Ticket { OwnerId = 3 });
            await db.SaveChangesAsync(AbortToken);
            await unit.RollbackAsync();
        }
        else
        {
            await using var transaction = await db.Database.BeginTransactionAsync(AbortToken);
            db.Tickets.Add(new Ticket { OwnerId = 3 });
            await db.SaveChangesAsync(AbortToken);
            await transaction.RollbackAsync(AbortToken);
        }

        // then
        host.Wakes.Should().Be(0);

        // and the next committed write announces only itself
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 4 });
            await db.SaveChangesAsync(AbortToken);
        });
        host.Wakes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_announce_the_tables_whole_when_the_commit_of_saved_rows_loses_its_answer(bool unitOfWork)
    {
        // given - a commit that is applied and then throws
        var answerLost = new CommitAnswerLostInterceptor();
        await using var host = await LiveEfTestHost.CreateRowLevelAsync(options => options.AddInterceptors(answerLost));
        await using var owner4 = _WatchOwner(host, 4);
        (await owner4.NextAsync()).Should().BeEmpty();

        await using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LiveDb>();
        var unit = unitOfWork ? await host.UnitOfWork.BeginAsync(db, AbortToken) : null;
        await using var transaction = unitOfWork ? null : await db.Database.BeginTransactionAsync(AbortToken);
        db.Tickets.Add(new Ticket { OwnerId = 3 });
        await db.SaveChangesAsync(AbortToken);
        answerLost.Armed = true;

        // when
        var commit = async () =>
        {
            if (unit is not null)
            {
                await unit.CompleteAsync(AbortToken);
            }
            else
            {
                await transaction!.CommitAsync(AbortToken);
            }
        };
        await commit.Should().ThrowAsync<Exception>();

        // then - what the transaction stored is not known for certain, so every watch of the table runs
        host.Wakes.Should().Be(1);
    }

    [Fact]
    public async Task should_re_run_the_watch_of_a_row_another_interceptor_adds_to_the_save_after_the_tracker_read_it()
    {
        // given - an interceptor that runs after the tracker's and adds a ticket for owner 4 to every save
        var addsTicket = new AddsTicketToEverySave(ownerId: 4);
        await using var host = await LiveEfTestHost.CreateRowLevelAsync(
            options => options.AddInterceptors(addsTicket),
            trackerRunsFirst: true
        );
        await using var owner3 = _WatchOwner(host, 3);
        await using var owner4 = _WatchOwner(host, 4);
        (await owner3.NextAsync()).Should().BeEmpty();
        (await owner4.NextAsync()).Should().BeEmpty();
        addsTicket.Armed = true;

        // when - the application saves a ticket for owner 3
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(new Ticket { OwnerId = 3, Title = "the application's" });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        (await owner3.NextAsync())
            .Should()
            .Equal("the application's");
        (await owner4.NextAsync()).Should().Equal("the interceptor's");
    }

    [Fact]
    public async Task should_re_run_filtered_watches_when_a_second_model_writes_the_same_table()
    {
        // given - a row of owner 1, and a second tracked context whose model lets the owner column change
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await _SeedAsync(host, new Ticket { OwnerId = 1, Title = "moved" });
        await using var owner1 = _WatchOwner(host, 1);
        await using var owner2 = _WatchOwner(host, 2);
        (await owner1.NextAsync()).Should().Equal("moved");
        (await owner2.NextAsync()).Should().BeEmpty();

        // when - the second context moves the row to owner 2
        await using (var scope = host.CreateScope())
        {
            var notes = scope.ServiceProvider.GetRequiredService<NotesDb>();
            (await notes.TicketRows.SingleAsync(row => row.Id == 1, AbortToken)).OwnerId = 2;
            await notes.SaveChangesAsync(AbortToken);
        }

        // then
        host.Wakes.Should().Be(2);
        (await owner1.NextAsync()).Should().BeEmpty();
        (await owner2.NextAsync()).Should().Equal("moved");
    }

    [Theory]
    [InlineData("one save in its own transaction")]
    [InlineData("one save in no transaction")]
    [InlineData("two saves of one transaction")]
    public async Task should_announce_the_table_when_one_commit_saves_more_rows_than_are_matched_one_by_one(string how)
    {
        // given
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await using var owner3 = _WatchOwner(host, 3);
        (await owner3.NextAsync()).Should().BeEmpty();

        // when - a bulk insert for another owner
        await host.WriteAsync(async db =>
        {
            static IEnumerable<Ticket> Tickets(int count) =>
                Enumerable.Range(0, count).Select(_ => new Ticket { OwnerId = 4 });

            switch (how)
            {
                case "one save in no transaction":
                    db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
                    db.Tickets.AddRange(Tickets(2000));
                    await db.SaveChangesAsync(AbortToken);

                    break;
                case "two saves of one transaction":
                    // Neither save is a bulk save by itself; the commit is.
                    await using (var transaction = await db.Database.BeginTransactionAsync(AbortToken))
                    {
                        db.Tickets.AddRange(Tickets(200));
                        await db.SaveChangesAsync(AbortToken);
                        db.Tickets.AddRange(Tickets(200));
                        await db.SaveChangesAsync(AbortToken);
                        await transaction.CommitAsync(AbortToken);
                    }

                    break;
                default:
                    db.Tickets.AddRange(Tickets(2000));
                    await db.SaveChangesAsync(AbortToken);

                    break;
            }
        });

        // then
        host.Wakes.Should().Be(1);
        host.RowsRuledOut.Should().Be(0);
        host.Fallbacks.Should().Be(0);
    }

    [Fact]
    public async Task should_announce_the_table_and_keep_the_save_when_reading_a_saved_row_fails()
    {
        // given - an interceptor that makes the saved ticket's owner unreadable once the save has stored it
        var breaksRead = new BreaksTheNextOwnerReadAfterSave();
        await using var host = await LiveEfTestHost.CreateRowLevelAsync(options => options.AddInterceptors(breaksRead));
        await using var owner4 = _WatchOwner(host, 4);
        (await owner4.NextAsync()).Should().BeEmpty();

        // when
        var ticket = new Ticket { OwnerId = 3, Title = "stored" };
        breaksRead.Ticket = ticket;
        await host.WriteAsync(async db =>
        {
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync(AbortToken);
        });

        // then - the save went through, the failure was counted, and the table was announced whole
        host.Fallbacks.Should().Be(1);
        host.Wakes.Should().Be(1);
        await host.WriteAsync(async db => (await db.Tickets.CountAsync(AbortToken)).Should().Be(1));
    }

    // Each holds the filter "owner is 3" in a shape that does not bound the rows the result depends on; the row the
    // theory adds belongs to owner 4.
    private static readonly Dictionary<string, Func<LiveDb, CancellationToken, Task<int>>> _TableLevelQueries = new(
        StringComparer.Ordinal
    )
    {
        ["a navigation in the filter"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => ticket.OwnerId == 3 && ticket.Comments.Count == 0, cancellationToken),
        ["an include"] = async (db, cancellationToken) =>
            (
                await db
                    .Tickets.Include(ticket => ticket.Comments)
                    .Where(ticket => ticket.OwnerId == 3)
                    .ToListAsync(cancellationToken)
            ).Count,
        ["a string comparison"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => ticket.Code == "A", cancellationToken),
        ["a list that contains the key"] = (db, cancellationToken) =>
        {
            int[] keys = [1, 7];

            return db.Tickets.CountAsync(ticket => keys.Contains(ticket.Id), cancellationToken);
        },
        ["a nullable value that is null"] = (db, cancellationToken) =>
        {
            int? region = null;

            return db.Tickets.CountAsync(ticket => ticket.Region == region, cancellationToken);
        },
        ["raw sql as the source"] = (db, cancellationToken) =>
            db
                .Tickets.FromSqlRaw("""SELECT * FROM "Tickets" """)
                .CountAsync(ticket => ticket.OwnerId == 3, cancellationToken),
        ["a navigation reached by name"] = (db, cancellationToken) =>
            db.Comments.CountAsync(comment => EF.Property<Ticket>(comment, "Ticket").OwnerId == 3, cancellationToken),
        ["a second source"] = (db, cancellationToken) =>
            db
                .Tickets.Where(ticket => ticket.OwnerId == 3)
                .Concat(db.Tickets.Where(ticket => ticket.OwnerId == 4))
                .CountAsync(cancellationToken),
        ["a subquery in the projection"] = (db, cancellationToken) =>
            db
                .Tickets.Where(ticket => ticket.OwnerId == 3)
                .Select(ticket => db.Tickets.Count())
                .FirstOrDefaultAsync(cancellationToken),
        ["a filter after paging"] = (db, cancellationToken) =>
            db
                .Tickets.OrderByDescending(ticket => ticket.Id)
                .Take(1)
                .CountAsync(ticket => ticket.OwnerId == 3, cancellationToken),
        ["a filter joined by or"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => ticket.OwnerId == 3 || ticket.Priority == 9, cancellationToken),
        ["an inequality"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => ticket.OwnerId != 3, cancellationToken),
        ["a comparison"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => ticket.OwnerId >= 3, cancellationToken),
        ["a negated filter"] = (db, cancellationToken) =>
#pragma warning disable RCS1068 // The fix would make it worse: the negation of an equality is the case under test.
            db.Tickets.CountAsync(ticket => !(ticket.OwnerId == 3), cancellationToken),
#pragma warning restore RCS1068
        ["a filter that must hold for all rows"] = async (db, cancellationToken) =>
            await db.Tickets.AllAsync(ticket => ticket.OwnerId == 3, cancellationToken) ? 1 : 0,
        ["a filter on a narrowed column"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => (byte)ticket.OwnerId == 3, cancellationToken),
        ["a function of the database"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(
                ticket => ticket.OwnerId == 3 && LiveDb.Magnitude(ticket.Priority) == 9,
                cancellationToken
            ),
        ["a filter on a computed value"] = (db, cancellationToken) =>
            db.Tickets.CountAsync(ticket => ticket.OwnerId + 1 == 4, cancellationToken),
    };

    private static readonly Func<LiveDb, int, Task<int>> _CountOfOwner = EF.CompileAsyncQuery(
        (LiveDb db, int owner) => db.Tickets.Count(ticket => ticket.OwnerId == owner)
    );

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

    private static Watch<List<string>> _WatchPriority(LiveEfTestHost host, int priority)
    {
        return host.Watch(
            (db, cancellationToken) =>
                db
                    .Tickets.Where(ticket => ticket.Priority == priority)
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

/// <summary>Adds a ticket to every save from inside the save, as an audit or outbox interceptor adds its rows.</summary>
internal sealed class AddsTicketToEverySave(int ownerId) : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (Armed)
        {
            eventData.Context!.Add(new Ticket { OwnerId = ownerId, Title = "the interceptor's" });
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>Makes the next read of one ticket's owner throw, from the moment its save has been stored.</summary>
internal sealed class BreaksTheNextOwnerReadAfterSave : SaveChangesInterceptor
{
    public Ticket? Ticket { get; set; }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (Ticket is { } ticket)
        {
            ticket.FailNextOwnerRead = true;
        }

        return ValueTask.FromResult(result);
    }
}

/// <summary>Refuses a save from inside its start, after the interceptors before it have seen the save begin.</summary>
internal sealed class RefusesTheNextSave : SaveChangesInterceptor
{
    public bool Armed { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (!Armed)
        {
            return ValueTask.FromResult(result);
        }

        Armed = false;

        throw new InvalidOperationException("The save was refused before it started.");
    }
}
