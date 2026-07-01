// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Tests.Fakes;

namespace Tests.RegularLocks;

public sealed class LeaseMonitorRegistryTests : TestBase
{
    private readonly FakeTimeProvider _timeProvider = new();
    private readonly List<LeaseMonitor> _monitors = [];

    [Fact]
    public void should_retry_registration_when_existing_bucket_was_already_removed()
    {
        // given
        var sut = _CreateRegistry();
        var resource = Faker.Random.AlphaNumeric(10);
        var originalMonitor = _CreateMonitor(resource, "original");
        var replacementMonitor = _CreateMonitor(resource, "replacement");
        sut.Register(resource, "original", originalMonitor);
        _MarkBucketRemoved(sut, resource);

        // when
        sut.Register(resource, "replacement", replacementMonitor);

        // then
        sut.GetMonitorCount(resource).Should().Be(1);
    }

    [Fact]
    public async Task should_remove_bucket_when_nudge_finds_only_collected_monitors()
    {
        // given
        var sut = _CreateRegistry();
        var resource = Faker.Random.AlphaNumeric(10);
        _RegisterMonitorAndDropReference(
            sut,
            _timeProvider,
            LoggerFactory.CreateLogger(nameof(LeaseMonitor)),
            resource,
            "abandoned"
        );
        _GetRawBucketCount(sut).Should().Be(1);

        // when
        for (var i = 0; i < 20 && _GetRawBucketCount(sut) != 0; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            sut.NudgeActive(resource);
            await Task.Yield();
        }

        // then
        _GetRawBucketCount(sut).Should().Be(0);
    }

    private LeaseMonitorRegistry _CreateRegistry() => new(LoggerFactory.CreateLogger(nameof(LeaseMonitorRegistry)));

    private LeaseMonitor _CreateMonitor(string resource, string leaseId)
    {
        var monitor = new LeaseMonitor(
            new FakeLeaseHandle
            {
                Resource = resource,
                LeaseId = leaseId,
                LeaseDuration = TimeSpan.FromMinutes(1),
                MonitoringCadence = TimeSpan.FromSeconds(1),
            },
            _timeProvider,
            LoggerFactory.CreateLogger(nameof(LeaseMonitor))
        );

        // Owned by the test: tracked here so the monitor is disposed at teardown (and does not
        // leak off the local — CA2000).
        _monitors.Add(monitor);

        return monitor;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _RegisterMonitorAndDropReference(
        LeaseMonitorRegistry registry,
        FakeTimeProvider timeProvider,
        ILogger logger,
        string resource,
        string leaseId
    )
    {
        // CA2000: intentionally not disposed — this test verifies the registry's WeakReference
        // safety net removes the bucket when an abandoned (leaked, undisposed) monitor is GC'd.
        var monitor = new LeaseMonitor(
            new FakeLeaseHandle
            {
                Resource = resource,
                LeaseId = leaseId,
                LeaseDuration = TimeSpan.FromMinutes(1),
                MonitoringCadence = TimeSpan.FromSeconds(1),
            },
            timeProvider,
            logger
        );

        registry.Register(resource, leaseId, monitor);
    }

    private static void _MarkBucketRemoved(LeaseMonitorRegistry registry, string resource)
    {
        var bucket = _GetBucket(registry, resource);
        var isRemovedField = bucket.GetType().GetField("_isRemoved", BindingFlags.Instance | BindingFlags.NonPublic);
        isRemovedField.Should().NotBeNull();
        isRemovedField.SetValue(bucket, true);
    }

    private static object _GetBucket(LeaseMonitorRegistry registry, string resource)
    {
        var activeMonitors = _GetActiveMonitors(registry);
        var tryGetValue = activeMonitors
            .GetType()
            .GetMethod(
                "TryGetValue",
                [typeof(string), activeMonitors.GetType().GenericTypeArguments[1].MakeByRefType()]
            );
        tryGetValue.Should().NotBeNull();
        var arguments = new object?[] { resource, null };
        ((bool)tryGetValue!.Invoke(activeMonitors, arguments)!).Should().BeTrue();
        arguments[1].Should().NotBeNull();

        return arguments[1]!;
    }

    private static int _GetRawBucketCount(LeaseMonitorRegistry registry)
    {
        var activeMonitors = _GetActiveMonitors(registry);
        var countProperty = activeMonitors.GetType().GetProperty("Count");
        countProperty.Should().NotBeNull();

        return (int)countProperty!.GetValue(activeMonitors)!;
    }

    private static object _GetActiveMonitors(LeaseMonitorRegistry registry)
    {
        var activeMonitorsField = typeof(LeaseMonitorRegistry).GetField(
            "_activeMonitors",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
        );
        activeMonitorsField.Should().NotBeNull();

        return activeMonitorsField!.GetValue(registry)!;
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        foreach (var monitor in _monitors)
        {
            await monitor.DisposeAsync().ConfigureAwait(false);
        }

        _monitors.Clear();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }
}
