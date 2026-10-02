// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.RequestReply;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests.RequestReply;

/// <summary>Records every reply a responder host sends, so a test reads them without waiting on a listener.</summary>
internal sealed class RecordingReplyTransport : IReplyTransport
{
    public ConcurrentQueue<(string Address, TransportMessage Reply)> Sent { get; } = new();

    public Exception? FailWith { get; set; }

    /// <summary>Makes every send wait until its token is canceled, like a broker in a brownout.</summary>
    public bool StallSends { get; set; }

    /// <summary>Completes when a stalled send has started.</summary>
    public TaskCompletionSource SendStalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ValueTask<IReplyListener> OpenListenerAsync(
        Func<TransportMessage, CancellationToken, ValueTask> onReply,
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException("A responder host only sends replies.");
    }

    public async ValueTask SendAsync(
        string address,
        TransportMessage reply,
        CancellationToken cancellationToken = default
    )
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        if (StallSends)
        {
            SendStalled.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        Sent.Enqueue((address, reply));
    }
}

public sealed class QuoteResponder : IRespond<PriceQuoteRequest, PriceQuote>
{
    public const string Identity = "tests.quote-responder";

    public ValueTask<PriceQuote> RespondAsync(
        ConsumeContext<PriceQuoteRequest> context,
        CancellationToken cancellationToken
    )
    {
        return ValueTask.FromResult(new PriceQuote(42));
    }
}

/// <summary>
/// A responder host reduced to its executor: real messaging services and a fake clock, with storage and the consumer
/// invocation substituted so a test controls each attempt's outcome and the state write's result.
/// </summary>
internal sealed class ResponderExecutorHost : IAsyncDisposable
{
    public const string ReplyAddress = "headless.reply.test-caller";
    public const string MessageName = "tests.quote-request";

    private ResponderExecutorHost(
        ServiceProvider provider,
        FakeTimeProvider clock,
        RecordingReplyTransport replies,
        IDataStorage storage,
        ISubscribeInvoker invoker,
        SubscribeExecutor executor,
        ConcurrentQueue<FailedInfo> exhausted
    )
    {
        Exhausted = exhausted;
        Provider = provider;
        Clock = clock;
        Replies = replies;
        Storage = storage;
        Invoker = invoker;
        Executor = executor;
    }

    public ServiceProvider Provider { get; }

    public FakeTimeProvider Clock { get; }

    public RecordingReplyTransport Replies { get; }

    public IDataStorage Storage { get; }

    public ISubscribeInvoker Invoker { get; }

    public SubscribeExecutor Executor { get; }

    /// <summary>Every call the host's exhausted-retry callback received.</summary>
    public ConcurrentQueue<FailedInfo> Exhausted { get; }

    public int ExhaustedCalls => Exhausted.Count;

    /// <summary>Builds a responder host whose consumer invocation and storage writes the test controls.</summary>
    /// <param name="configure">Configures the host's messaging options.</param>
    /// <param name="configureServices">Adds services after messaging.</param>
    /// <param name="stateWriteTakesEffect">Whether storage reports each state write as applied.</param>
    /// <param name="realInvoker">
    /// Runs the descriptor's dispatch through the real consume pipeline instead of a substituted invoker.
    /// </param>
    /// <param name="services">A collection to build on, such as a host builder's with tenancy registered.</param>
    public static ResponderExecutorHost Create(
        Action<MessagingOptions>? configure = null,
        Action<IServiceCollection>? configureServices = null,
        bool stateWriteTakesEffect = true,
        bool realInvoker = false,
        IServiceCollection? services = null
    )
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var replies = new RecordingReplyTransport();
        var exhausted = new ConcurrentQueue<FailedInfo>();

        services ??= new ServiceCollection();
        services.AddLogging();
        services.ConfigureMessaging(messaging => messaging.Message<PriceQuoteRequest>(MessageName));
        services.AddHeadlessMessaging(setup =>
        {
            setup.UseInMemory();
            setup.UseProcessLocalInMemoryStorage();
            configure?.Invoke(setup.Options);
        });
        configureServices?.Invoke(services);

        // Registered last, so they replace the provider defaults.
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IReplyTransport>(replies);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        var userOnExhausted = options.RetryPolicy.OnExhausted;
        options.RetryPolicy.OnExhausted = async (info, cancellationToken) =>
        {
            exhausted.Enqueue(info);
            if (userOnExhausted is not null)
            {
                await userOnExhausted(info, cancellationToken);
            }
        };

        var storage = Substitute.For<IDataStorage>();
        storage
            .LeaseReceiveAsync(Arg.Any<MediumMessage>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .LeaseReceiveAndReserveAttemptAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(true));
        storage
            .ReserveReceiveAttemptAsync(Arg.Any<MediumMessage>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));
        storage
            .ChangeReceiveRetryStateAsync(
                Arg.Any<MediumMessage>(),
                Arg.Any<StatusName>(),
                Arg.Any<MessageContentWrite>(),
                Arg.Any<RetryDelay?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ValueTask.FromResult(stateWriteTakesEffect));

        ISubscribeInvoker invoker;
        if (realInvoker)
        {
            invoker = provider.GetRequiredService<ISubscribeInvoker>();
        }
        else
        {
            invoker = Substitute.For<ISubscribeInvoker>();
            invoker
                .InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(Completed()));
            invoker
                .InvokeInScopeAsync(
                    Arg.Any<ConsumerContext>(),
                    Arg.Any<IServiceProvider>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(_ => Task.FromResult(Completed()));
        }

        var executor = new SubscribeExecutor(
            provider,
            storage,
            invoker,
            clock,
            provider.GetRequiredService<ILogger<SubscribeExecutor>>(),
            Options.Create(options)
        );

        return new ResponderExecutorHost(provider, clock, replies, storage, invoker, executor, exhausted);
    }

    /// <summary>A request the way the caller's publish path stamps it, with a deadline on the caller's clock.</summary>
    public MediumMessage Request(
        TimeSpan timeUntilDeadline,
        string? tenantId = null,
        string? correlationId = null,
        bool asRequest = true
    )
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = MessageName,
            [Headers.ConsumerIdentity] = QuoteResponder.Identity,
            [Headers.ContractVersion] = "1",
        };

        if (asRequest)
        {
            headers[Headers.RequestId] = Guid.NewGuid().ToString("D");
            headers[Headers.ReplyTo] = ReplyAddress;
            headers[Headers.RequestDeadline] = Clock
                .GetUtcNow()
                .Add(timeUntilDeadline)
                .ToString("O", CultureInfo.InvariantCulture);
        }

        if (tenantId is not null)
        {
            headers[Headers.TenantId] = tenantId;
        }

        if (correlationId is not null)
        {
            headers[Headers.CorrelationId] = correlationId;
        }

        return new MediumMessage
        {
            StorageId = Guid.NewGuid(),
            Origin = new Message(headers, new PriceQuoteRequest("sku-1")),
            Content = "{}",
            Lane = MessageLane.Queue,
            Added = Clock.GetUtcNow(),
        };
    }

    /// <summary>
    /// Runs one dispatch with a bounded wait: an inline retry sleeps on the fake clock, so a dispatch that wrongly
    /// schedules one fails the test instead of hanging it.
    /// </summary>
    public Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        CancellationToken cancellationToken,
        ConsumerExecutorDescriptor? descriptor = null
    )
    {
        return Executor
            .ExecuteAsync(message, Provider, descriptor ?? ResponderDescriptor(), cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
    }

    /// <summary>The result of an attempt whose consumer returned without recording a reply.</summary>
    public static ConsumerExecutedResult Completed()
    {
        return new ConsumerExecutedResult(null, null, "message-id", null, null);
    }

    /// <summary>The result of an attempt whose responder returned <paramref name="reply"/>.</summary>
    public static ConsumerExecutedResult Replied(PriceQuote? reply)
    {
        return new ConsumerExecutedResult(null, null, "message-id", null, null)
        {
            Reply = reply,
            ReplyType = typeof(PriceQuote),
        };
    }

    public static ConsumerExecutorDescriptor ResponderDescriptor(MessageConsumerDispatch? dispatch = null)
    {
        return new ConsumerExecutorDescriptor
        {
            Dispatch = dispatch,
            MethodName = "RespondAsync",
            Lane = MessageLane.Queue,
            ConsumerType = typeof(QuoteResponder),
            MessageType = typeof(PriceQuoteRequest),
            MessageName = MessageName,
            SubscriptionName = "tests",
            ConsumerIdentity = QuoteResponder.Identity,
            MessageContractVersion = "1",
            ResponseType = typeof(PriceQuote),
        };
    }

    /// <summary>Makes every consumer invocation, on either tier, end with the result <paramref name="attempt"/> gives.</summary>
    public void OnInvoke(Func<Task<ConsumerExecutedResult>> attempt)
    {
        Invoker.InvokeAsync(Arg.Any<ConsumerContext>(), Arg.Any<CancellationToken>()).Returns(_ => attempt());
        Invoker
            .InvokeInScopeAsync(Arg.Any<ConsumerContext>(), Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>())
            .Returns(_ => attempt());
    }

    /// <summary>Runs <paramref name="callback"/> whenever the executor writes <paramref name="status"/>.</summary>
    public void OnStateWrite(StatusName status, Action callback)
    {
        Storage
            .When(storage =>
                storage.ChangeReceiveRetryStateAsync(
                    Arg.Any<MediumMessage>(),
                    status,
                    Arg.Any<MessageContentWrite>(),
                    Arg.Any<RetryDelay?>(),
                    Arg.Any<DateTimeOffset?>(),
                    Arg.Any<int>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => callback());
    }

    /// <summary>The fault codes of every fault reply sent, in order.</summary>
    public IReadOnlyList<string?> FaultCodes()
    {
        return Replies
            .Sent.Where(static sent =>
                string.Equals(
                    sent.Reply.Headers[Headers.ReplyStatus],
                    ReplyProtocol.StatusFault,
                    StringComparison.Ordinal
                )
            )
            .Select(static sent => ReplyProtocol.ReadFault(sent.Reply.Body)?.Code)
            .ToList();
    }

    /// <summary>Every terminal or retry state write the executor made, in order.</summary>
    public IReadOnlyList<(StatusName Status, RetryDelay? NextRetry)> StateWrites()
    {
        return Storage
            .ReceivedCalls()
            .Where(call =>
                string.Equals(
                    call.GetMethodInfo().Name,
                    nameof(IDataStorage.ChangeReceiveRetryStateAsync),
                    StringComparison.Ordinal
                )
            )
            .Select(call => ((StatusName)call.GetArguments()[1]!, (RetryDelay?)call.GetArguments()[3]))
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        await Provider.DisposeAsync();
    }
}
