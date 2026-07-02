// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using tusdotnet.Interfaces;

namespace Tests;

public sealed class TusExpiredUploadsCleanupServiceTests : TestBase
{
    private static readonly TimeSpan _Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Advances fake time one interval at a time with small real-time yields until the signal
    /// completes. A single Advance can race the service's async loop before it re-arms
    /// PeriodicTimer.WaitForNextTickAsync (same pattern as the coordination heartbeat tests).
    /// </summary>
    private static async Task _AdvanceUntilAsync(FakeTimeProvider timeProvider, Task signal)
    {
        for (var i = 0; i < 50 && !signal.IsCompleted; i++)
        {
            timeProvider.Advance(_Interval);
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        await signal.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static TusExpiredUploadsCleanupService _CreateService(
        ITusExpirationStore store,
        FakeTimeProvider timeProvider
    )
    {
        return new TusExpiredUploadsCleanupService(
            store,
            Options.Create(new TusExpiredUploadsCleanupOptions { Interval = _Interval }),
            timeProvider,
            NullLogger<TusExpiredUploadsCleanupService>.Instance
        );
    }

    [Fact]
    public async Task should_remove_expired_uploads_each_interval()
    {
        // given - signal completion deterministically instead of waiting on real time
        var store = Substitute.For<ITusExpirationStore>();
        var firstPass = new TaskCompletionSource();
        store.RemoveExpiredFilesAsync(Arg.Any<CancellationToken>()).Returns(3).AndDoes(_ => firstPass.TrySetResult());

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when
        await service.StartAsync(AbortToken);
        await _AdvanceUntilAsync(timeProvider, firstPass.Task);
        await service.StopAsync(AbortToken);

        // then
        await store.Received().RemoveExpiredFilesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_keep_running_after_a_failed_pass()
    {
        // given - the first pass throws, the second succeeds
        var store = Substitute.For<ITusExpirationStore>();
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

        var timeProvider = new FakeTimeProvider();
        using var service = _CreateService(store, timeProvider);

        // when
        await service.StartAsync(AbortToken);
        await _AdvanceUntilAsync(timeProvider, secondPass.Task);
        await service.StopAsync(AbortToken);

        // then - the loop survived the failure
        calls.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void should_register_cleanup_service_via_di()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITusExpirationStore>());
        services.AddLogging();

        // when
        services.AddTusExpiredUploadsCleanup(options => options.Interval = TimeSpan.FromSeconds(30));
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Should()
            .ContainSingle(service => service is TusExpiredUploadsCleanupService);
        provider
            .GetRequiredService<IOptions<TusExpiredUploadsCleanupOptions>>()
            .Value.Interval.Should()
            .Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void should_reject_non_positive_interval()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITusExpirationStore>());
        services.AddLogging();
        services.AddTusExpiredUploadsCleanup(options => options.Interval = TimeSpan.Zero);
        using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.GetRequiredService<IOptions<TusExpiredUploadsCleanupOptions>>().Value;

        // then
        act.Should().Throw<OptionsValidationException>();
    }
}
