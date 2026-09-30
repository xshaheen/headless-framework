// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Idempotency;
using Headless.Idempotency.Caching;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The cache provider over a real Redis, with two hosts on separate connections standing in for two replicas: the
/// admission race, the zombie refusal after a takeover, replay and conflict across hosts, retention by the cache's own
/// expiry, and the refusal of enlisted calls.
/// </summary>
[Collection<RedisIdempotencyFixture>]
public sealed class RedisCacheIdempotencyTests(RedisIdempotencyFixture fixture) : TestBase
{
    private static readonly IdempotencyFingerprint _Fingerprint = IdempotencyFingerprint.Compute("request-a");
    private static readonly IdempotencyFingerprint _OtherFingerprint = IdempotencyFingerprint.Compute("request-b");
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan _Retention = TimeSpan.FromHours(1);
    private const string _Contract = "test-result.v1";

    [Fact]
    public async Task should_admit_exactly_one_of_sixteen_parallel_admissions_across_two_hosts()
    {
        var prefix = _Prefix();
        await using var hostA = await fixture.CreateHostAsync(prefix, AbortToken);
        await using var hostB = await fixture.CreateHostAsync(prefix, AbortToken);

        for (var round = 0; round < 5; round++)
        {
            var key = _Key();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var racers = Enumerable
                .Range(0, 16)
                .Select(i =>
                    Task.Run(
                        async () =>
                        {
                            await start.Task;

                            return await _AdmitAsync(i % 2 == 0 ? hostA : hostB, key);
                        },
                        AbortToken
                    )
                )
                .ToList();

            start.SetResult();
            var results = await Task.WhenAll(racers);

            results.Where(static r => r.IsAdmitted).Should().ContainSingle($"round {round} has one winner");
            results
                .Where(static r => !r.IsAdmitted)
                .Should()
                .HaveCount(15)
                .And.AllSatisfy(r => r.Disposition.Should().Be(IdempotentDisposition.InFlight));
        }
    }

    [Fact]
    public async Task should_refuse_the_zombie_completion_after_another_host_takes_over_an_expired_lease()
    {
        var prefix = _Prefix();
        var key = _Key();
        await using var hostA = await fixture.CreateHostAsync(prefix, AbortToken);
        await using var hostB = await fixture.CreateHostAsync(prefix, AbortToken);

        var first = await hostA.Operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: TimeSpan.FromSeconds(1),
            retention: _Retention,
            cancellationToken: AbortToken
        );
        await Task.Delay(TimeSpan.FromSeconds(1.5), AbortToken);

        var second = await _AdmitAsync(hostB, key);

        second.IsAdmitted.Should().BeTrue();
        second.IsTakeover.Should().BeTrue("the first attempt's lease expired without a completion or release");
        second.Generation.Should().BeGreaterThan(first.Generation!.Value);

        var zombie = async () =>
            await hostA.Operations.CompleteAsync(first, _Bytes("first"), _Contract, cancellationToken: AbortToken);

        (await zombie.Should().ThrowAsync<StaleAdmissionException>())
            .Which.Reason.Should()
            .Be(IdempotentLeaseStatus.Stale);

        await hostB.Operations.CompleteAsync(second, _Bytes("second"), _Contract, cancellationToken: AbortToken);
        await zombie.Should().ThrowAsync<StaleAdmissionException>("the zombie stays refused after the completion");

        var replay = await _AdmitAsync(hostA, key);
        _Text(replay.Result!.Payload).Should().Be("second", "the zombie never overwrote the newer result");
    }

    [Fact]
    public async Task should_replay_on_one_host_what_another_host_completed_and_refuse_another_fingerprint()
    {
        var prefix = _Prefix();
        var key = _Key();
        await using var hostA = await fixture.CreateHostAsync(prefix, AbortToken);
        await using var hostB = await fixture.CreateHostAsync(prefix, AbortToken);

        var admitted = await _AdmitAsync(hostA, key);
        (await _AdmitAsync(hostB, key)).Disposition.Should().Be(IdempotentDisposition.InFlight);
        await hostA.Operations.CompleteAsync(admitted, _Bytes("stored"), _Contract, cancellationToken: AbortToken);

        var replay = await _AdmitAsync(hostB, key);

        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        _Text(replay.Result!.Payload).Should().Be("stored");
        replay.Result.Contract.Should().Be(_Contract);
        (await hostB.Operations.PeekAsync(key, AbortToken)).Should().Be(IdempotencyPeekStatus.Completed);

        var conflict = await hostB.Operations.AdmitAsync(
            key,
            _OtherFingerprint,
            retention: _Retention,
            cancellationToken: AbortToken
        );

        conflict.Disposition.Should().Be(IdempotentDisposition.Conflict);
        conflict.StoredFingerprint.Should().Be(_Fingerprint);
    }

    [Fact]
    public async Task should_drop_the_record_when_its_retention_ends_in_the_cache()
    {
        var prefix = _Prefix();
        var key = _Key();
        await using var host = await fixture.CreateHostAsync(prefix, AbortToken);
        var cacheKey = CacheIdempotencyRecordStore.RecordKey(prefix, new IdempotencyRecordKey("", key));

        var first = await host.Operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: TimeSpan.FromSeconds(1),
            retention: TimeSpan.FromSeconds(1),
            cancellationToken: AbortToken
        );
        await host.Operations.CompleteAsync(
            first,
            _Bytes("short-lived"),
            _Contract,
            retention: TimeSpan.FromSeconds(1),
            cancellationToken: AbortToken
        );

        (await host.Cache.ExistsAsync(cacheKey, AbortToken)).Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(2), AbortToken);

        (await host.Cache.ExistsAsync(cacheKey, AbortToken)).Should().BeFalse("Redis expired the entry");
        (await host.Operations.PeekAsync(key, AbortToken)).Should().Be(IdempotencyPeekStatus.Absent);

        var next = await _AdmitAsync(host, key);

        next.IsAdmitted.Should().BeTrue("the expired record no longer binds the key");
        next.IsTakeover.Should().BeFalse();
        next.Generation.Should().BeGreaterThan(first.Generation!.Value, "the generation counter outlives records");
    }

    [Fact]
    public async Task should_refuse_an_enlisted_call()
    {
        await using var host = await fixture.CreateHostAsync(_Prefix(), AbortToken);
        var factory = host.Services.GetRequiredService<IUnitOfWorkFactory>();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        var enlisted = async () =>
            await unit.Idempotency.AdmitAsync(_Key(), _Fingerprint, cancellationToken: AbortToken);

        (await enlisted.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("UsePostgreSql, UseSqlServer, or UseInMemory");
    }

    private static async Task<IdempotentAdmission> _AdmitAsync(RedisIdempotencyHost host, string key)
    {
        return await host.Operations.AdmitAsync(
            key,
            _Fingerprint,
            leaseDuration: _Lease,
            retention: _Retention,
            cancellationToken: AbortToken
        );
    }

    private static string _Prefix()
    {
        return $"it-{Guid.NewGuid():N}:";
    }

    private static string _Key()
    {
        return $"key-{Guid.NewGuid():N}";
    }

    private static ReadOnlyMemory<byte> _Bytes(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    private static string _Text(ReadOnlyMemory<byte> payload)
    {
        return Encoding.UTF8.GetString(payload.Span);
    }
}
