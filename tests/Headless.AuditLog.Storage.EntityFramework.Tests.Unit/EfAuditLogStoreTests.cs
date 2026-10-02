// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class EfAuditLogStoreTests : TestBase
{
    private static readonly DateTimeOffset _Timestamp = new(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static AuditLogEntryData _CreateEntryData(string action = "entity.created")
    {
        return new AuditLogEntryData { Action = action, CreatedAt = _Timestamp };
    }

    // ---------------------------------------------------------------------------
    // PrepareForRetry
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task should_detach_added_entries_and_support_clean_retry_when_prepare_for_retry_runs()
    {
        // given - a first save attempt left audit rows in Added state
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var store = new EfAuditLogStore();
            IReadOnlyList<AuditLogEntryData> entries = [_CreateEntryData("first"), _CreateEntryData("second")];

            var handles = store.Save(entries, db);

            handles.Should().HaveCount(2);
            db.ChangeTracker.Entries<AuditLogEntry>()
                .Should()
                .HaveCount(2)
                .And.OnlyContain(e => e.State == EntityState.Added);

            // when - the execution strategy prepares the context for a retry
            store.PrepareForRetry(db);

            // then - stale audit rows are detached so the retry cannot double-insert them
            db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();

            // and a full retry Save/commit/Release cycle works without duplicated rows
            var retryHandles = store.Save(entries, db);

            retryHandles.Should().HaveCount(2);
            db.ChangeTracker.Entries<AuditLogEntry>().Should().HaveCount(2);

            await db.SaveChangesAsync(AbortToken);

            foreach (var handle in retryHandles)
            {
                handle.ReleaseAfterCommit();
            }

            db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
            (await db.Set<AuditLogEntry>().AsNoTracking().CountAsync(AbortToken)).Should().Be(2);
        }
    }

    [Fact]
    public async Task should_not_throw_when_prepare_for_retry_runs_for_context_without_tracked_entries()
    {
        // given - a context the store never saved into
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var store = new EfAuditLogStore();

            // when
            var act = () => store.PrepareForRetry(db);

            // then
            act.Should().NotThrow();
        }
    }

    [Fact]
    public async Task should_keep_committed_entries_attached_when_prepare_for_retry_runs_after_save_changes()
    {
        // given - entries already committed (Unchanged) but not yet released
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var store = new EfAuditLogStore();
            _ = store.Save([_CreateEntryData()], db);
            await db.SaveChangesAsync(AbortToken);

            // when
            store.PrepareForRetry(db);

            // then - only stale Added entries are detached; committed rows stay attached
            db.ChangeTracker.Entries<AuditLogEntry>()
                .Should()
                .ContainSingle()
                .Which.State.Should()
                .Be(EntityState.Unchanged);
        }
    }

    // ---------------------------------------------------------------------------
    // Handle idempotency (IAuditLogStoreEntry contract)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task should_stay_detached_and_keep_state_clean_when_discard_pending_changes_called_twice()
    {
        // given
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var store = new EfAuditLogStore();
            var handle = store.Save([_CreateEntryData()], db).Single();

            // when
            handle.DiscardPendingChanges();
            var act = () => handle.DiscardPendingChanges();

            // then - second discard is a no-op and the entry stays detached
            act.Should().NotThrow();
            db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();

            // and store state is not corrupted: a fresh Save/commit cycle persists exactly one row
            var retryHandle = store.Save([_CreateEntryData()], db).Single();
            await db.SaveChangesAsync(AbortToken);
            retryHandle.ReleaseAfterCommit();

            (await db.Set<AuditLogEntry>().AsNoTracking().CountAsync(AbortToken)).Should().Be(1);
        }
    }

    [Fact]
    public async Task should_keep_committed_row_when_release_after_commit_called_twice()
    {
        // given - a committed audit row
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var store = new EfAuditLogStore();
            var handle = store.Save([_CreateEntryData()], db).Single();
            await db.SaveChangesAsync(AbortToken);

            // when
            handle.ReleaseAfterCommit();
            var act = () => handle.ReleaseAfterCommit();

            // then - second release is a no-op; local tracking is gone, the committed row survives
            act.Should().NotThrow();
            db.ChangeTracker.Entries<AuditLogEntry>().Should().BeEmpty();
            (await db.Set<AuditLogEntry>().AsNoTracking().CountAsync(AbortToken)).Should().Be(1);
        }
    }

    [Fact]
    public async Task should_not_delete_committed_row_when_discard_called_after_release()
    {
        // given - a committed and released audit row
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var store = new EfAuditLogStore();
            var handle = store.Save([_CreateEntryData()], db).Single();
            await db.SaveChangesAsync(AbortToken);
            handle.ReleaseAfterCommit();

            // when - rollback cleanup runs on an already-released handle
            var act = () => handle.DiscardPendingChanges();

            // then - it must not throw and must not undo the committed row
            act.Should().NotThrow();
            (await db.Set<AuditLogEntry>().AsNoTracking().CountAsync(AbortToken)).Should().Be(1);
        }
    }

    // ---------------------------------------------------------------------------
    // savingContext type guard
    // ---------------------------------------------------------------------------

    [Fact]
    public void should_throw_argument_exception_when_save_saving_context_is_not_a_db_context()
    {
        // given
        var store = new EfAuditLogStore();

        // when
        var act = () => store.Save([_CreateEntryData()], new object());

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_throw_argument_exception_when_save_async_saving_context_is_not_a_db_context()
    {
        // given
        var store = new EfAuditLogStore();

        // when
        var act = () => store.SaveAsync([_CreateEntryData()], new object(), AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
