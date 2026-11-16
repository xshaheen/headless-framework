// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Guarantees;
using Tests.Simulation;

namespace Tests.RequestReply;

/// <summary>
/// A request whose deadline passes before its responder gets to it, on the real pipeline against the simulated broker
/// and store: the responder never runs, and the request ends with no reply and no failure counted against the
/// responder's circuit, reported once to <c>OnTerminal</c> as an expired deadline.
/// </summary>
[Collection(RequestReplyCollection.Name)]
public sealed class ExpiredRequestTests : TestBase
{
    private static readonly TimeSpan _RequestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    [Guarantee("MSG-RR-05")]
    public async Task should_not_run_the_responder_when_the_request_deadline_passes_before_its_first_attempt()
    {
        // given: the request's admission reaches the store only after its deadline
        var desk = new ExpiringQuoteDesk();
        var exhausted = new TerminalCalls();
        await using var host = await _StartAsync(desk, exhausted, setup => { });
        var admissionDelay = _RequestTimeout * 2;
        host.Faults.DelayStoreCommit(StorageOperation.AdmitReceivedMessage, admissionDelay);

        var mark = host.Clock.WaitMark;

        // when: the caller sends, the request arrives in time, and its deadline passes while the admission waits; the
        // clock has not moved, so the admission's wait is the one due exactly its delay ahead
        var call = _RequestAsync(host, "sku-1");
        await host.WhenWaitArmedAsync(mark, due => due == admissionDelay, AbortToken);
        host.Advance(admissionDelay);
        await call.Awaiting(static pending => pending).Should().ThrowAsync<RequestTimeoutException>();
        await host.WithinBoundAsync(exhausted.First, AbortToken);

        // then
        desk.Attempts.Should().Be(0);
        _AssertEndedAsExpired(host, exhausted);
    }

    [Fact]
    [Guarantee("MSG-RR-05")]
    public async Task should_not_run_the_responder_again_when_its_lease_is_recovered_after_the_request_deadline()
    {
        // given: the first attempt starts in time and runs until the host goes down
        var desk = new ExpiringQuoteDesk
        {
            FirstAttempt = static cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
        };
        var exhausted = new TerminalCalls();
        var host = await _StartAsync(desk, exhausted, static _ => { });
        var call = _RequestAsync(host, "sku-1");
        await host.WithinBoundAsync(desk.FirstAttemptStarted.Task, AbortToken);

        // when: the host stops mid-attempt, and a restarted host recovers the row once its lease is gone, long after
        // the request's deadline
        await host.ShutDownAsync(AbortToken);
        await host.DisposeAsync();
        await call.Awaiting(static pending => pending).Should().ThrowAsync<Exception>();
        var restartedExhausted = new TerminalCalls();
        await using var restarted = await SimulationHost.RestartAsync(
            host,
            setup => _Configure(setup, restartedExhausted, static _ => { }),
            services => _ConfigureServices(services, desk),
            AbortToken
        );
        await restarted.RunRetryPickupAsync(TimeSpan.FromMinutes(5) + PipelineHosts.PollInterval, AbortToken);
        await restarted.WithinBoundAsync(restartedExhausted.First, AbortToken);

        // then: the recovered attempt found the request expired and ended it without running the responder
        desk.Attempts.Should().Be(1);
        _AssertEndedAsExpired(restarted, restartedExhausted);
        exhausted.Calls.Should().BeEmpty("the stopped host wrote no terminal state for the attempt shutdown cut off");
    }

    private static void _AssertEndedAsExpired(SimulationHost host, TerminalCalls exhausted)
    {
        host.Broker.Events.Should().NotContain(e => e.Kind == BrokerEventKind.ReplySent);
        exhausted
            .Calls.Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Be(MessageTerminalReason.DeliveryDeadlineExpired);
        host.GetRequiredService<ICircuitBreakerMonitor>()
            .GetSnapshot(MessageLane.Queue, ExpiringQuoteDesk.Identity)!
            .ConsecutiveFailures.Should()
            .Be(0);
    }

    private static Task<ExpiringQuote> _RequestAsync(SimulationHost host, string sku)
    {
        return host.GetRequiredService<IRequestClient>()
            .RequestAsync<ExpiringQuoteRequest, ExpiringQuote>(
                new ExpiringQuoteRequest(sku),
                new RequestOptions { Timeout = _RequestTimeout },
                AbortToken
            );
    }

    private static Task<SimulationHost> _StartAsync(
        ExpiringQuoteDesk desk,
        TerminalCalls exhausted,
        Action<MessagingSetupBuilder> configure
    )
    {
        return SimulationHost.StartAsync(
            setup => _Configure(setup, exhausted, configure),
            services => _ConfigureServices(services, desk),
            cancellationToken: AbortToken
        );
    }

    private static void _Configure(
        MessagingSetupBuilder setup,
        TerminalCalls exhausted,
        Action<MessagingSetupBuilder> configure
    )
    {
        setup.AddModule<ExpiringQuoteModule>();
        setup.AddRequestReply();
        setup.Options.RetryProcessor.AdaptivePolling = false;
        setup.Options.RetryProcessor.BaseInterval = PipelineHosts.PollInterval;
        setup.Options.RetryPolicy.OnTerminal = (info, _) =>
        {
            exhausted.Record(info);
            return Task.CompletedTask;
        };
        configure(setup);
    }

    private static void _ConfigureServices(IServiceCollection services, ExpiringQuoteDesk desk)
    {
        services.AddSingleton(desk);
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<ExpiringQuoteRequest>(ExpiringQuoteRequest.Name);
            messaging.Message<ExpiringQuote>(ExpiringQuote.Name, ExpiringQuote.Version);
        });
    }
}

public sealed record ExpiringQuoteRequest(string Sku)
{
    public const string Name = "tests.pipeline.quote-request";
}

public sealed record ExpiringQuote(string Sku)
{
    public const string Name = "tests.pipeline.quote";
    public const string Version = "v1";
}

/// <summary>Counts its attempts; it is resolved from the container, so the test reads the same instance.</summary>
public sealed class ExpiringQuoteDesk : IRespond<ExpiringQuoteRequest, ExpiringQuote>
{
    public const string Identity = "tests.pipeline.quote-desk";

    private int _attempts;

    /// <summary>What the first attempt does before it answers; it answers at once when the test sets nothing.</summary>
    public Func<CancellationToken, Task>? FirstAttempt { get; init; }

    public TaskCompletionSource FirstAttemptStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Attempts => Volatile.Read(ref _attempts);

    public async ValueTask<ExpiringQuote> RespondAsync(
        ConsumeContext<ExpiringQuoteRequest> context,
        CancellationToken cancellationToken
    )
    {
        if (Interlocked.Increment(ref _attempts) == 1)
        {
            FirstAttemptStarted.TrySetResult();
            if (FirstAttempt is { } first)
            {
                await first(cancellationToken).ConfigureAwait(false);
            }
        }

        return new ExpiringQuote(context.Message.Sku);
    }
}

/// <summary>
/// Every <c>OnTerminal</c> call of one host, and a signal for the first. The pipeline writes the terminal state before it
/// runs <c>OnTerminal</c>, so a test that waits for that store write still races the callback; it waits on this instead.
/// </summary>
internal sealed class TerminalCalls
{
    private readonly ConcurrentQueue<TerminalMessageInfo> _calls = new();
    private readonly TaskCompletionSource _first = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyCollection<TerminalMessageInfo> Calls => _calls;

    /// <summary>Completes once <c>OnTerminal</c> has been called.</summary>
    public Task First => _first.Task;

    public void Record(TerminalMessageInfo info)
    {
        _calls.Enqueue(info);
        _first.TrySetResult();
    }
}

/// <summary>What the source generator emits for a responder, written out so the desk is the container's instance.</summary>
internal sealed class ExpiringQuoteModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddQueueResponder<ExpiringQuoteDesk, ExpiringQuoteRequest, ExpiringQuote>(
            ExpiringQuoteDesk.Identity,
            static async (services, context, cancellationToken) =>
            {
                var typed = (ConsumeContext<ExpiringQuoteRequest>)context;
                var reply = await services
                    .GetRequiredService<ExpiringQuoteDesk>()
                    .RespondAsync(typed, cancellationToken)
                    .ConfigureAwait(false);
                typed.RecordReply(reply);
            }
        );
    }
}
