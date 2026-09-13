// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;

namespace Tests;

public sealed partial class SqlServerStorageTests
{
    protected override (IDataStorage Storage, MessagingOptions Options) CreateSchedulingTestStorage(TimeProvider clock)
    {
        _EnsureInitialized();
        return (_CreateStorage(clock), _messagingOptions!.Value);
    }

    [Fact]
    public override Task should_preserve_schedules_and_revocation_across_restart_and_clock_steps() =>
        base.should_preserve_schedules_and_revocation_across_restart_and_clock_steps();

    [Theory]
    [InlineData(StatusName.Succeeded, 0, false)]
    [InlineData(StatusName.Failed, 0, false)]
    [InlineData(StatusName.Scheduled, 1, false)]
    [InlineData(StatusName.Scheduled, 0, true)]
    public override Task should_fence_revocation_on_each_persisted_state(
        StatusName status,
        int retries,
        bool nextRetry
    ) => base.should_fence_revocation_on_each_persisted_state(status, retries, nextRetry);

    [Fact]
    public override Task should_reject_revocation_of_unscheduled_initial_grace_row() =>
        base.should_reject_revocation_of_unscheduled_initial_grace_row();

    [Fact]
    public override Task should_have_one_winner_when_revocation_races_claimed_reservation() =>
        base.should_have_one_winner_when_revocation_races_claimed_reservation();

    [Fact]
    public override Task should_revoke_delayed_message_and_prevent_reservation_and_shutdown_resurrection() =>
        base.should_revoke_delayed_message_and_prevent_reservation_and_shutdown_resurrection();

    [Fact]
    public override Task should_revoke_queued_message_before_first_reservation() =>
        base.should_revoke_queued_message_before_first_reservation();

    [Fact]
    public override Task should_reject_revocation_after_attempt_reservation() =>
        base.should_reject_revocation_after_attempt_reservation();

    [Fact]
    public override Task should_not_revoke_pending_persisted_retry_with_reset_inline_counter() =>
        base.should_not_revoke_pending_persisted_retry_with_reset_inline_counter();

    [Fact]
    public override Task should_revoke_claimed_but_unreserved_message() =>
        base.should_revoke_claimed_but_unreserved_message();

    [Fact]
    public override Task should_not_revoke_when_request_is_cancelled() =>
        base.should_not_revoke_when_request_is_cancelled();

    [Fact]
    public override Task should_have_one_winner_when_revocation_races_first_reservation() =>
        base.should_have_one_winner_when_revocation_races_first_reservation();
}
