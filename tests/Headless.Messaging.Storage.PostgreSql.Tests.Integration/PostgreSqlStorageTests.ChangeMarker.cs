// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Dapper;
using Npgsql;

namespace Tests;

public partial class PostgreSqlStorageTests
{
    protected override async Task<DbTransaction?> BeginStorageTransactionAsync(CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // False positive: the caller owns the connection through the returned transaction and disposes it.
        var connection = new NpgsqlConnection(fixture.ConnectionString);
#pragma warning restore CA2000
        await connection.OpenAsync(cancellationToken);

        return await connection.BeginTransactionAsync(cancellationToken);
    }

    protected override async Task SetChangeMarkerAsync(
        bool published,
        Guid storageId,
        DateTimeOffset marker,
        CancellationToken cancellationToken
    )
    {
        var table = published ? "messaging_published" : "messaging_received";
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"""UPDATE headless.{table} SET "changed_at"=@Marker WHERE "id"=@Id;""",
                new { Marker = marker.ToUniversalTime(), Id = storageId },
                cancellationToken: cancellationToken
            )
        );
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

    [Fact]
    public override Task should_return_a_row_committed_late_inside_the_overlap_window() =>
        base.should_return_a_row_committed_late_inside_the_overlap_window();
}
