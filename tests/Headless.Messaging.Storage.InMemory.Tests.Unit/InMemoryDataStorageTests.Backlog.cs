// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed partial class InMemoryDataStorageTests
{
    protected override Task BackdateStoredRowAsync(
        bool published,
        Guid storageId,
        TimeSpan addedAgo,
        TimeSpan? expiresAgo,
        CancellationToken cancellationToken
    )
    {
        _EnsureInitialized();
        var rows = published ? _storage!.PublishedMessages : _storage!.ReceivedMessages;
        var row = rows[storageId];
        var now = _fakeTimeProvider!.GetUtcNow();

        row.Added = now - addedAgo;
        if (expiresAgo is { } ago)
        {
            row.ExpiresAt = now - ago;
        }

        return Task.CompletedTask;
    }

    [Fact]
    public override Task should_count_only_pending_rows_when_reading_the_backlog() =>
        base.should_count_only_pending_rows_when_reading_the_backlog();

    [Fact]
    public override Task should_age_the_oldest_pending_row_on_the_store_clock_when_reading_the_backlog() =>
        base.should_age_the_oldest_pending_row_on_the_store_clock_when_reading_the_backlog();

    [Fact]
    public override Task should_age_a_delayed_row_from_its_publish_time_when_it_comes_due() =>
        base.should_age_a_delayed_row_from_its_publish_time_when_it_comes_due();
}
