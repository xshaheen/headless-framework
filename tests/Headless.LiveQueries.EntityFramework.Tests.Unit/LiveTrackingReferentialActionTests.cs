// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests;

/// <summary>
/// A delete of a row other rows refer to. The database removes or clears those rows itself (ON DELETE CASCADE,
/// ON DELETE SET NULL), with no command of their own, so the tracker announces their tables with the delete.
/// </summary>
public sealed class LiveTrackingReferentialActionTests : TestBase
{
    [Fact]
    public async Task should_re_run_the_watches_of_the_rows_the_database_removed_or_cleared_with_a_saved_delete()
    {
        // given - a shelf with a folder, a sheet in that folder, and a label; no watch reads the shelves
        await using var host = await _CreateWithOneShelfAsync();
        await using var folders = host.Watch((db, ct) => db.Folders.CountAsync(ct), AbortToken);
        await using var sheets = host.Watch((db, ct) => db.Sheets.CountAsync(ct), AbortToken);
        await using var shelved = host.Watch((db, ct) => db.Labels.CountAsync(x => x.ShelfId != null, ct), AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await folders.NextAsync()).Should().Be(1);
        (await sheets.NextAsync()).Should().Be(1);
        (await shelved.NextAsync()).Should().Be(1);
        (await counters.NextAsync()).Should().Be(0);

        // when - the shelf is deleted alone: the context never loaded the rows that refer to it
        await host.WriteAsync(async db =>
        {
            db.Shelves.Remove(new Shelf { Id = 1 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then - the folder went with the shelf, the sheet with the folder, and the label lost its shelf
        host.Wakes.Should().Be(3);
        (await folders.NextAsync()).Should().Be(0);
        (await sheets.NextAsync()).Should().Be(0);
        (await shelved.NextAsync()).Should().Be(0);
        counters.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_announce_the_tables_of_the_removed_rows_whole_when_rows_are_matched()
    {
        // given - both watches read through a filter on a key, so a save is matched against them row by row
        await using var host = await _CreateWithOneShelfAsync(rowLevel: true);
        await using var shelf = host.Watch((db, ct) => db.Shelves.CountAsync(x => x.Id == 1, ct), AbortToken);
        await using var sheet = host.Watch((db, ct) => db.Sheets.CountAsync(x => x.Id == 3, ct), AbortToken);
        (await shelf.NextAsync()).Should().Be(1);
        (await sheet.NextAsync()).Should().Be(1);

        // when
        await host.WriteAsync(async db =>
        {
            db.Shelves.Remove(new Shelf { Id = 1 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then - no row of the sheets table was saved, so only its table can speak for the sheet that is gone
        host.Wakes.Should().Be(2);
        (await shelf.NextAsync()).Should().Be(0);
        (await sheet.NextAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_announce_the_tables_of_the_removed_rows_whole_when_the_delete_joins_the_save_late()
    {
        // given - an interceptor that runs after the tracker's and deletes folder 2 from inside the save
        var deletesFolder = new DeletesFolderFromInsideTheSave(folderId: 2);
        await using var host = await LiveEfTestHost.CreateRowLevelAsync(
            options => options.AddInterceptors(deletesFolder),
            trackerRunsFirst: true
        );
        await _SeedOneShelfAsync(host);

        // and a second sheet, in a folder of its own, with an empty folder to move it to
        await host.WriteAsync(async db =>
        {
            db.Folders.Add(new Folder { Id = 5, ShelfId = 1 });
            db.Folders.Add(new Folder { Id = 6, ShelfId = 1 });
            db.Sheets.Add(new Sheet { Id = 9, FolderId = 5 });
            await db.SaveChangesAsync(AbortToken);
        });

        // and a watch of sheet 3, which is in folder 2
        await using var sheet = host.Watch((db, ct) => db.Sheets.CountAsync(x => x.Id == 3, ct), AbortToken);
        (await sheet.NextAsync()).Should().Be(1);
        deletesFolder.Armed = true;

        // when - the application saves sheet 9 alone, a row the watch's filter rules out
        await host.WriteAsync(async db =>
        {
            var moved = await db.Sheets.SingleAsync(x => x.Id == 9, AbortToken);
            moved.FolderId = 6;
            await db.SaveChangesAsync(AbortToken);
        });

        // then - the database removed sheet 3 with its folder, and the saved row of the same table says nothing
        // about it, so the sheets are announced as a table
        host.Wakes.Should().Be(1);
        (await sheet.NextAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("""DELETE FROM "Shelves" WHERE "Id" = 1""")]
    [InlineData("""/* tidy up */ delete from "Shelves" where "Id" = 1""")]
    [InlineData("""WITH "gone" AS (SELECT 1 AS "Id") DELETE FROM "Shelves" WHERE "Id" IN (SELECT "Id" FROM "gone")""")]
    public async Task should_re_run_the_watches_of_the_rows_the_database_removed_with_a_set_based_delete(string? sql)
    {
        // given
        await using var host = await _CreateWithOneShelfAsync();
        await using var folders = host.Watch((db, ct) => db.Folders.CountAsync(ct), AbortToken);
        await using var sheets = host.Watch((db, ct) => db.Sheets.CountAsync(ct), AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await folders.NextAsync()).Should().Be(1);
        (await sheets.NextAsync()).Should().Be(1);
        (await counters.NextAsync()).Should().Be(0);

        // when - ExecuteDelete, or SQL the application wrote
        await host.WriteAsync(async db =>
        {
            if (sql is null)
            {
                await db.Shelves.Where(x => x.Id == 1).ExecuteDeleteAsync(AbortToken);
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync(sql, AbortToken);
            }
        });

        // then
        host.Wakes.Should().Be(2);
        (await folders.NextAsync()).Should().Be(0);
        (await sheets.NextAsync()).Should().Be(0);
        counters.Runs.Should().Be(1);
    }

    [Theory]
    [InlineData("""INSERT OR REPLACE INTO "Shelves" ("Id", "Name") VALUES (1, 'bottom')""")]
    [InlineData("""insert or replace into "Shelves" ("Id", "Name") values (1, 'bottom')""")]
    [InlineData("""REPLACE INTO "Shelves" ("Id", "Name") VALUES (1, 'bottom')""")]
    public async Task should_re_run_the_watches_of_the_rows_the_database_removed_with_a_replaced_row(string sql)
    {
        // given
        await using var host = await _CreateWithOneShelfAsync();
        await using var folders = host.Watch((db, ct) => db.Folders.CountAsync(ct), AbortToken);
        await using var sheets = host.Watch((db, ct) => db.Sheets.CountAsync(ct), AbortToken);
        await using var shelved = host.Watch((db, ct) => db.Labels.CountAsync(x => x.ShelfId != null, ct), AbortToken);
        await using var counters = host.WatchCounterCount(AbortToken);
        (await folders.NextAsync()).Should().Be(1);
        (await sheets.NextAsync()).Should().Be(1);
        (await shelved.NextAsync()).Should().Be(1);
        (await counters.NextAsync()).Should().Be(0);

        // when - the shelf's row is written again under its key
        await host.WriteAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, AbortToken));

        // then - SQLite resolves the key conflict by deleting the old row, and that delete takes the folder and
        // its sheet with it and clears the label, although the shelf is still there
        var (shelfRows, folderRows) = await _CountShelvesAndFoldersAsync(host);
        shelfRows.Should().Be(1);
        folderRows.Should().Be(0, "SQLite runs the foreign keys' delete actions for the row a replace removes");
        host.Wakes.Should().Be(3);
        (await folders.NextAsync()).Should().Be(0);
        (await sheets.NextAsync()).Should().Be(0);
        (await shelved.NextAsync()).Should().Be(0);
        counters.Runs.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_re_run_the_watch_of_rows_a_hand_written_schema_removes_where_the_model_cascades_on_the_client(
        bool setBased
    )
    {
        // given - the model cascades bookmarks on the client only, and the table was created to cascade itself
        await using var host = await _CreateWithOneShelfAsync();
        await _ExecuteOutsideTrackingAsync(
            host,
            """
            DROP TABLE "Bookmarks";
            CREATE TABLE "Bookmarks" (
                "Id" INTEGER NOT NULL PRIMARY KEY,
                "SheetId" INTEGER NOT NULL REFERENCES "Sheets" ("Id") ON DELETE CASCADE
            );
            INSERT INTO "Bookmarks" ("Id", "SheetId") VALUES (8, 3);
            """
        );
        await using var bookmarks = host.Watch((db, ct) => db.Bookmarks.CountAsync(ct), AbortToken);
        (await bookmarks.NextAsync()).Should().Be(1);

        // when - the sheet is deleted alone, so EF Core has no bookmark to cascade to
        await host.WriteAsync(async db =>
        {
            if (setBased)
            {
                await db.Sheets.Where(x => x.Id == 3).ExecuteDeleteAsync(AbortToken);
            }
            else
            {
                db.Sheets.Remove(new Sheet { Id = 3 });
                await db.SaveChangesAsync(AbortToken);
            }
        });

        // then
        host.Wakes.Should().Be(1);
        (await bookmarks.NextAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_announce_the_table_of_rows_that_refer_to_rows_of_their_own_table()
    {
        // given - three sections in a chain, and a watch whose filter rules out every row but the last
        await using var host = await LiveEfTestHost.CreateRowLevelAsync();
        await host.WriteAsync(async db =>
        {
            db.Sections.Add(new Section { Id = 1 });
            db.Sections.Add(new Section { Id = 2, ParentId = 1 });
            db.Sections.Add(new Section { Id = 3, ParentId = 2 });
            await db.SaveChangesAsync(AbortToken);
        });
        await using var last = host.Watch((db, ct) => db.Sections.CountAsync(x => x.Id == 3, ct), AbortToken);
        (await last.NextAsync()).Should().Be(1);

        // when - the first is deleted alone
        await host.WriteAsync(async db =>
        {
            db.Sections.Remove(new Section { Id = 1 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then - the search for referring tables ends on a table that refers to itself, and names it: the saved
        // row is not the watched one, so only the table can speak for the rows the database removed below it
        host.Wakes.Should().Be(1);
        (await last.NextAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_re_run_the_watch_of_a_join_table_when_a_row_of_either_side_is_deleted(bool deleteSheet)
    {
        // given - a keyword on the sheet, and a watch that reads the join table alone
        await using var host = await _CreateWithOneShelfAsync();
        await host.WriteAsync(async db =>
        {
            var sheet = await db.Sheets.SingleAsync(x => x.Id == 3, AbortToken);
            db.Keywords.Add(new Keyword { Id = 7, Sheets = { sheet } });
            await db.SaveChangesAsync(AbortToken);
        });
        await using var links = host.Watch((db, ct) => db.SheetKeywords.CountAsync(ct), AbortToken);
        (await links.NextAsync()).Should().Be(1);

        // when - one side is deleted alone; the link was never loaded
        await host.WriteAsync(async db =>
        {
            if (deleteSheet)
            {
                await db.Sheets.Where(x => x.Id == 3).ExecuteDeleteAsync(AbortToken);
            }
            else
            {
                db.Keywords.Remove(new Keyword { Id = 7 });
                await db.SaveChangesAsync(AbortToken);
            }
        });

        // then
        host.Wakes.Should().Be(1);
        (await links.NextAsync()).Should().Be(0);
    }

    [Fact]
    public async Task should_re_run_the_watch_of_a_derived_types_own_table_when_the_row_its_base_refers_to_is_deleted()
    {
        // given - a ring binder on the shelf, and a watch that reads the derived type's table alone
        await using var host = await _CreateWithOneShelfAsync();
        await host.WriteAsync(async db =>
        {
            db.RingBinders.Add(
                new RingBinder
                {
                    Id = 6,
                    ShelfId = 1,
                    Rings = 4,
                }
            );
            await db.SaveChangesAsync(AbortToken);
        });
        await using var rings = host.Watch(
            (db, ct) =>
                db.Database.SqlQuery<int>($"""SELECT COUNT(*) AS "Value" FROM "RingBinders" """).SingleAsync(ct),
            AbortToken
        );
        (await rings.NextAsync()).Should().Be(1);

        // when - the foreign key to the shelf is on the base type's table, which no watch reads
        await host.WriteAsync(async db =>
        {
            db.Shelves.Remove(new Shelf { Id = 1 });
            await db.SaveChangesAsync(AbortToken);
        });

        // then
        host.Wakes.Should().Be(1);
        (await rings.NextAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("""/* truncate */ UPDATE "Shelves" SET "Name" = 'bottom'""")]
    [InlineData("""UPDATE "Shelves" SET "Name" = 'bottom' -- TRUNCATE would be quicker""")]
    public async Task should_take_sql_that_holds_the_truncate_keyword_for_a_delete(string sql)
    {
        // given - SQLite has no TRUNCATE statement, so the keyword rides in a comment of a statement it accepts
        await using var host = await _CreateWithOneShelfAsync();
        await using var folders = host.Watch((db, ct) => db.Folders.CountAsync(ct), AbortToken);
        await using var sheets = host.Watch((db, ct) => db.Sheets.CountAsync(ct), AbortToken);
        (await folders.NextAsync()).Should().Be(1);
        (await sheets.NextAsync()).Should().Be(1);

        // when
        await host.WriteAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, AbortToken));

        // then - no watch reads the shelves, so both were signalled as tables a delete of shelves can change
        host.Wakes.Should().Be(2);
    }

    [Theory]
    [InlineData("""UPDATE "Shelves" SET "Name" = (SELECT deleted_at FROM (SELECT 'bottom' AS deleted_at))""")]
    [InlineData("""UPDATE "Shelves" SET "Name" = (SELECT is_truncated FROM (SELECT 'bottom' AS is_truncated))""")]
    [InlineData("""UPDATE "Shelves" SET "Name" = (SELECT replaced_by FROM (SELECT 'bottom' AS replaced_by))""")]
    public async Task should_not_take_a_column_whose_name_only_contains_a_delete_keyword_for_a_delete(string sql)
    {
        // given
        await using var host = await _CreateWithOneShelfAsync();
        await using var names = host.Watch((db, ct) => db.Shelves.Select(x => x.Name).SingleAsync(ct), AbortToken);
        await using var folders = host.Watch((db, ct) => db.Folders.CountAsync(ct), AbortToken);
        await using var sheets = host.Watch((db, ct) => db.Sheets.CountAsync(ct), AbortToken);
        (await names.NextAsync()).Should().Be("top");
        (await folders.NextAsync()).Should().Be(1);
        (await sheets.NextAsync()).Should().Be(1);

        // when
        await host.WriteAsync(async db => await db.Database.ExecuteSqlRawAsync(sql, AbortToken));

        // then - the statement removes no row, so only the watch that read the shelves runs again
        host.Wakes.Should().Be(1);
        (await names.NextAsync()).Should().Be("bottom");
        folders.Runs.Should().Be(1);
        sheets.Runs.Should().Be(1);
    }

    [Fact]
    public async Task should_not_re_run_the_watches_of_referring_rows_after_an_update_of_the_row_they_refer_to()
    {
        // given
        await using var host = await _CreateWithOneShelfAsync();
        await using var names = host.Watch((db, ct) => db.Shelves.Select(x => x.Name).SingleAsync(ct), AbortToken);
        await using var sheets = host.Watch((db, ct) => db.Sheets.CountAsync(ct), AbortToken);
        (await names.NextAsync()).Should().Be("top");
        (await sheets.NextAsync()).Should().Be(1);

        // when
        await host.WriteAsync(async db =>
            await db.Shelves.ExecuteUpdateAsync(shelves => shelves.SetProperty(x => x.Name, "bottom"), AbortToken)
        );

        // then - an update removes no row, so only the watch that read the shelves runs again
        host.Wakes.Should().Be(1);
        (await names.NextAsync()).Should().Be("bottom");
        sheets.Runs.Should().Be(1);
    }

    private static async Task<LiveEfTestHost> _CreateWithOneShelfAsync(bool rowLevel = false)
    {
        var host = rowLevel ? await LiveEfTestHost.CreateRowLevelAsync() : await LiveEfTestHost.CreateAsync();
        await _SeedOneShelfAsync(host);

        return host;
    }

    // Run on the host's connection directly, outside any tracked context, so it announces nothing.
    private static async Task _ExecuteOutsideTrackingAsync(LiveEfTestHost host, string sql)
    {
        await using var command = host.Connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(AbortToken);
    }

    // Read on the host's connection directly, outside any tracked context, so the reading is no live query's.
    private static async Task<(long Shelves, long Folders)> _CountShelvesAndFoldersAsync(LiveEfTestHost host)
    {
        await using var command = host.Connection.CreateCommand();
        command.CommandText = """SELECT (SELECT COUNT(*) FROM "Shelves"), (SELECT COUNT(*) FROM "Folders")""";
        await using var reader = await command.ExecuteReaderAsync(AbortToken);
        (await reader.ReadAsync(AbortToken)).Should().BeTrue();

        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static Task _SeedOneShelfAsync(LiveEfTestHost host)
    {
        return host.WriteAsync(async db =>
        {
            db.Shelves.Add(new Shelf { Id = 1, Name = "top" });
            db.Folders.Add(
                new Folder
                {
                    Id = 2,
                    ShelfId = 1,
                    Name = "taxes",
                }
            );
            db.Sheets.Add(new Sheet { Id = 3, FolderId = 2 });
            db.Labels.Add(new Label { Id = 4, ShelfId = 1 });
            await db.SaveChangesAsync(AbortToken);
        });
    }
}

/// <summary>
/// Deletes a folder from inside a save, after the interceptors before it have read what the save holds, as an
/// interceptor that tidies up related rows does.
/// </summary>
internal sealed class DeletesFolderFromInsideTheSave(int folderId) : SaveChangesInterceptor
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
            eventData.Context!.Remove(new Folder { Id = folderId });
        }

        return ValueTask.FromResult(result);
    }
}
