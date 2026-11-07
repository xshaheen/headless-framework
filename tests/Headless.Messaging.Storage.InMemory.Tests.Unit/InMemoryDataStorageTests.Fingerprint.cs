// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests;

public sealed partial class InMemoryDataStorageTests
{
    protected override Task<PersistedFailure?> ReadPersistedFailureAsync(
        bool published,
        Guid storageId,
        CancellationToken cancellationToken
    )
    {
        _EnsureInitialized();
        var rows = published ? _storage!.PublishedMessages : _storage!.ReceivedMessages;

        return Task.FromResult<PersistedFailure?>(
            rows.TryGetValue(storageId, out var row)
                ? new PersistedFailure(
                    published ? null : row.ConsumerIdentity,
                    row.ExceptionInfo,
                    row.FailureFingerprint
                )
                : null
        );
    }

    protected override Task<IReadOnlyList<PersistedFailure>> ReadReceivedFailuresAsync(
        string messageId,
        CancellationToken cancellationToken
    )
    {
        _EnsureInitialized();

        return Task.FromResult<IReadOnlyList<PersistedFailure>>(
            _storage!
                .ReceivedMessages.Values.Where(row => string.Equals(row.Origin.Id, messageId, StringComparison.Ordinal))
                .Select(row => new PersistedFailure(row.ConsumerIdentity, row.ExceptionInfo, row.FailureFingerprint))
                .ToList()
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
}
