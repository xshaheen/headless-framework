// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed partial class InMemoryDataStorageTests
{
    protected override Task SetChangeMarkerAsync(
        bool published,
        Guid storageId,
        DateTimeOffset marker,
        CancellationToken cancellationToken
    )
    {
        _EnsureInitialized();
        var rows = published ? _storage!.PublishedMessages : _storage!.ReceivedMessages;
        rows[storageId].ChangedAt = marker;

        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task should_move_the_change_marker_forward_on_insert_retry_and_terminal_writes(bool published) =>
        base.should_move_the_change_marker_forward_on_insert_retry_and_terminal_writes(published);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task should_leave_the_change_marker_on_a_retry_pickup_claim_and_a_lease_renewal(bool published) =>
        base.should_leave_the_change_marker_on_a_retry_pickup_claim_and_a_lease_renewal(published);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task should_stamp_a_later_marker_when_the_clock_has_not_passed_the_previous_one(bool published) =>
        base.should_stamp_a_later_marker_when_the_clock_has_not_passed_the_previous_one(published);

    [Fact]
    public override Task should_read_a_row_again_while_its_marker_lies_inside_the_overlap_window() =>
        base.should_read_a_row_again_while_its_marker_lies_inside_the_overlap_window();
}
