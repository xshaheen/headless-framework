// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Conformance;

/// <summary>A conformance case a provider class deliberately does not run, and why.</summary>
/// <param name="ProviderClass">The concrete provider test class that inherits the case.</param>
/// <param name="Case">The harness case method name.</param>
/// <param name="Reason">Why this provider cannot or need not run the case. Must name a real capability gap.</param>
internal sealed record ConformanceSkip(string ProviderClass, string Case, string Reason);

/// <summary>
/// Every harness case some provider class does not run. <see cref="ConformanceCoverageTests"/> fails on any
/// un-run case missing from this list and on any entry that no longer matches an un-run case.
/// </summary>
internal static class ConformanceCaseAllowList
{
    public static readonly IReadOnlyList<ConformanceSkip> Entries =
    [
        new(
            "AmazonSnsBusTransportTests",
            "should_throw_when_transport_disposed",
            "Unresolved: ITransport documents no post-dispose contract and reports send failures as OperateResult rather than exceptions; AmazonSnsBusTransport.SendAsync after DisposeAsync catches the disposed-client error and returns a failed OperateResult. Needs a decision on whether transports must throw ObjectDisposedException."
        ),
        new(
            "AmazonSqsConsumerClientHarnessTests",
            "should_delegate_commit_callback_value",
            "SQS settles by the in-flight receipt-handle state the consumer handed out, so the case's arbitrary object sender cannot be a settlement value; AmazonSqsConsumerClientConformanceTests covers broker-observed commit with a real receipt handle."
        ),
        new(
            "AmazonSqsConsumerClientHarnessTests",
            "should_delegate_reject_callback_value",
            "SQS settles by the in-flight receipt-handle state the consumer handed out, so the case's arbitrary object sender cannot be a settlement value; AmazonSqsConsumerClientConformanceTests covers broker-observed reject with a real receipt handle."
        ),
        new(
            "AzureStorageTests",
            "bulk_delete_reports_each_blob_by_identity",
            "Azurite reports deleting an already-absent blob in a batch as success rather than 404, so Ok(false) is not observable against the emulator; AzureBlobStorageDeleteMappingTests in Headless.Blobs.Azure.Tests.Unit pins the 404 to Ok(false) mapping."
        ),
        new(
            "AzureStorageTests",
            "bulk_delete_reports_per_entry_results",
            "Azurite reports deleting an already-absent blob in a batch as success rather than 404, so Ok(false) is not observable against the emulator; AzureBlobStorageDeleteMappingTests in Headless.Blobs.Azure.Tests.Unit pins the 404 to Ok(false) mapping."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_admit_a_blocked_admission_when_the_enlisted_winner_rolls_back",
            "Needs an enlisted admission that a rollback undoes; the cache provider refuses enlistment."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_clear_the_recovery_point_when_the_operation_completes",
            "Records and completes inside a unit of work; the cache provider refuses enlistment. The provider's own tests cover the autonomous form."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_draw_a_higher_generation_after_the_record_was_purged",
            "Asserts that PurgeAsync deletes the record; the cache's own entry expiry is the purge. The provider's own tests cover re-admission after the entry is gone."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_hand_a_takeover_the_last_recovery_point_the_crashed_attempt_committed",
            "Records a recovery point inside a unit of work; the cache provider refuses enlistment."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_keep_a_live_attempt_record_past_retention_until_its_lease_expires",
            "Asserts that PurgeAsync deletes the record; the cache's own entry expiry is the purge."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_leave_no_record_when_an_enlisted_admission_rolls_back",
            "Needs an enlisted admission that a rollback undoes; the cache provider refuses enlistment."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_make_an_admission_wait_for_an_enlisted_fence_and_then_replay",
            "FenceAsync is enlisted-only, and a cache holds no lock for an admission to wait on."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_not_deadlock_admissions_racing_an_enlisted_fence_then_complete",
            "FenceAsync is enlisted-only and the cache provider refuses enlistment."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_not_expose_a_recovery_point_whose_unit_rolled_back",
            "Needs an enlisted recovery point that a rollback undoes; the cache provider refuses enlistment."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_peek_without_waiting_on_an_uncommitted_write_to_the_record",
            "Needs an uncommitted enlisted write; the cache provider refuses enlistment."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_purge_only_records_past_retention_whose_lease_is_not_live",
            "The cache's own entry expiry is the purge, so PurgeAsync deletes nothing by design."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_purge_records_from_the_retention_service",
            "Asserts that the retention service deletes the record; the cache's own entry expiry is the purge."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_refuse_a_fence_in_a_long_enlisted_unit_once_the_lease_expired",
            "FenceAsync is enlisted-only and the cache provider refuses enlistment."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_refuse_a_recovery_point_from_an_attempt_that_lost_the_key_and_write_nothing",
            "Ends with an enlisted recovery point; the cache provider refuses enlistment. The provider's own tests cover the autonomous refusals."
        ),
        new(
            "CacheIdempotencyConformanceTests",
            "should_serialize_parallel_enlisted_admissions_and_replay_the_winner",
            "Admits through unit.Idempotency; a cache cannot commit or roll back with a unit of work, so the provider refuses enlistment."
        ),
        new(
            "InMemoryDataStorageTests",
            "should_expire_terminal_poison_inbox_and_allow_readmission",
            "The in-memory store keeps envelopes as live objects rather than serialized rows, so there is no persisted envelope to corrupt into a poison row."
        ),
        new(
            "InMemoryDataStorageTests",
            "should_store_and_find_rows_under_a_version_containing_sql_metacharacters",
            "The in-memory store keeps no per-row version column and builds no SQL, so there is no bound version to read back or quote to break; the relational storages run the case."
        ),
        new(
            "InMemoryDistributedLockTests",
            "should_fire_handle_lost_token_when_lock_holding_connection_dies",
            "The in-process lock has no backing connection to kill."
        ),
        new(
            "InMemoryDistributedLockTests",
            "should_not_renew_after_lock_holding_connection_dies",
            "The in-process lock has no backing connection to kill."
        ),
        new(
            "InMemoryLeasesConformanceTests",
            "should_mark_only_an_observed_unit_non_retryable_and_only_for_writes",
            "The in-memory store has no database connection to enlist a unit of work on, so there is no observed unit to mark."
        ),
        new(
            "InMemoryLeasesConformanceTests",
            "should_refuse_a_unit_on_another_database_before_any_statement",
            "The in-memory store has no database, so a unit on another database cannot exist."
        ),
        new(
            "NatsTransportTests",
            "should_throw_when_transport_disposed",
            "Unresolved: ITransport documents no post-dispose contract and reports send failures as OperateResult rather than exceptions; NatsTransport.DisposeAsync is a no-op over a pooled connection it does not own, so SendAsync after dispose still publishes. Needs a decision on whether transports must throw ObjectDisposedException."
        ),
        new(
            "PostgreSqlDistributedLockConformanceTests",
            "should_get_expiration_for_locked_resource",
            "A PostgreSQL advisory lock is session-scoped with no lease, so expiration is always null."
        ),
        new(
            "PostgreSqlDistributedLockConformanceTests",
            "should_get_lock_info_for_locked_resource",
            "A PostgreSQL advisory lock is session-scoped with no lease, so TimeToLive is always null."
        ),
        new(
            "PostgreSqlDistributedLockConformanceTests",
            "should_timeout_when_try_to_lock_acquired_resource",
            "Relies on TTL expiry freeing an undisposed lock; a session-scoped advisory lock is held for the connection lifetime and never expires."
        ),
        new(
            "PostgresReaderWriterLockConformanceTests",
            "should_auto_extend_write_lock",
            "A PostgreSQL advisory lock is session-scoped with no lease, so there is nothing to auto-extend and the renewal count stays zero."
        ),
        new(
            "PostgresReaderWriterLockConformanceTests",
            "should_fire_handle_lost_token_when_read_lock_ttl_expires",
            "A PostgreSQL advisory lock is session-scoped with no lease, so there is no TTL to expire."
        ),
        new(
            "PostgresReaderWriterLockConformanceTests",
            "should_fire_handle_lost_token_when_write_lock_ttl_expires",
            "A PostgreSQL advisory lock is session-scoped with no lease, so there is no TTL to expire."
        ),
        new(
            "PostgresReaderWriterLockConformanceTests",
            "should_prefer_queued_writer_over_new_reader",
            "Writer preference relies on a queued-writer marker that only the Redis provider keeps; the provider puts no such marker on a PostgreSQL advisory lock, so a new reader is granted while a writer waits."
        ),
        new(
            "RabbitMqConsumerClientHarnessTests",
            "should_delegate_commit_callback_value",
            "RabbitMQ settles by the ulong delivery tag the consumer handed out, so the case's arbitrary object sender cannot be a settlement value; RabbitMqConsumerClientConformanceTests covers broker-observed commit with a real tag."
        ),
        new(
            "RabbitMqConsumerClientHarnessTests",
            "should_delegate_reject_callback_value",
            "RabbitMQ settles by the ulong delivery tag the consumer handed out, so the case's arbitrary object sender cannot be a settlement value; RabbitMqConsumerClientConformanceTests covers broker-observed reject with a real tag."
        ),
        new(
            "RabbitMqTransportTests",
            "should_throw_when_transport_disposed",
            "Unresolved: ITransport documents no post-dispose contract and reports send failures as OperateResult rather than exceptions; RabbitMqTransport.DisposeAsync is a no-op over a pooled channel it does not own, so SendAsync after dispose still publishes. Needs a decision on whether transports must throw ObjectDisposedException."
        ),
        new(
            "RedisDistributedLockConformanceTests",
            "should_fire_handle_lost_token_when_lock_holding_connection_dies",
            "A Redis lock is a TTL lease key, not a session-scoped lock, so no connection holds it and its loss is detected by lease expiry instead."
        ),
        new(
            "RedisDistributedLockConformanceTests",
            "should_not_renew_after_lock_holding_connection_dies",
            "A Redis lock is a TTL lease key, not a session-scoped lock, so no connection holds it and its loss is detected by lease expiry instead."
        ),
        new(
            "SqlServerDistributedLockConformanceTests",
            "should_get_expiration_for_locked_resource",
            "A SQL Server application lock is session-scoped with no lease, so expiration is always null."
        ),
        new(
            "SqlServerDistributedLockConformanceTests",
            "should_get_lock_info_for_locked_resource",
            "A SQL Server application lock is session-scoped with no lease, so TimeToLive is always null."
        ),
        new(
            "SqlServerDistributedLockConformanceTests",
            "should_timeout_when_try_to_lock_acquired_resource",
            "Relies on TTL expiry freeing an undisposed lock; a session-scoped application lock is held for the connection lifetime and never expires."
        ),
        new(
            "SqlServerIdempotencyConformanceTests",
            "should_peek_without_waiting_on_an_uncommitted_write_to_the_record",
            "Without read committed snapshot isolation a plain READ COMMITTED read waits on an uncommitted write's exclusive row lock; SqlServerRcsiIdempotencyConformanceTests runs this case on an RCSI database."
        ),
        new(
            "SqlServerReaderWriterLockConformanceTests",
            "should_auto_extend_write_lock",
            "A SQL Server application lock is session-scoped with no lease, so there is nothing to auto-extend and the renewal count stays zero."
        ),
        new(
            "SqlServerReaderWriterLockConformanceTests",
            "should_fire_handle_lost_token_when_read_lock_ttl_expires",
            "A SQL Server application lock is session-scoped with no lease, so there is no TTL to expire."
        ),
        new(
            "SqlServerReaderWriterLockConformanceTests",
            "should_fire_handle_lost_token_when_write_lock_ttl_expires",
            "A SQL Server application lock is session-scoped with no lease, so there is no TTL to expire."
        ),
        new(
            "SqlServerReaderWriterLockConformanceTests",
            "should_prefer_queued_writer_over_new_reader",
            "Writer preference relies on a queued-writer marker that only the Redis provider keeps; the provider puts no such marker on a SQL Server application lock, so a new reader is granted while a writer waits."
        ),
        new(
            "SqliteDialectConformanceTests",
            "should_retry_engine_chosen_deadlock_victims_and_apply_each_call_once",
            "SQLite begins every write transaction IMMEDIATE and admits one writer per database file, so two transactions never hold one row each and wait on the other: the engine cannot choose a deadlock victim."
        ),
        new(
            "SqliteDialectConformanceTests",
            "should_surface_the_deadlock_after_the_attempt_cap_and_apply_nothing",
            "SQLite begins every write transaction IMMEDIATE and admits one writer per database file, so no deadlock can be forced on any attempt; busy waits are covered by the SQLite dialect's own tests."
        ),
    ];
}
