// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Dapper;
using Microsoft.Data.SqlClient;

namespace Tests;

public partial class SqlServerStorageTests
{
    protected override async Task<PersistedFailure?> ReadPersistedFailureAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        var sql = published
            ? "SELECT CAST(NULL AS nvarchar(200)) AS ConsumerIdentity,ExceptionInfo,FailureFingerprint FROM headless.MessagingPublished WHERE Id=@Id;"
            : "SELECT ConsumerIdentity,ExceptionInfo,FailureFingerprint FROM headless.MessagingReceived WHERE Id=@Id;";
        await using var connection = new SqlConnection(fixture.ConnectionString);

        var rows = await connection.QueryAsync<PersistedFailure>(
            new CommandDefinition(sql, new { Id = storageId }, cancellationToken: cancellationToken)
        );

        return rows.Select(static row => (PersistedFailure?)row).SingleOrDefault();
    }

    protected override async Task<IReadOnlyList<PersistedFailure>> ReadReceivedFailuresAsync(
        string messageId,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        var rows = await connection.QueryAsync<PersistedFailure>(
            new CommandDefinition(
                "SELECT ConsumerIdentity,ExceptionInfo,FailureFingerprint FROM headless.MessagingReceived WHERE MessageId=@MessageId;",
                new { MessageId = messageId },
                cancellationToken: cancellationToken
            )
        );

        return rows.ToList();
    }

    protected override async Task CorruptStoredContentAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        var table = published ? "MessagingPublished" : "MessagingReceived";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.ExecuteAsync(
            new CommandDefinition(
                $"UPDATE headless.{table} SET Content=N'not-json' WHERE Id=@Id;",
                new { Id = storageId },
                cancellationToken: cancellationToken
            )
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task should_store_the_failure_text_and_fingerprint_in_the_terminal_failure_write(bool published) =>
        base.should_store_the_failure_text_and_fingerprint_in_the_terminal_failure_write(published);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task should_keep_the_first_failure_when_a_later_failure_write_meets_the_terminal_row(
        bool published
    ) => base.should_keep_the_first_failure_when_a_later_failure_write_meets_the_terminal_row(published);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task should_store_the_failure_on_a_retry_write_and_accept_the_success_that_follows(
        bool published
    ) => base.should_store_the_failure_on_a_retry_write_and_accept_the_success_that_follows(published);

    [Fact]
    public override Task should_store_the_poison_cause_for_a_failure_stored_on_arrival_without_one() =>
        base.should_store_the_poison_cause_for_a_failure_stored_on_arrival_without_one();

    [Fact]
    public override Task should_store_the_cause_a_failure_stored_on_arrival_names() =>
        base.should_store_the_cause_a_failure_stored_on_arrival_names();

    [Fact]
    public override Task should_keep_the_failures_of_consumer_identities_that_differ_by_a_trailing_space_apart() =>
        base.should_keep_the_failures_of_consumer_identities_that_differ_by_a_trailing_space_apart();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public override Task should_store_the_poison_cause_when_a_claim_cannot_read_a_stored_row(bool published) =>
        base.should_store_the_poison_cause_when_a_claim_cannot_read_a_stored_row(published);
}
