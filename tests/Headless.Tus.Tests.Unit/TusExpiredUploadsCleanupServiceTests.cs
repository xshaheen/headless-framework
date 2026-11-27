// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Tests.Endpoint;
using tusdotnet.FileLocks;
using tusdotnet.Interfaces;
using tusdotnet.Stores;
using tusdotnet.Stores.FileIdProviders;

namespace Tests;

/// <summary>A store that can expire uploads, as one type NSubstitute can stand in for.</summary>
public interface IExpiringTusStore : ITusStore, ITusExpirationStore;

public sealed class TusExpiredUploadsCleanupServiceTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Advances fake time by one interval once the service has armed its interval timer, then waits for the signal.
    /// An advance that lands before the timer exists is lost, because the timer is created against the later time.
    /// </summary>
    private static async Task _AdvanceUntilAsync(TimerArmedTimeProvider timeProvider, Task signal)
    {
        await timeProvider.TimerArmed.Bounded();
        timeProvider.Advance(_Interval);
        await signal.Bounded();
    }

    /// <summary>A fake clock that reports when the service creates its periodic timer.</summary>
    private sealed class TimerArmedTimeProvider : FakeTimeProvider
    {
        private readonly TaskCompletionSource _timerArmed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TimerArmed => _timerArmed.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _timerArmed.TrySetResult();

            return timer;
        }
    }

    private static TusExpiredUploadsCleanupService _CreateService(
        ITusStore store,
        FakeTimeProvider timeProvider,
        ITusExpiredUploadsCleanupGate? gate = null,
        bool enabled = true
    )
    {
        return new TusExpiredUploadsCleanupService(
            store,
            Options.Create(new TusExpiredUploadsCleanupOptions { Interval = _Interval, Enabled = enabled }),
            timeProvider,
            NullLogger<TusExpiredUploadsCleanupService>.Instance,
            gate
        );
    }

    [Fact]
    public async Task should_skip_the_pass_when_another_node_claimed_it()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var gate = new _FakeGate(claims: false);

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, gate);

        // when
        await service.StartAsync(AbortToken);
        await gate.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await service.StopAsync(AbortToken);

        // then
        await store.DidNotReceive().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_run_the_pass_and_hold_the_claim_for_most_of_the_interval_when_claimed()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var firstPass = new TaskCompletionSource();
        store.RemoveExpiredFilesAsync(Arg.Any<CancellationToken>()).Returns(1).AndDoes(_ => firstPass.TrySetResult());
        var gate = new _FakeGate(claims: true);

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, gate);

        // when
        await service.StartAsync(AbortToken);
        await firstPass.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await service.StopAsync(AbortToken);

        // then
        gate.HoldFor.Should().Be(_Interval - (_Interval / 10));
    }

    // A hand-written fake: the gate interface is internal, which NSubstitute's proxy assembly cannot see.
    private sealed class _FakeGate(bool claims) : ITusExpiredUploadsCleanupGate
    {
        public TaskCompletionSource Attempted { get; } = new();

        public TimeSpan? HoldFor { get; private set; }

        public Task<bool> TryClaimPassAsync(TimeSpan holdFor, CancellationToken cancellationToken)
        {
            HoldFor = holdFor;
            Attempted.TrySetResult();

            return Task.FromResult(claims);
        }
    }

    [Fact]
    public void should_resolve_without_a_gate_when_no_lock_provider_is_registered()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessTus(tus => tus.UseStore(Substitute.For<IExpiringTusStore>()));

        // when
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetService<ITusExpiredUploadsCleanupGate>().Should().BeNull();
        provider
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should()
            .ContainSingle(service => service is TusExpiredUploadsCleanupService);
    }

    [Fact]
    public async Task should_end_without_a_pass_when_disabled()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, enabled: false);

        // when - the loop ends at once, so no interval has to pass
        await service.StartAsync(AbortToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then
        await store.DidNotReceive().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_end_without_a_pass_when_the_store_cannot_expire_uploads()
    {
        // given - a store with no ITusExpirationStore, and a gate that would record a claim attempt
        var store = Substitute.For<ITusStore>();
        var gate = new _FakeGate(claims: true);
        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider, gate);

        // when
        await service.StartAsync(AbortToken);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);

        // then - it never competed for the pass it could not run
        gate.HoldFor.Should().BeNull();
    }

    [Fact]
    public async Task should_run_a_cleanup_pass_immediately_at_startup()
    {
        // given
        var store = Substitute.For<IExpiringTusStore>();
        var firstPass = new TaskCompletionSource();
        store.RemoveExpiredFilesAsync(Arg.Any<CancellationToken>()).Returns(1).AndDoes(_ => firstPass.TrySetResult());

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when - no time is advanced at all
        await service.StartAsync(AbortToken);
        await firstPass.Task.WaitAsync(TimeSpan.FromSeconds(5), AbortToken);
        await service.StopAsync(AbortToken);

        // then - the startup pass ran without waiting for the first interval
        await store.Received().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_remove_expired_uploads_each_interval()
    {
        // given - the startup pass satisfies call #1, so the interval loop is only proven by a
        // SECOND call that requires advancing fake time
        var store = Substitute.For<IExpiringTusStore>();
        var secondPass = new TaskCompletionSource();
        var calls = 0;
        store
            .RemoveExpiredFilesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) >= 2)
                {
                    secondPass.TrySetResult();
                }

                return 3;
            });

        var timeProvider = new TimerArmedTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when
        await service.StartAsync(AbortToken);
        await _AdvanceUntilAsync(timeProvider, secondPass.Task);
        await service.StopAsync(AbortToken);

        // then - startup pass + at least one interval-driven pass
        calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_keep_running_after_a_failed_pass()
    {
        // given - the first pass throws, the second succeeds
        var store = Substitute.For<IExpiringTusStore>();
        var secondPass = new TaskCompletionSource();
        var calls = 0;
        store
            .RemoveExpiredFilesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls++;

                if (calls == 1)
                {
                    throw new InvalidOperationException("storage hiccup");
                }

                secondPass.TrySetResult();

                return 0;
            });

        var timeProvider = new TimerArmedTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when
        await service.StartAsync(AbortToken);
        await _AdvanceUntilAsync(timeProvider, secondPass.Task);
        await service.StopAsync(AbortToken);

        // then - the loop survived the failure
        calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task should_keep_a_disk_upload_completed_after_selection()
    {
        var fileIds = new AfterSelectionFileIdProvider();
        var clock = new TimerArmedTimeProvider();
        var gate = new PausedCleanupGate();
        await using var host = await _StartDiskHostAsync(clock, gate, fileIds: fileIds);
        byte[] content = [1, 2, 3];
        var location = await host.CreateUploadAsync(content.Length);
        var fileId = TusEndpointTestHost.FileIdOf(location);
        await host.Store.SetExpirationAsync(fileId, DateTimeOffset.UnixEpoch, AbortToken);
        fileIds.AfterSelection = async id =>
        {
            await using var body = new MemoryStream(content);
            await host.Store.AppendDataAsync(id, body, AbortToken);
        };

        gate.Resume();
        await clock.TimerArmed.WaitAsync(AbortToken);

        var upload = await host.Services.GetRequiredService<ITusUploads>().FindUncheckedAsync(fileId, AbortToken);
        upload.Should().NotBeNull();
        await using var stream = await upload!.OpenContentAsync(AbortToken);
        await using var output = new MemoryStream();
        await stream.CopyToAsync(output, AbortToken);
        output.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task should_keep_a_disk_upload_refreshed_after_selection()
    {
        var fileIds = new AfterSelectionFileIdProvider();
        var clock = new TimerArmedTimeProvider();
        var gate = new PausedCleanupGate();
        await using var host = await _StartDiskHostAsync(clock, gate, fileIds: fileIds);
        var location = await host.CreateUploadAsync(uploadLength: 3);
        var fileId = TusEndpointTestHost.FileIdOf(location);
        await host.Store.SetExpirationAsync(fileId, DateTimeOffset.UnixEpoch, AbortToken);
        fileIds.AfterSelection = id => host.Store.SetExpirationAsync(id, DateTimeOffset.MaxValue, AbortToken);

        gate.Resume();
        await clock.TimerArmed.WaitAsync(AbortToken);

        using var response = await host.HeadAsync(location);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Store.GetExpirationAsync(fileId, AbortToken)).Should().Be(DateTimeOffset.MaxValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(3L)]
    public async Task should_remove_an_expired_incomplete_disk_upload(long? length)
    {
        var clock = new TimerArmedTimeProvider();
        var gate = new PausedCleanupGate();
        await using var host = await _StartDiskHostAsync(clock, gate);
        var location = await host.CreateUploadAsync(length);
        var fileId = TusEndpointTestHost.FileIdOf(location);
        // The sweep timer's fake clock is in 2000. Disk expiration follows tusdotnet's system clock.
        var expiredAt = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await host.Store.SetExpirationAsync(fileId, expiredAt, AbortToken);

        gate.Resume();
        await clock.TimerArmed.WaitAsync(AbortToken);

        (await host.Store.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_share_the_endpoint_lock_and_retry_a_busy_disk_upload(bool registerLockProvider)
    {
        var provider = registerLockProvider ? new NamespacedLockProvider() : InMemoryFileLockProvider.Instance;
        var clock = new TimerArmedTimeProvider();
        var gate = new PausedCleanupGate();
        var log = new CleanupLog();
        await using var host = await _StartDiskHostAsync(clock, gate, log, registerLockProvider ? provider : null);
        var location = await host.CreateUploadAsync(uploadLength: 3);
        var fileId = TusEndpointTestHost.FileIdOf(location);
        var fileLock = await provider.AquireLock(fileId);
        (await fileLock.Lock()).Should().BeTrue();

        try
        {
            using var response = await host.PatchAsync(location, [1, 2, 3], offset: 0);
            response.StatusCode.Should().Be(HttpStatusCode.Locked);
            await host.Store.SetExpirationAsync(fileId, DateTimeOffset.UnixEpoch, AbortToken);

            gate.Resume();
            await clock.TimerArmed.WaitAsync(AbortToken);

            (await host.Store.FileExistAsync(fileId, AbortToken)).Should().BeTrue();
        }
        finally
        {
            await fileLock.ReleaseIfHeld();
        }

        clock.Advance(_Interval);
        await log.Removed.WaitAsync(AbortToken);
        (await host.Store.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
    }

    [Fact]
    public async Task should_skip_a_disk_upload_removed_after_selection()
    {
        var fileIds = new AfterSelectionFileIdProvider();
        var clock = new TimerArmedTimeProvider();
        var gate = new PausedCleanupGate();
        var log = new CleanupLog();
        await using var host = await _StartDiskHostAsync(clock, gate, log, fileIds: fileIds);
        var location = await host.CreateUploadAsync(uploadLength: 3);
        var fileId = TusEndpointTestHost.FileIdOf(location);
        await host.Store.SetExpirationAsync(fileId, DateTimeOffset.UnixEpoch, AbortToken);
        fileIds.AfterSelection = id => host.Store.DeleteFileAsync(id, AbortToken);

        gate.Resume();
        await clock.TimerArmed.WaitAsync(AbortToken);

        (await host.Store.FileExistAsync(fileId, AbortToken)).Should().BeFalse();
        log.Removed.IsCompleted.Should().BeFalse();
        log.Failure.Should().BeNull();
    }

    [Fact]
    public async Task should_release_the_disk_upload_lock_when_the_store_fails()
    {
        var fileIds = new AfterSelectionFileIdProvider();
        var clock = new TimerArmedTimeProvider();
        var gate = new PausedCleanupGate();
        var log = new CleanupLog();
        await using var host = await _StartDiskHostAsync(clock, gate, log, fileIds: fileIds);
        var location = await host.CreateUploadAsync(uploadLength: 3);
        var fileId = TusEndpointTestHost.FileIdOf(location);
        await host.Store.SetExpirationAsync(fileId, DateTimeOffset.UnixEpoch, AbortToken);
        var failure = new IOException("Disk read failed");
        fileIds.AfterSelection = _ => Task.FromException(failure);

        gate.Resume();
        await clock.TimerArmed.WaitAsync(AbortToken);

        log.Failure.Should().BeSameAs(failure);
        await host.Store.SetExpirationAsync(fileId, DateTimeOffset.MaxValue, AbortToken);
        using var response = await host.PatchAsync(location, [1, 2, 3], offset: 0);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static Task<TusEndpointTestHost> _StartDiskHostAsync(
        TimerArmedTimeProvider clock,
        PausedCleanupGate gate,
        CleanupLog? log = null,
        ITusFileLockProvider? lockProvider = null,
        ITusFileIdProvider? fileIds = null
    )
    {
        return TusEndpointTestHost.StartAsync(
            new TusEndpointTestHostSetup
            {
                CreateStore = directory =>
                    fileIds is null
                        ? new TusDiskStore(directory)
                        : new TusDiskStore(directory, false, TusDiskBufferSize.Default, fileIds),
                ConfigureServices = services =>
                {
                    services.AddSingleton<TimeProvider>(clock);
                    services.AddSingleton<ITusExpiredUploadsCleanupGate>(gate);

                    if (log is not null)
                    {
                        services.AddSingleton<ILogger<TusExpiredUploadsCleanupService>>(log);
                    }

                    if (lockProvider is not null)
                    {
                        services.AddSingleton(lockProvider);
                    }
                },
            },
            AbortToken
        );
    }

    private sealed class PausedCleanupGate : ITusExpiredUploadsCleanupGate
    {
        private readonly TaskCompletionSource<bool> _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Resume() => _resume.SetResult(true);

        public Task<bool> TryClaimPassAsync(TimeSpan holdFor, CancellationToken cancellationToken) =>
            _resume.Task.WaitAsync(cancellationToken);
    }

    private sealed class AfterSelectionFileIdProvider : GuidFileIdProvider
    {
        private int _accesses;

        public Func<string, Task>? AfterSelection { get; set; }

        public override async Task<bool> ValidateId(string fileId)
        {
            // The scan validates the only candidate once. The next access is the legacy bulk deletion or
            // the new recheck. Mutate the real store at that boundary without depending on thread timing.
            if (AfterSelection is { } action && Interlocked.Increment(ref _accesses) == 2)
            {
                await action(fileId);
            }

            return await base.ValidateId(fileId);
        }
    }

    private sealed class NamespacedLockProvider : ITusFileLockProvider
    {
        // A distinct key makes a mistaken fallback to the default provider observable.
        public Task<ITusFileLock> AquireLock(string fileId) =>
            InMemoryFileLockProvider.Instance.AquireLock($"test-{fileId}");
    }

    private sealed class CleanupLog : ILogger<TusExpiredUploadsCleanupService>
    {
        private readonly TaskCompletionSource _removed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Removed => _removed.Task;

        public Exception? Failure { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (eventId.Id == 3260)
            {
                _removed.TrySetResult();
            }

            if (logLevel == LogLevel.Error)
            {
                Failure = exception;
            }
        }
    }
}
