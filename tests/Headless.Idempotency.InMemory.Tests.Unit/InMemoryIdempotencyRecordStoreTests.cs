// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class InMemoryIdempotencyRecordStoreTests : TestBase
{
    private const string _Contract = "test-result.v1";
    private static readonly TimeSpan _Lease = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _Retention = TimeSpan.FromHours(1);
    private static readonly IdempotencyFingerprint _Fingerprint = IdempotencyFingerprint.Compute("request-a");
    private static readonly IdempotencyFingerprint _OtherFingerprint = IdempotencyFingerprint.Compute("request-b");

    /// <summary>Long enough that a call not blocked on a held key has certainly returned.</summary>
    private static readonly TimeSpan _Returns = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task should_decide_leases_and_retention_by_the_injected_clock()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        await using var services = _CreateServices(clock);
        var operations = services.GetRequiredService<IIdempotentOperations>();

        // when
        var first = await _AdmitAsync(operations, _Fingerprint);
        clock.Advance(TimeSpan.FromSeconds(59));
        var inFlight = await _AdmitAsync(operations, _Fingerprint);
        clock.Advance(TimeSpan.FromSeconds(1));
        var takeover = await _AdmitAsync(operations, _Fingerprint);
        await operations.CompleteAsync(
            takeover,
            Encoding.UTF8.GetBytes("done"),
            _Contract,
            cancellationToken: AbortToken
        );
        var replay = await _AdmitAsync(operations, _Fingerprint);
        clock.Advance(_Retention);
        var peek = await operations.PeekAsync("key-1", AbortToken);
        var fresh = await _AdmitAsync(operations, _OtherFingerprint);

        // then — an expiry at the clock's instant is already over, for leases and retention alike
        first.LeaseExpiresAt.Should().Be(new DateTimeOffset(2026, 9, 26, 12, 1, 0, TimeSpan.Zero));
        inFlight.Disposition.Should().Be(IdempotentDisposition.InFlight);
        takeover.IsTakeover.Should().BeTrue();
        takeover.Generation.Should().BeGreaterThan(first.Generation!.Value);
        replay.Disposition.Should().Be(IdempotentDisposition.Replay);
        peek.Should().Be(IdempotencyPeekStatus.Absent);
        fresh.IsAdmitted.Should().BeTrue("the completed record's retention ended");
        fresh.IsTakeover.Should().BeFalse();
    }

    [Fact]
    public async Task should_admit_exactly_one_of_many_racing_admissions_in_one_process()
    {
        // given
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var racers = Enumerable
            .Range(0, 64)
            .Select(_ =>
                Task.Run(
                    async () =>
                    {
                        await start.Task;

                        return await _AdmitAsync(operations, _Fingerprint);
                    },
                    AbortToken
                )
            )
            .ToList();

        // when
        start.SetResult();
        var results = await Task.WhenAll(racers);

        // then
        var winner = results.Should().ContainSingle(static r => r.IsAdmitted).Subject;
        results
            .Where(static r => !r.IsAdmitted)
            .Should()
            .HaveCount(63)
            .And.AllSatisfy(r =>
            {
                r.Disposition.Should().Be(IdempotentDisposition.InFlight);
                r.Generation.Should().Be(winner.Generation);
            });
    }

    [Fact]
    public async Task should_refuse_a_unit_over_a_database_connection_before_any_write()
    {
        // given
        await using var services = _CreateServices();
        var enlisted = services.GetRequiredService<IUnitOfWorkIdempotency>();
        var unit = Substitute.For<IUnitOfWork>();
        unit.State.Returns(UnitOfWorkState.Active);
        unit.Resource.Returns(Substitute.For<IRelationalUnitOfWorkResource>());

        // when
        var act = async () => await enlisted.AdmitAsync(unit, "key-1", _Fingerprint, cancellationToken: AbortToken);

        // then — records in this process cannot commit or roll back with a database transaction
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*database transaction*InMemory*");
        unit.DidNotReceive().PreventRetry();
        (await services.GetRequiredService<IIdempotentOperations>().PeekAsync("key-1", AbortToken))
            .Should()
            .Be(IdempotencyPeekStatus.Absent, "the refused call wrote nothing");
    }

    [Fact]
    public async Task should_let_one_unit_fence_and_then_complete_the_same_record()
    {
        // given
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        var admitted = await _AdmitAsync(operations, _Fingerprint);

        // when — the unit already holds the record after the fence, so the completion must not wait on itself
        await using (var unit = await factory.BeginAsync(cancellationToken: AbortToken))
        {
            await unit.Idempotency.FenceAsync(admitted, AbortToken);
            await unit
                .Idempotency.CompleteAsync(
                    admitted,
                    Encoding.UTF8.GetBytes("done"),
                    _Contract,
                    cancellationToken: AbortToken
                )
                .AsTask()
                .WaitAsync(_Returns, AbortToken);
            await unit.CompleteAsync(AbortToken);
        }

        // then
        var replay = await _AdmitAsync(operations, _Fingerprint).WaitAsync(_Returns, AbortToken);
        replay.Disposition.Should().Be(IdempotentDisposition.Replay, "the completed unit released the record");
        Encoding.UTF8.GetString(replay.Result!.Payload.Span).Should().Be("done");
    }

    [Fact]
    public async Task should_release_the_record_and_drop_the_writes_when_a_unit_is_disposed_without_completing()
    {
        // given
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        long abandoned;

        // when
        await using (var unit = await factory.BeginAsync(cancellationToken: AbortToken))
        {
            var admission = await unit.Idempotency.AdmitAsync(
                "key-1",
                _Fingerprint,
                leaseDuration: _Lease,
                retention: _Retention,
                cancellationToken: AbortToken
            );
            abandoned = admission.Generation!.Value;
        }

        // then
        var next = await _AdmitAsync(operations, _Fingerprint).WaitAsync(_Returns, AbortToken);
        next.IsAdmitted.Should().BeTrue();
        next.IsTakeover.Should().BeFalse("the abandoned unit left no record behind");
        next.Generation.Should().BeGreaterThan(abandoned);
    }

    [Fact]
    public async Task should_release_the_record_when_a_unit_rolls_back()
    {
        // given
        await using var services = _CreateServices();
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        var admitted = await _AdmitAsync(operations, _Fingerprint);

        // when
        await using (var unit = await factory.BeginAsync(cancellationToken: AbortToken))
        {
            (await unit.Idempotency.ReleaseAsync(admitted, AbortToken)).Should().Be(IdempotentLeaseStatus.Released);
            await unit.RollbackAsync();
        }

        // then — the release never happened, and the record is free for the next call at once
        var renewed = await operations
            .RenewAsync(admitted, _Lease, AbortToken)
            .AsTask()
            .WaitAsync(_Returns, AbortToken);
        renewed.IsRenewed.Should().BeTrue();
    }

    [Fact]
    public async Task should_skip_a_record_a_unit_holds_and_purge_it_once_the_unit_ends()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        await using var services = _CreateServices(clock);
        var operations = services.GetRequiredService<IIdempotentOperations>();
        var store = services.GetRequiredService<IIdempotencyRecordStore>();
        var factory = services.GetRequiredService<IUnitOfWorkFactory>();
        var admitted = await _AdmitAsync(operations, _Fingerprint);
        await operations.CompleteAsync(
            admitted,
            Encoding.UTF8.GetBytes("done"),
            _Contract,
            cancellationToken: AbortToken
        );
        clock.Advance(_Retention);

        // when
        int whileHeld;

        await using (var unit = await factory.BeginAsync(cancellationToken: AbortToken))
        {
            // The admission resets the expired record inside the unit, holding its lock until the unit ends without
            // completing, so the reset is dropped and the old record stays for the purge.
            (await unit.Idempotency.AdmitAsync("key-1", _Fingerprint, cancellationToken: AbortToken))
                .IsAdmitted.Should()
                .BeTrue();
            whileHeld = await store.PurgeAsync(TimeSpan.Zero, 100, AbortToken);
        }

        var afterwards = await store.PurgeAsync(TimeSpan.Zero, 100, AbortToken);

        // then
        whileHeld.Should().Be(0, "a record a unit holds is left for a later purge");
        afterwards.Should().Be(1);
        (await operations.PeekAsync("key-1", AbortToken)).Should().Be(IdempotencyPeekStatus.Absent);
    }

    [Fact]
    public void should_name_the_in_memory_provider_and_refuse_a_second_one()
    {
        // when
        var none = () => new ServiceCollection().AddHeadlessIdempotency(static _ => { });
        var two = () =>
            new ServiceCollection().AddHeadlessIdempotency(static setup =>
            {
                setup.UseInMemory();
                setup.UseInMemory();
            });

        // then
        none.Should().Throw<InvalidOperationException>().WithMessage("*`UseInMemory`*");
        two.Should().Throw<InvalidOperationException>().WithMessage("*Multiple storage providers*");
    }

    private Task<IdempotentAdmission> _AdmitAsync(IIdempotentOperations operations, IdempotencyFingerprint fingerprint)
    {
        return operations
            .AdmitAsync(
                "key-1",
                fingerprint,
                leaseDuration: _Lease,
                retention: _Retention,
                cancellationToken: AbortToken
            )
            .AsTask();
    }

    private static ServiceProvider _CreateServices(TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddHeadlessIdempotency(setup =>
        {
            setup.UseInMemory();
            setup.ConfigureOptions(static options => options.PurgeInterval = null);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
