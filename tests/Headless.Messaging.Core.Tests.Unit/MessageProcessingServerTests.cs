// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Processor;
using Headless.Messaging.Runtime;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class MessageProcessingServerTests : TestBase
{
    [Fact]
    public async Task concurrent_quiesce_cannot_be_undone_by_start_publication()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });

        await using var provider = services.BuildServiceProvider();
        var server = provider.GetServices<IProcessingServer>().OfType<MessageProcessingServer>().Single();
        var retryProcessor = provider.GetRequiredService<MessageNeedToRetryProcessor>();
        using var releaseStart = new ManualResetEventSlim(initialState: false);
        var startPublishing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.StartPublicationHookForTest = () =>
        {
            startPublishing.TrySetResult();
            releaseStart.Wait();
        };

        var startTask = Task
            .Factory.StartNew(
                () => server.StartAsync(AbortToken).AsTask(),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
                TaskScheduler.Default
            )
            .Unwrap();
        await startPublishing.Task.WaitAsync(AbortToken);
        var quiesceTask = Task.Factory.StartNew(
            () => ((IProcessingServerShutdown)server).Quiesce(),
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );

        releaseStart.Set();
        await Task.WhenAll(startTask, quiesceTask);
        await using var context = new ProcessingContext(
            new RejectingServiceProvider(),
            TimeProvider.System,
            CancellationToken.None
        );

        await retryProcessor.ProcessAsync(context).WaitAsync(AbortToken);
        retryProcessor.StartupJitterApplied.Should().BeFalse("quiesce must remain authoritative after startup");
        await ((IProcessingServerShutdown)server).StopAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task shutdown_quiesces_retry_pickup_before_bounded_inflight_drain()
    {
        var fakeTime = new FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
        {
            setup.Options.ShutdownTimeout = TimeSpan.FromSeconds(2);
            setup.UseInMemory();
            setup.UseInMemoryStorage();
        });
        services.AddSingleton<TimeProvider>(fakeTime);

        await using var provider = services.BuildServiceProvider();
        var server = provider.GetServices<IProcessingServer>().OfType<MessageProcessingServer>().Single();
        var retryProcessor = provider.GetRequiredService<MessageNeedToRetryProcessor>();
        var releaseInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        retryProcessor.SetQuadrantActiveTaskForTest(MessageType.Subscribe, MessageLane.Bus, releaseInFlight.Task);

        var shutdownTask = server.DisposeAsync().AsTask();
        try
        {
            shutdownTask.IsCompleted.Should().BeFalse("the captured in-flight retry quadrant is still running");
            await using (
                var context = new ProcessingContext(new RejectingServiceProvider(), fakeTime, CancellationToken.None)
            )
            {
                await retryProcessor.ProcessAsync(context).WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
            }

            retryProcessor
                .StartupJitterApplied.Should()
                .BeFalse("quiesced retry pickup returns before polling storage");

            await Task.Yield();
            fakeTime.Advance(TimeSpan.FromSeconds(2));

            await shutdownTask.WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
            releaseInFlight.Task.IsCompleted.Should().BeFalse("the configured boundary does not cancel a live attempt");
        }
        finally
        {
            releaseInFlight.TrySetResult();
        }

        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2), AbortToken);
    }

    private sealed class RejectingServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            throw new InvalidOperationException($"Retry pickup unexpectedly resolved {serviceType} after quiesce.");
        }
    }
}
