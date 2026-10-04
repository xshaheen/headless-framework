// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Headless.Messaging.Storage.InMemory;

namespace Tests;

public sealed partial class InMemoryDataStorageTests
{
    protected override (IDataStorage Storage, MessagingOptions Options) CreateSchedulingTestStorage(TimeProvider clock)
    {
        _EnsureInitialized();
        return (
            new InMemoryDataStorage(
                _messagingOptions!,
                _serializer!,
                new SequentialGuidGenerator(SequentialGuidType.SqlServer),
                clock,
                NodeMembership
            ),
            _messagingOptions!.Value
        );
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

    [Fact]
    public override Task should_converge_inbox_admission_and_require_exact_fence()
    {
        return base.should_converge_inbox_admission_and_require_exact_fence();
    }

    [Fact]
    public override Task should_converge_n_way_inbox_admission_on_one_generation()
    {
        return base.should_converge_n_way_inbox_admission_on_one_generation();
    }

    [Fact]
    public override Task should_isolate_every_persisted_inbox_key_component()
    {
        return base.should_isolate_every_persisted_inbox_key_component();
    }

    [Fact]
    public override Task should_enforce_inbox_key_length_boundaries_without_truncation()
    {
        return base.should_enforce_inbox_key_length_boundaries_without_truncation();
    }

    [Fact]
    public override Task should_suppress_terminal_inbox_redelivery()
    {
        return base.should_suppress_terminal_inbox_redelivery();
    }

    [Fact]
    public override Task should_apply_audited_inbox_operations_once_and_reject_operation_identity_reuse()
    {
        return base.should_apply_audited_inbox_operations_once_and_reject_operation_identity_reuse();
    }

    [Fact]
    public override Task should_filter_monitoring_messages_by_intent_type()
    {
        return base.should_filter_monitoring_messages_by_intent_type();
    }

    [Fact]
    public override Task should_store_received_bus_and_queue_rows_with_same_identity()
    {
        return base.should_store_received_bus_and_queue_rows_with_same_identity();
    }

    [Fact]
    public override Task should_use_database_clock_when_reclaiming_published_retry_lease()
    {
        return base.should_use_database_clock_when_reclaiming_published_retry_lease();
    }

    [Fact]
    public override Task should_use_database_clock_when_reclaiming_received_retry_lease()
    {
        return base.should_use_database_clock_when_reclaiming_received_retry_lease();
    }

    [Fact]
    public override Task should_use_database_clock_when_fast_forwarding_dead_owner_lease()
    {
        return base.should_use_database_clock_when_fast_forwarding_dead_owner_lease();
    }

    [Fact]
    public override Task should_stamp_retry_lease_from_database_clock()
    {
        return base.should_stamp_retry_lease_from_database_clock();
    }

    [Fact]
    public override Task should_allow_published_fenced_writes_with_fast_application_clock()
    {
        return base.should_allow_published_fenced_writes_with_fast_application_clock();
    }

    [Fact]
    public override Task should_allow_received_fenced_writes_with_fast_application_clock()
    {
        return base.should_allow_received_fenced_writes_with_fast_application_clock();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public override Task should_stamp_fresh_dispatch_lease_from_database_clock(bool published, bool reserveAttempt)
    {
        return base.should_stamp_fresh_dispatch_lease_from_database_clock(published, reserveAttempt);
    }
}
