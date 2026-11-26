// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;
using Headless.Testing.Tests;
using static Tests.IdempotencyTestContext;

namespace Tests;

public sealed class IdempotentOperationsInspectTests : TestBase
{
    [Fact]
    public async Task should_inspect_the_current_tenant_record_through_the_store()
    {
        // given
        var context = new IdempotencyTestContext();
        var snapshot = new IdempotencyRecordSnapshot(
            RecordKey.ToKey(),
            IdempotencyPeekStatus.Completed,
            Generation,
            TakeoverCount: 0,
            Fingerprint,
            LeaseExpiresAt: null,
            IsLeaseExpired: false,
            RetentionUntil: ExpiresAt,
            ObservedAt: ExpiresAt.AddHours(-1),
            ResultContract: "c.v1",
            ResultSizeBytes: 12,
            RecoveryPoint: null,
            RecoveryContract: null,
            RecoveryStateSizeBytes: null
        );
        context.Store.InspectAsync(RecordKey, AbortToken).Returns(snapshot);

        // when
        var result = await context.Operations.InspectAsync(Key, AbortToken);

        // then - the key resolves against the current tenant, and nothing but the lock-free read runs
        result.Should().BeSameAs(snapshot);
        await context.Store.DidNotReceiveWithAnyArgs().BeginOwnedUnitAsync(AbortToken);
        await context.Store.DidNotReceiveWithAnyArgs().GetResultAsync(RecordKey, AbortToken);
    }

    [Fact]
    public async Task should_refuse_an_invalid_key_before_the_store_is_called()
    {
        // given
        var context = new IdempotencyTestContext();

        // when
        var act = async () => await context.Operations.InspectAsync(" ", AbortToken);

        // then
        await act.Should().ThrowAsync<ArgumentException>();
        await context.Store.DidNotReceiveWithAnyArgs().InspectAsync(RecordKey, AbortToken);
    }

    [Fact]
    public void should_describe_the_store_and_the_current_options()
    {
        // given
        var context = new IdempotencyTestContext();
        context.Store.Info.Returns(
            new IdempotencyStoreInfo("Headless.Idempotency.PostgreSql", "headless.records", true)
        );
        context.Options.DefaultRetention = TimeSpan.FromHours(3);
        context.Options.PurgeInterval = TimeSpan.FromMinutes(10);
        context.Options.PurgeBatchSize = 50;

        // when
        var description = context.Operations.Describe();

        // then
        description
            .Should()
            .Be(
                new IdempotencyStoreDescription(
                    "Headless.Idempotency.PostgreSql",
                    "headless.records",
                    TimeSpan.FromHours(3),
                    context.Options.DefaultLeaseDuration,
                    context.Options.MinimumLeaseDuration,
                    context.Options.MaximumLeaseDuration,
                    TimeSpan.FromMinutes(10),
                    50
                )
            );
    }

    [Fact]
    public void should_describe_no_purge_schedule_when_the_store_purges_nothing()
    {
        // given
        var context = new IdempotencyTestContext();
        context.Store.Info.Returns(new IdempotencyStoreInfo("Headless.Idempotency.Caching", "p:", false));
        context.Options.PurgeInterval = TimeSpan.FromMinutes(10);

        // when
        var description = context.Operations.Describe();

        // then - its entries expire in the cache, so a schedule would describe work that never happens
        description.PurgeInterval.Should().BeNull();
    }

    [Fact]
    public void should_carry_no_result_or_recovery_state_bytes_in_a_snapshot()
    {
        // given
        Type[] payloadTypes =
        [
            typeof(byte[]),
            typeof(ReadOnlyMemory<byte>),
            typeof(Memory<byte>),
            typeof(IdempotentResult),
            typeof(IdempotentRecoveryPoint),
        ];

        // when
        var properties = typeof(IdempotencyRecordSnapshot).GetProperties();

        // then - the only bytes are the fingerprint digest, which identifies the request rather than its response
        properties
            .Select(static p => Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType)
            .Should()
            .NotContain(payloadTypes);
    }
}
