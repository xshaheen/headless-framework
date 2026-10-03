// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.AuditLog;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;

namespace Tests;

public sealed class AuditLogEntryChangeTrackingTests : TestBase
{
    [Fact]
    public async Task should_save_in_place_mutations_of_the_json_columns()
    {
        // given - a persisted entry that is still tracked
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var entry = new AuditLogEntry
            {
                Action = "entity.updated",
                CreatedAt = DateTime.UtcNow,
                OldValues = new(StringComparer.Ordinal) { ["name"] = "before" },
                NewValues = new(StringComparer.Ordinal) { ["name"] = "after" },
                ChangedFields = ["name"],
            };
            db.Add(entry);
            await db.SaveChangesAsync(AbortToken);

            // when - each JSON-backed collection is mutated in place rather than replaced
            entry.OldValues!["status"] = "draft";
            entry.NewValues!["name"] = "renamed";
            entry.ChangedFields!.Add("status");

            db.ChangeTracker.DetectChanges();
            var state = db.Entry(entry).State;
            await db.SaveChangesAsync(AbortToken);

            // then - the change tracker sees the mutations and the stored row carries them
            state.Should().Be(EntityState.Modified);

            var stored = await db.Set<AuditLogEntry>().AsNoTracking().SingleAsync(e => e.Id == entry.Id, AbortToken);

            stored.OldValues.Should().ContainKey("status");
            stored.NewValues!["name"].Should().BeOfType<JsonElement>().Which.GetString().Should().Be("renamed");
            stored.ChangedFields.Should().Equal("name", "status");
        }
    }

    [Fact]
    public async Task should_not_mark_an_entry_modified_when_its_json_columns_are_unchanged()
    {
        var (db, conn) = AuditStoreDbContext.Create();
        await using (conn)
        await using (db)
        {
            var entry = new AuditLogEntry
            {
                Action = "entity.created",
                CreatedAt = DateTime.UtcNow,
                NewValues = new(StringComparer.Ordinal) { ["count"] = 3 },
                ChangedFields = ["count"],
            };
            db.Add(entry);
            await db.SaveChangesAsync(AbortToken);

            db.ChangeTracker.DetectChanges();

            db.Entry(entry).State.Should().Be(EntityState.Unchanged);
        }
    }
}
