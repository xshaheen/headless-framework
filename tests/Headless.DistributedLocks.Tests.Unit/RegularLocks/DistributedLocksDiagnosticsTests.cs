// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Abstractions;
using Headless.DistributedLocks;
using Headless.DistributedLocks.InMemory;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;

namespace Tests.RegularLocks;

public sealed class DistributedLocksDiagnosticsTests : TestBase
{
    private readonly IGuidGenerator _guidGenerator = Substitute.For<IGuidGenerator>();

    public DistributedLocksDiagnosticsTests()
    {
        _guidGenerator.Create().Returns(_ => Guid.NewGuid());
    }

    private (DistributedLock Provider, InMemoryDistributedLockStorage Storage) _CreateProvider()
    {
        var tp = TimeProvider.System;
        var storage = new InMemoryDistributedLockStorage(tp);
        var provider = new DistributedLock(
            storage,
            outboxBus: null,
            new DistributedLockOptions(),
            _guidGenerator,
            tp,
            LoggerFactory.CreateLogger<DistributedLock>()
        );
        return (provider, storage);
    }

    [Fact]
    public async Task should_emit_expected_telemetry_on_successful_acquire()
    {
        // given
        var (provider, _) = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        var activities = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener();
        activityListener.ShouldListenTo = source =>
            string.Equals(source.Name, "Headless.DistributedLocks", StringComparison.Ordinal);
        activityListener.Sample = static (ref _) => ActivitySamplingResult.AllData;
        activityListener.ActivityStopped = activities.Add;
        ActivitySource.AddActivityListener(activityListener);

        var lockFailedCount = 0;
        var waitTimeRecorded = 0;
        using var meterListener = new MeterListener();

        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, "Headless.DistributedLocks", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, state) =>
            {
                if (string.Equals(instrument.Name, "headless.lock.failed", StringComparison.Ordinal))
                {
                    Interlocked.Add(ref lockFailedCount, measurement);
                }
            }
        );
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, state) =>
            {
                if (string.Equals(instrument.Name, "headless.lock.wait.time", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref waitTimeRecorded);
                }
            }
        );
        meterListener.Start();

        // when
        await using (var handle = await provider.AcquireAsync(resource, cancellationToken: AbortToken))
        {
            handle.Should().NotBeNull();
        }

        // then
        var myActivities = activities
            .Where(a =>
                string.Equals((string?)a.GetTagItem("headless.lock.resource"), resource, StringComparison.Ordinal)
            )
            .ToList();
        myActivities.Should().ContainSingle();
        var activity = myActivities.Single();
        activity.OperationName.Should().Be("lock.acquire");
        activity.DisplayName.Should().Be($"Lock: {resource}");
        activity.GetTagItem("headless.lock.resource").Should().Be(resource);

        waitTimeRecorded.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task should_emit_expected_telemetry_on_timeout_failure()
    {
        // given
        var (provider, _) = _CreateProvider();
        var resource = Faker.Random.AlphaNumeric(10);

        // hold lock first so subsequent acquire times out
        await using var heldLock = await provider.AcquireAsync(resource, cancellationToken: AbortToken);

        var activities = new ConcurrentBag<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, "Headless.DistributedLocks", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(activityListener);

        var lockFailedCount = 0;
        var waitTimeRecorded = 0;
        using var meterListener = new MeterListener();

        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, "Headless.DistributedLocks", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, state) =>
            {
                if (string.Equals(instrument.Name, "headless.lock.failed", StringComparison.Ordinal))
                {
                    Interlocked.Add(ref lockFailedCount, measurement);
                }
            }
        );
        meterListener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, state) =>
            {
                if (string.Equals(instrument.Name, "headless.lock.wait.time", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref waitTimeRecorded);
                }
            }
        );
        meterListener.Start();

        // when
        var act = async () =>
            await provider.AcquireAsync(
                resource,
                new DistributedLockAcquireOptions { AcquireTimeout = TimeSpan.FromMilliseconds(5) },
                AbortToken
            );

        // then
        await act.Should().ThrowAsync<LockAcquisitionTimeoutException>();

        var myActivities = activities
            .Where(a =>
                string.Equals((string?)a.GetTagItem("headless.lock.resource"), resource, StringComparison.Ordinal)
            )
            .ToList();
        myActivities.Should().ContainSingle();
        var activity = myActivities.Single();
        activity.OperationName.Should().Be("lock.acquire");
        activity.DisplayName.Should().Be($"Lock: {resource}");
        activity.GetTagItem("headless.lock.resource").Should().Be(resource);

        lockFailedCount.Should().BeGreaterThanOrEqualTo(1);
        waitTimeRecorded.Should().BeGreaterThanOrEqualTo(1);
    }
}
