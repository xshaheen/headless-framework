// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Microsoft.Data.SqlClient;

namespace Tests;

public partial class SqlServerStorageTests
{
    protected override async Task BackdateStoredRowAsync(
        bool published,
        Guid storageId,
        TimeSpan addedAgo,
        TimeSpan? expiresAgo,
        CancellationToken cancellationToken
    )
    {
        var table = published ? "headless.MessagingPublished" : "headless.MessagingReceived";
        var expires = expiresAgo is null
            ? ""
            : ", ExpiresAt=TODATETIMEOFFSET(DATEADD(second,-@ExpiresAgoSeconds,SYSUTCDATETIME()),0)";

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"UPDATE {table} SET Added=TODATETIMEOFFSET(DATEADD(second,-@AddedAgoSeconds,SYSUTCDATETIME()),0){expires} WHERE Id=@Id;",
                new
                {
                    Id = storageId,
                    AddedAgoSeconds = (int)addedAgo.TotalSeconds,
                    ExpiresAgoSeconds = (int)(expiresAgo ?? TimeSpan.Zero).TotalSeconds,
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
