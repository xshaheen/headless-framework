// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Npgsql;

namespace Tests;

public partial class PostgreSqlStorageTests
{
    protected override async Task BackdateStoredRowAsync(
        bool published,
        Guid storageId,
        TimeSpan addedAgo,
        TimeSpan? expiresAgo,
        CancellationToken cancellationToken
    )
    {
        var table = published ? "headless.messaging_published" : "headless.messaging_received";
        var expires = expiresAgo is null ? "" : """, "expires_at"=clock_timestamp()-@ExpiresAgo""";

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"""UPDATE {table} SET "added"=clock_timestamp()-@AddedAgo{expires} WHERE "id"=@Id;""",
                new
                {
                    Id = storageId,
                    AddedAgo = addedAgo,
                    ExpiresAgo = expiresAgo ?? TimeSpan.Zero,
                },
                cancellationToken: cancellationToken
            )
        );
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
