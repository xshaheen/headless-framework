// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Headless.Messaging.Processor;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Tests.Helpers;

namespace Tests.Processor;

/// <summary>
/// The outbox initialization processor retries every additional outbox whose database was down at startup until
/// it initializes, one outbox apart from another, and then idles until shutdown.
/// </summary>
public sealed class OutboxInitializationProcessorTests : TestBase
{
    private static readonly TimeSpan _Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeTimeProvider _timeProvider = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly OutboxInitializationProcessor _sut = new(NullLogger<OutboxInitializationProcessor>.Instance);

    protected override async ValueTask DisposeAsyncCore()
    {
        _stopping.Dispose();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_retry_each_outbox_until_it_initializes_without_waiting_on_another()
    {
        // given — billing comes back on the second attempt; shipping stays down
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing", initialized: false);
        billing
            .Initializer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("billing is down")), Task.CompletedTask);
        var shipping = AdditionalOutboxDoubles.CreateOutbox("shipping", initialized: false);
        shipping
            .Initializer.InitializeAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new TimeoutException("shipping is down")));
        await using var context = _CreateContext(billing, shipping);

        // when
        var run = _sut.ProcessAsync(context);
        await _AdvanceUntilAsync(() => billing.IsInitialized);

        // then
        shipping.IsInitialized.Should().BeFalse();
        await billing.Initializer.Received(2).InitializeAsync(Arg.Any<CancellationToken>());
        await shipping.Initializer.Received().InitializeAsync(Arg.Any<CancellationToken>());
        await _StopAsync(run);
    }

    [Fact]
    public async Task should_idle_until_shutdown_when_every_outbox_is_initialized()
    {
        // given
        var billing = AdditionalOutboxDoubles.CreateOutbox("billing");
        await using var context = _CreateContext(billing);

        // when
        var run = _sut.ProcessAsync(context);
        _timeProvider.Advance(OutboxInitializationProcessor.MaxRetryDelay * 4);

        // then — only the attempt that initialized it
        await billing.Initializer.Received(1).InitializeAsync(Arg.Any<CancellationToken>());
        run.IsCompleted.Should().BeFalse();
        await _StopAsync(run);
    }

    private ProcessingContext _CreateContext(params MessagingOutbox[] secondaries)
    {
        var primary = AdditionalOutboxDoubles.CreateRelationalStorage("orders");
        var provider = new ServiceCollection()
            .AddSingleton(AdditionalOutboxDoubles.CreateOutboxes(primary, secondaries))
            .BuildServiceProvider();

        return new ProcessingContext(provider, _timeProvider, _stopping.Token);
    }

    // The processor's awaits resume on the thread pool, so time is advanced in steps until it observes the change.
    private async Task _AdvanceUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        timeout.CancelAfter(_Timeout);
        while (!condition())
        {
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private async Task _StopAsync(Task run)
    {
        await _stopping.CancelAsync();
        await run.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
    }
}
