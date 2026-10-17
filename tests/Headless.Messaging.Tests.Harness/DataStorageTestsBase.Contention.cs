// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Tests.Guarantees;

namespace Tests;

/// <summary>
/// Retry rows under contention: several pickers draining one backlog, an attempt that outlives its lease, and nodes
/// whose application clocks disagree with the store's.
/// </summary>
public abstract partial class DataStorageTestsBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Guarantee("MSG-RETRY-02")]
    public async Task should_claim_each_backlog_row_once_across_eight_racing_retry_pickers(bool published)
    {
        // given: a retry backlog eight times larger than one claim batch
        const int pickers = 8;
        const int backlogSize = 40;
        var storage = CreateStorageWithRetryBatchSize(5) ?? GetStorage();
        var backlog = new HashSet<Guid>();
        for (var i = 0; i < backlogSize; i++)
        {
            backlog.Add((await _StoreRetryDueAsync(storage, published, $"racing-backlog-{published}")).StorageId);
        }

        // when: eight pickers released together drain it
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var claims = new ConcurrentBag<Guid>();
        var racers = Enumerable
            .Range(0, pickers)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        await start.Task;

                        // A store that claims a row more than once would hand it out on every round, so the rounds
                        // are capped at the backlog size rather than left to loop until the test times out.
                        for (var round = 0; round < backlogSize; round++)
                        {
                            var batch = (await _PickUpRetriesAsync(storage, published))
                                .Select(m => m.StorageId)
                                .Where(backlog.Contains)
                                .ToList();
                            if (batch.Count == 0)
                            {
                                return;
                            }

                            foreach (var id in batch)
                            {
                                claims.Add(id);
                            }
                        }
                    },
                    AbortToken
                )
            )
            .ToList();
        start.SetResult();
        await Task.WhenAll(racers);

        // then: every row was claimed, and no row by two pickers
        claims.Should().OnlyHaveUniqueItems();
        claims.Should().BeEquivalentTo(backlog);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Guarantee("MSG-LEASE-04")]
    public async Task should_fence_the_old_owner_once_its_lease_lapses_mid_attempt_and_another_node_claims_the_row(
        bool published
    )
    {
        // given: an owner leased a retry row and reserved an attempt on a half-second lease
        var storage = GetStorage();
        NodeMembership.SetIdentity($"lapsed-attempt-owner-{published}");
        var attempt = await _StoreRetryDueAsync(storage, published, "lapsed-attempt");
        var originalInlineAttempts = attempt.InlineAttempts++;
        var leased = published
            ? await storage.LeasePublishAndReserveAttemptAsync(
                attempt,
                TimeSpan.FromMilliseconds(500),
                originalInlineAttempts,
                AbortToken
            )
            : await storage.LeaseReceiveAndReserveAttemptAsync(
                attempt,
                TimeSpan.FromMilliseconds(500),
                originalInlineAttempts,
                AbortToken
            );
        leased.Should().BeTrue();

        // when: the attempt runs past its lease and another node's retry pickup claims the row
        await _ElapseAsync(TimeSpan.FromMilliseconds(750));
        NodeMembership.SetIdentity($"lapsed-attempt-successor-{published}");
        var claimed = (await _PickUpRetriesAsync(storage, published))
            .Should()
            .ContainSingle(m => m.StorageId == attempt.StorageId, "a lapsed lease leaves the row to the next owner")
            .Subject;

        // then: the old owner can neither renew its lease nor record the outcome of its attempt
        (await _RenewAsync(storage, published, attempt, TimeSpan.FromMinutes(1)))
            .Should()
            .BeFalse();
        (await _WriteSucceededAsync(storage, published, attempt))
            .Should()
            .BeFalse("an attempt whose lease another node took must not write the row");

        // and: the new owner runs its own attempt to the end
        var successorInlineAttempts = claimed.InlineAttempts++;
        var reserved = published
            ? await storage.ReservePublishAttemptAsync(claimed, successorInlineAttempts, AbortToken)
            : await storage.ReserveReceiveAttemptAsync(claimed, successorInlineAttempts, AbortToken);
        reserved.Should().BeTrue();
        (await _WriteSucceededAsync(storage, published, claimed)).Should().BeTrue();
        var monitoring = storage.GetMonitoringApi();
        var stored = published
            ? await monitoring.GetPublishedMessageAsync(attempt.StorageId, AbortToken)
            : await monitoring.GetReceivedMessageAsync(attempt.StorageId, AbortToken);
        stored!.StatusName.Should().Be(StatusName.Succeeded);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [Guarantee("MSG-LEASE-03")]
    public async Task should_decide_lease_expiry_on_the_database_clock_when_the_application_clock_is_an_hour_off(
        bool published,
        bool applicationClockAhead
    )
    {
        // given: a node whose application clock is an hour off the database's holds a two-second lease
        var skew = applicationClockAhead ? TimeSpan.FromHours(1) : TimeSpan.FromHours(-1);
        var skewedStorage = CreateStorageWithTimeProvider(
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow.Add(skew))
        );
        if (skewedStorage is null)
        {
            Assert.Skip("Storage does not expose a relational clock-skew test seam");
        }

        var storage = GetStorage();
        NodeMembership.SetIdentity($"skewed-holder-{published}-{applicationClockAhead}");
        var message = await _StoreRetryDueAsync(storage, published, "skewed-lease");
        (await _LeaseAsync(skewedStorage, published, message, TimeSpan.FromSeconds(2))).Should().BeTrue();

        // when: a contender with the same skew tries to claim the row while the lease is live on the database
        NodeMembership.SetIdentity($"skewed-contender-{published}-{applicationClockAhead}");
        var whileLive = await _PickUpRetriesAsync(skewedStorage, published);

        // then: it cannot, even when its clock says the lease ended an hour ago
        whileLive.Should().NotContain(m => m.StorageId == message.StorageId);

        // and: once the lease has passed on the database, the contender claims the row, even when its clock says the
        // lease has an hour left, and the old holder cannot renew it
        await _ElapseAsync(TimeSpan.FromSeconds(2.5));
        (await _PickUpRetriesAsync(skewedStorage, published)).Should().Contain(m => m.StorageId == message.StorageId);
        (await _RenewAsync(skewedStorage, published, message, TimeSpan.FromSeconds(2))).Should().BeFalse();
    }

    private static async Task<bool> _WriteSucceededAsync(IDataStorage storage, bool published, MediumMessage attempt)
    {
        return published
            ? await storage.ChangePublishRetryStateAsync(
                attempt,
                StatusName.Succeeded,
                MessageContentWrite.Preserve,
                retryDelay: null,
                lockedUntil: null,
                originalRetries: attempt.Retries,
                originalInlineAttempts: attempt.InlineAttempts,
                AbortToken
            )
            : await storage.ChangeReceiveRetryStateAsync(
                attempt,
                StatusName.Succeeded,
                MessageContentWrite.Preserve,
                retryDelay: null,
                lockedUntil: null,
                originalRetries: attempt.Retries,
                originalInlineAttempts: attempt.InlineAttempts,
                AbortToken
            );
    }
}
