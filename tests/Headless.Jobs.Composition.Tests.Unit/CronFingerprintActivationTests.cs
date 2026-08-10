// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests;

public sealed class CronFingerprintActivationTests : TestBase
{
    [Fact]
    public async Task should_drain_one_forward_only_snapshot_before_activation_completes()
    {
        var firstCursor = new Guid("00000002-0000-0000-0000-000000000000");
        var highWatermark = new Guid("00000003-0000-0000-0000-000000000000");
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .RebaseStaleFingerprintsAsync(2, null, null, false, AbortToken)
            .Returns(
                new CronFingerprintSweepResult
                {
                    Scanned = 2,
                    Rebased = 1,
                    Deferred = 1,
                    LostFence = 0,
                    HasMore = true,
                    NextCursorId = firstCursor,
                    SnapshotHighWatermarkId = highWatermark,
                }
            );
        manager
            .RebaseStaleFingerprintsAsync(2, firstCursor, highWatermark, false, AbortToken)
            .Returns(
                new CronFingerprintSweepResult
                {
                    Scanned = 1,
                    Rebased = 1,
                    Deferred = 0,
                    LostFence = 0,
                    HasMore = false,
                    NextCursorId = highWatermark,
                    SnapshotHighWatermarkId = highWatermark,
                }
            );
        var hosted = _Create(manager);

        await hosted.DrainFingerprintSnapshotAsync(
            new SchedulerOptionsBuilder { FingerprintSweepBatchSize = 2 },
            AbortToken
        );

        await manager.Received(1).RebaseStaleFingerprintsAsync(2, null, null, false, AbortToken);
        await manager.Received(1).RebaseStaleFingerprintsAsync(2, firstCursor, highWatermark, false, AbortToken);
        await manager
            .Received(2)
            .RebaseStaleFingerprintsAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<Guid?>(), false, AbortToken);
    }

    [Fact]
    public async Task should_fail_closed_when_fingerprint_storage_fails()
    {
        var manager = Substitute.For<IInternalJobManager>();
        manager
            .RebaseStaleFingerprintsAsync(100, null, null, false, AbortToken)
            .Returns<Task<CronFingerprintSweepResult>>(_ => throw new InvalidOperationException("store unavailable"));
        var hosted = _Create(manager);

        var act = () => hosted.DrainFingerprintSnapshotAsync(new SchedulerOptionsBuilder(), AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("store unavailable");
    }

    private static JobsInitializationHostedService _Create(IInternalJobManager manager)
    {
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        var provider = services.BuildServiceProvider();
        return new JobsInitializationHostedService(
            provider,
            new JobFunctionRegistry(
                FrozenDictionary<string, JobFunctionRegistration>.Empty,
                FrozenDictionary<string, (string, Type)>.Empty,
                FrozenDictionary<string, JobFunctionDescriptor>.Empty,
                FrozenDictionary<string, JobFunctionDescriptor>.Empty,
                FrozenDictionary<Type, JobFunctionDescriptor>.Empty
            ),
            NullLogger<JobsInitializationHostedService>.Instance
        );
    }
}
