// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Testing.Tests;

namespace Tests.Transport;

public sealed class IDispatcherCompatibilityTests : TestBase
{
    [Fact]
    public async Task DisposeAsync_with_timeout_should_delegate_to_legacy_dispose_implementation()
    {
        // given
        var dispatcher = new LegacyDispatcher();

        // when
        await ((IDispatcher)dispatcher).DisposeAsync(TimeSpan.FromSeconds(1), AbortToken);

        // then
        dispatcher.IsDisposed.Should().BeTrue();
    }

    private sealed class LegacyDispatcher : IDispatcher
    {
        public bool IsDisposed { get; private set; }

        public ValueTask StartAsync(CancellationToken stoppingToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask EnqueueToPublish(MediumMessage message, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask EnqueueToExecute(
            MediumMessage message,
            ConsumerExecutorDescriptor? descriptor = null,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;

        public Task EnqueueToScheduler(
            MediumMessage message,
            DateTimeOffset publishTime,
            object? transaction = null,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }
}
