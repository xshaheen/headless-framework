// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Caching;
using Headless.Idempotency;
using Headless.Idempotency.Caching;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

/// <summary>
/// What the cache provider does beyond the shared conformance contract: it refuses every enlisted call, lets the cache
/// expiry stand in for the purge, keeps generations growing when the record or the counter is evicted, and handles the
/// autonomous recovery-point paths the enlisted conformance scenarios cover for the other providers.
/// </summary>
public sealed class CacheIdempotencyRecordStoreTests : TestBase
{
    private static readonly IdempotencyFingerprint _Fingerprint = IdempotencyFingerprint.Compute("request-a");
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _Retention = TimeSpan.FromHours(1);
    private const string _Contract = "test-result.v1";
    private const string _Prefix = "headless:idempotency:";

    [Fact]
    public async Task should_refuse_every_enlisted_call_and_write_nothing()
    {
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        var key = _Key();
        var admitted = await _AdmitAsync(operations, key);

        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        Func<Task>[] enlisted =
        [
            async () => await unit.Idempotency.AdmitAsync(_Key(), _Fingerprint, cancellationToken: AbortToken),
            async () => await unit.Idempotency.FenceAsync(admitted, AbortToken),
            async () =>
                await unit.Idempotency.SetRecoveryPointAsync(admitted, "step", _Bytes("x"), _Contract, AbortToken),
            async () =>
                await unit.Idempotency.CompleteAsync(admitted, _Bytes("r"), _Contract, cancellationToken: AbortToken),
            async () => await unit.Idempotency.ReleaseAsync(admitted, AbortToken),
        ];

        foreach (var call in enlisted)
        {
            (await call.Should().ThrowAsync<InvalidOperationException>())
                .Which.Message.Should()
                .Contain("cannot commit or roll back with a unit of work");
        }

        unit.IsRetryPrevented.Should().BeFalse("a refused call never marks the unit");
        (await operations.RenewAsync(admitted, _Lease, AbortToken))
            .IsRenewed.Should()
            .BeTrue("the refused calls left the attempt's record as it was");
    }

    [Fact]
    public async Task should_delete_nothing_on_purge_because_the_cache_expiry_is_the_purge()
    {
        await using var services = _CreateServices();
        var store = services.GetRequiredService<IIdempotencyRecordStore>();

        (await store.PurgeAsync(TimeSpan.Zero, 100, AbortToken)).Should().Be(0);
    }

    [Fact]
    public async Task should_keep_the_entry_until_the_later_of_its_retention_and_its_lease()
    {
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var cache = services.GetRequiredService<ICache>();
        var key = _Key();
        var cacheKey = CacheIdempotencyRecordStore.RecordKey(_Prefix, new IdempotencyRecordKey("", key));

        var admitted = await operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: TimeSpan.FromMinutes(30),
            retention: TimeSpan.FromMinutes(10),
            cancellationToken: AbortToken
        );

        (await cache.GetExpirationAsync(cacheKey, AbortToken))
            .Should()
            .BeGreaterThan(TimeSpan.FromMinutes(29), "a live attempt must still find its record when it completes");

        await operations.CompleteAsync(admitted, _Bytes("done"), _Contract, cancellationToken: AbortToken);

        (await cache.GetExpirationAsync(cacheKey, AbortToken))
            .Should()
            .BeLessThanOrEqualTo(TimeSpan.FromMinutes(30))
            .And.BeGreaterThan(TimeSpan.FromMinutes(9), "a completed record lives for its retention");
    }

    [Fact]
    public async Task should_admit_a_higher_generation_and_refuse_the_zombie_after_the_entry_was_evicted()
    {
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var cache = services.GetRequiredService<ICache>();
        var key = _Key();

        var first = await _AdmitAsync(operations, key);
        await cache.RemoveAsync(
            CacheIdempotencyRecordStore.RecordKey(_Prefix, new IdempotencyRecordKey("", key)),
            AbortToken
        );

        var second = await _AdmitAsync(operations, key);

        second.IsAdmitted.Should().BeTrue();
        second.IsTakeover.Should().BeFalse("an evicted record names no unfinished attempt");
        second.Generation.Should().BeGreaterThan(first.Generation!.Value, "generations come from the store counter");

        var zombie = async () =>
            await operations.CompleteAsync(first, _Bytes("zombie"), _Contract, cancellationToken: AbortToken);

        (await zombie.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Stale);
    }

    [Fact]
    public async Task should_draw_above_the_record_generation_when_the_counter_was_evicted()
    {
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var cache = services.GetRequiredService<ICache>();
        var key = _Key();

        // Several draws first, so a restarted counter would repeat a generation this record already used.
        for (var i = 0; i < 3; i++)
        {
            await _AdmitAsync(operations, _Key());
        }

        var first = await operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: TimeSpan.FromSeconds(1),
            retention: _Retention,
            cancellationToken: AbortToken
        );
        await operations.ReleaseAsync(first, AbortToken);
        var released = await _AdmitAsync(operations, key);
        await cache.RemoveAsync(CacheIdempotencyRecordStore.GenerationKey(_Prefix), AbortToken);

        await operations.ReleaseAsync(released, AbortToken);
        var next = await _AdmitAsync(operations, key);

        next.Generation.Should().BeGreaterThan(released.Generation!.Value);
        (await operations.ReleaseAsync(released, AbortToken)).Should().Be(IdempotentLeaseStatus.Stale);

        // The counter was raised past the record, so the next draw anywhere does not repeat it.
        var other = await _AdmitAsync(operations, _Key());
        other.Generation.Should().BeGreaterThan(next.Generation!.Value);
    }

    [Fact]
    public async Task should_refuse_a_recovery_point_from_an_attempt_that_lost_the_key()
    {
        var time = new FakeTimeProvider();
        await using var services = _CreateServices(time);
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var key = _Key();

        var first = await operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: TimeSpan.FromSeconds(1),
            retention: _Retention,
            cancellationToken: AbortToken
        );
        time.Advance(TimeSpan.FromSeconds(1.2));

        var expired = async () =>
            await operations.SetRecoveryPointAsync(first, "late", _Bytes("x"), _Contract, AbortToken);

        (await expired.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Expired);

        var second = await _AdmitAsync(operations, key);
        second.IsTakeover.Should().BeTrue();
        second.RecoveryPoint.Should().BeNull("the refused point was never written");

        (await expired.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Stale);
    }

    [Fact]
    public async Task should_hand_a_takeover_the_last_point_and_clear_it_on_completion()
    {
        var time = new FakeTimeProvider();
        await using var services = _CreateServices(time);
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var key = _Key();

        var first = await operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: TimeSpan.FromSeconds(1),
            retention: _Retention,
            cancellationToken: AbortToken
        );
        await operations.SetRecoveryPointAsync(first, "reserved", _Bytes("r-1"), _Contract, AbortToken);
        await operations.SetRecoveryPointAsync(first, "charged", _Bytes("c-1"), _Contract, AbortToken);
        time.Advance(TimeSpan.FromSeconds(1.2));

        var second = await _AdmitAsync(operations, key);

        second.IsTakeover.Should().BeTrue();
        second.RecoveryPoint!.Name.Should().Be("charged");
        Encoding.UTF8.GetString(second.RecoveryPoint.State.Span).Should().Be("c-1");

        await operations.CompleteAsync(second, _Bytes("done"), _Contract, cancellationToken: AbortToken);
        var replay = await _AdmitAsync(operations, key);

        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        replay.RecoveryPoint.Should().BeNull("a completion clears the point");
    }

    [Fact]
    public async Task should_resolve_a_named_cache_and_refuse_a_missing_one()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessCaching(static setup =>
        {
            setup.UseInMemory();
            setup.AddNamed("records", static instance => instance.UseInMemory(static _ => { }));
        });
        services.AddHeadlessIdempotency(static setup => setup.UseCache(static o => o.CacheName = "records"));
        await using var provider = services.BuildServiceProvider();

        var key = _Key();
        await _AdmitAsync(provider.GetRequiredService<IIdempotentOperations>(), key);
        var cacheKey = CacheIdempotencyRecordStore.RecordKey(_Prefix, new IdempotencyRecordKey("", key));

        (await provider.GetRequiredKeyedService<ICache>("records").ExistsAsync(cacheKey, AbortToken)).Should().BeTrue();
        (await provider.GetRequiredService<ICache>().ExistsAsync(cacheKey, AbortToken)).Should().BeFalse();

        var missing = new ServiceCollection();
        missing.AddLogging();
        missing.AddHeadlessCaching(static setup => setup.UseInMemory());
        missing.AddHeadlessIdempotency(static setup => setup.UseCache(static o => o.CacheName = "absent"));
        await using var missingProvider = missing.BuildServiceProvider();

        var resolve = () => missingProvider.GetRequiredService<IIdempotencyRecordStore>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*'absent'*");
    }

    [Fact]
    public void should_name_use_cache_among_the_providers_when_none_is_chosen()
    {
        var none = () => new ServiceCollection().AddHeadlessIdempotency(static _ => { });

        none.Should().Throw<InvalidOperationException>().WithMessage("*`UseCache`*");
    }

    [Fact]
    public void should_refuse_a_second_provider_next_to_use_cache()
    {
        var both = () =>
            new ServiceCollection().AddHeadlessIdempotency(static setup =>
            {
                setup.UseCache();
                setup.UseCache();
            });

        both.Should().Throw<InvalidOperationException>().WithMessage("*exactly one storage provider*");
    }

    private static ServiceProvider _CreateServices(FakeTimeProvider? time = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (time is not null)
        {
            services.AddSingleton<TimeProvider>(time);
        }

        services.AddHeadlessCaching(static setup => setup.UseInMemory());
        services.AddHeadlessIdempotency(static setup =>
        {
            setup.UseCache();
            setup.ConfigureOptions(static options => options.PurgeInterval = null);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<IdempotentAdmission> _AdmitAsync(IIdempotentOperations operations, string key)
    {
        return await operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: _Lease,
            retention: _Retention,
            cancellationToken: AbortToken
        );
    }

    private static string _Key()
    {
        return $"key-{Guid.NewGuid():N}";
    }

    private static ReadOnlyMemory<byte> _Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }
}
