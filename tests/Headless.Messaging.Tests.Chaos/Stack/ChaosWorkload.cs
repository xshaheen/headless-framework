// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Tests.Helpers;
using Tests.Ledger;

namespace Tests.Stack;

/// <summary>The unit of work every load scenario publishes, keyed by the business key the ledger counts.</summary>
public sealed record ChaosWork(string Key);

/// <summary>A request whose handler answers with one <see cref="ChaosReply"/>.</summary>
public sealed record ChaosRequest(string Key);

/// <summary>The answer to a <see cref="ChaosRequest"/>.</summary>
public sealed record ChaosReply(string Key);

/// <summary>
/// How the handlers of one host behave: how long each run takes, whether the first attempt of a message fails, and
/// whether runs are held so the host keeps them in flight.
/// </summary>
public sealed class HostBehavior(string name, int seed)
{
    private readonly Lock _lock = new();
    private readonly Random _random = new(seed);
    private TaskCompletionSource? _hold;

    public string Name { get; } = name;

    public TimeSpan MinWork { get; init; }

    public TimeSpan MaxWork { get; init; }

    /// <summary>Fails the first attempt of every message with a transient error, so each one lands in the retry backlog.</summary>
    public bool FailFirstAttempt { get; init; }

    /// <summary>Holds every run on this host until <see cref="Release"/> or its delivery is cancelled.</summary>
    public void Hold()
    {
        lock (_lock)
        {
            _hold ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            _hold?.TrySetResult();
            _hold = null;
        }
    }

    public Task WaitWhileHeldAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return _hold?.Task.WaitAsync(cancellationToken) ?? Task.CompletedTask;
        }
    }

    public TimeSpan NextWork()
    {
        if (MaxWork <= MinWork)
        {
            return MinWork;
        }

        lock (_lock)
        {
            return MinWork + TimeSpan.FromTicks((long)(_random.NextDouble() * (MaxWork - MinWork).Ticks));
        }
    }
}

/// <summary>Records each run of a <see cref="ChaosWork"/> message, taking the time its host's behavior asks for.</summary>
public sealed class ChaosWorkConsumer(MessageLedger ledger, HostBehavior host) : IConsume<ChaosWork>
{
    public async ValueTask ConsumeAsync(ConsumeContext<ChaosWork> context, CancellationToken cancellationToken)
    {
        var key = context.Message.Key;
        var attempt = ledger.RecordStarted(key, host.Name);

        if (host.FailFirstAttempt && attempt == 1)
        {
            // Transient by the retry classification, so the message is retried from storage rather than failed for good.
            throw new TimeoutException($"First attempt of {key} fails on purpose.");
        }

        await host.WaitWhileHeldAsync(cancellationToken).ConfigureAwait(false);

        var work = host.NextWork();
        if (work > TimeSpan.Zero)
        {
            await Task.Delay(work, cancellationToken).ConfigureAwait(false);
        }

        // The side effect: recorded only once the run is past every point where shutdown or a lost lease cancels it.
        ledger.RecordCompleted(key, host.Name);
    }
}

/// <summary>Answers a <see cref="ChaosRequest"/> with one <see cref="ChaosReply"/> per handler run.</summary>
public sealed class ChaosRequestConsumer(MessageLedger ledger, HostBehavior host, IQueue queue) : IConsume<ChaosRequest>
{
    public async ValueTask ConsumeAsync(ConsumeContext<ChaosRequest> context, CancellationToken cancellationToken)
    {
        var key = context.Message.Key;
        ledger.RecordStarted(key, host.Name);

        await queue
            .EnqueueAsync(
                new ChaosReply(key),
                new QueueOptions
                {
                    MessageName = ChaosModule.ReplyName,
                    MessageId = key + ".reply",
                    DeliveryMode = DeliveryMode.Durable,
                },
                cancellationToken
            )
            .ConfigureAwait(false);

        ledger.RecordCompleted(key, host.Name);
    }
}

/// <summary>Counts the replies that reach the caller's side.</summary>
public sealed class ChaosReplyConsumer(MessageLedger ledger) : IConsume<ChaosReply>
{
    public ValueTask ConsumeAsync(ConsumeContext<ChaosReply> context, CancellationToken cancellationToken)
    {
        ledger.RecordReply(context.Message.Key);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// The chaos consumers, all on the Queue lane: its queues are named after the message, and every scenario prefixes its
/// message names, so no scenario ever reads what an earlier one left in the broker.
/// </summary>
public sealed class ChaosModule : IMessagingModule
{
    public const string WorkName = "chaos-work";
    public const string RequestName = "chaos-request";
    public const string ReplyName = "chaos-reply";

    public const string WorkConsumer = "chaos.work";
    public const string RequestConsumer = "chaos.request";
    public const string ReplyConsumer = "chaos.reply";

    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddQueueConsumer<ChaosWorkConsumer, ChaosWork>(
            WorkConsumer,
            TestConsumerDispatch.FromServices<ChaosWorkConsumer, ChaosWork>()
        );
        catalog.AddQueueConsumer<ChaosRequestConsumer, ChaosRequest>(
            RequestConsumer,
            TestConsumerDispatch.FromServices<ChaosRequestConsumer, ChaosRequest>()
        );
        catalog.AddQueueConsumer<ChaosReplyConsumer, ChaosReply>(
            ReplyConsumer,
            TestConsumerDispatch.FromServices<ChaosReplyConsumer, ChaosReply>()
        );
    }
}
