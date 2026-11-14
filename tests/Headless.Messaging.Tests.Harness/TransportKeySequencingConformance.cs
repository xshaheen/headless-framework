// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Tests.RequestReply;

namespace Tests;

/// <summary>
/// A sequential consumer against a provider's real transport: one host publishes keyed messages and consumes them
/// through the real pipeline, with in-memory storage, so only the transport varies between providers.
/// </summary>
[PublicAPI]
public static class TransportKeySequencingConformance
{
    private const byte _Concurrency = 4;
    private const int _Keys = 5;
    private const int _MessagesPerKey = 4;
    private const int _Total = _Keys * _MessagesPerKey;

    /// <summary>
    /// Proves that the messages of one routing affinity key never run at once, that as many keys as the consumer's
    /// concurrency run side by side and no more, and, on a transport that delivers in publish order, that each key's
    /// messages run in that order.
    /// </summary>
    public static async Task AssertAsync(TransportProviderConformanceDriver driver, CancellationToken cancellationToken)
    {
        var probe = new KeySequencingProbe();
        var destination = $"conformance.seq.{Guid.NewGuid():N}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(probe);
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<KeySequencedOrder>(destination);
            messaging.AddModule<KeySequencingConformanceModule>();
        });
        services.AddHeadlessMessaging(setup =>
        {
            // The same transport a consuming host uses; every driver configures it, whatever its request/reply support.
            driver.ConfigureRequestReplyTransport(setup);
            setup.UseInMemoryStorage();
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.ProcessLocal;
            setup.Tune(
                KeySequencingConformanceModule.Identity,
                consumer => consumer.Concurrency(_Concurrency).Sequential()
            );
        });
        driver.ConfigureRequestReplyServices(services);

        await using var provider = services.BuildServiceProvider();
        await provider.SetupResourcesAndBootstrapAsync(cancellationToken);

        // given: every key's messages published one after another, the keys interleaved
        var keys = Enumerable.Range(0, _Keys).Select(static index => $"order-{index}").ToArray();
        var queue = provider.GetRequiredService<IQueue>();
        for (var sequence = 0; sequence < _MessagesPerKey; sequence++)
        {
            foreach (var key in keys)
            {
                // Sent directly, so the broker stores the messages in the order of these calls.
                await queue.EnqueueAsync(
                    new KeySequencedOrder(key, sequence),
                    new QueueOptions { RoutingAffinityKey = key, DeliveryMode = DeliveryMode.Direct },
                    cancellationToken
                );
            }
        }

        // when: the handlers that started are held until every message is stored and handed to the sequencer
        var held = new List<KeySequencedStart>();
        for (var slot = 0; slot < _Concurrency; slot++)
        {
            held.Add(await _NextStartAsync(probe, cancellationToken));
        }

        var monitoring = provider.GetRequiredService<IDataStorage>().GetMonitoringApi();
        await RequestReplyConformanceWait.UntilAsync(
            async token =>
            {
                var page = await monitoring.GetMessagesAsync(
                    new MessageQuery
                    {
                        MessageType = MessageType.Subscribe,
                        Name = destination,
                        Lane = MessageLane.Queue,
                        PageSize = _Total * 2,
                    },
                    token
                );

                return page.Items.Count == _Total;
            },
            $"all {_Total} keyed messages are stored while {_Concurrency} handlers are held",
            cancellationToken
        );

        // then: one handler per key, as many keys as the concurrency, and nothing else started
        held.Should().OnlyHaveUniqueItems(static start => start.Order.Key, "a key runs one message at a time");
        probe.Started.Should().Be(_Concurrency, "no more keys run at once than the consumer's concurrency");

        // and: every later message runs only after the one before it on its key finished
        var runOrder = keys.ToDictionary(static key => key, static _ => new List<int>(), StringComparer.Ordinal);
        foreach (var start in held)
        {
            runOrder[start.Order.Key].Add(start.Order.Sequence);
            start.Release.TrySetResult();
        }

        for (var remaining = _Total - held.Count; remaining > 0; remaining--)
        {
            var start = await _NextStartAsync(probe, cancellationToken);
            runOrder[start.Order.Key].Add(start.Order.Sequence);
            start.Release.TrySetResult();
        }

        await RequestReplyConformanceWait.UntilAsync(
            () => probe.Finished == _Total,
            "every keyed message finished",
            cancellationToken
        );

        probe.Overlaps.Should().Be(0, "two messages of one key never run at once");
        probe.MostAtOnce.Should().Be(_Concurrency);
        foreach (var key in keys)
        {
            if (driver.DeliversInPublishOrder)
            {
                runOrder[key].Should().Equal(Enumerable.Range(0, _MessagesPerKey), $"{key} runs in publish order");
            }
            else
            {
                runOrder[key].Should().BeEquivalentTo(Enumerable.Range(0, _MessagesPerKey));
            }
        }
    }

    private static async Task<KeySequencedStart> _NextStartAsync(
        KeySequencingProbe probe,
        CancellationToken cancellationToken
    )
    {
        var next = probe.Starts.ReadAsync(cancellationToken).AsTask();
        await RequestReplyConformanceWait.ForAsync(next, "the next keyed message started", cancellationToken);

        return await next;
    }
}

internal sealed record KeySequencedOrder(string Key, int Sequence);

/// <summary>One handler run, held until the scenario releases it.</summary>
internal sealed record KeySequencedStart(KeySequencedOrder Order, TaskCompletionSource Release);

/// <summary>What the scenario observes of the handler: each start, how many ran at once, and any overlap on a key.</summary>
internal sealed class KeySequencingProbe
{
    private readonly Channel<KeySequencedStart> _starts = Channel.CreateUnbounded<KeySequencedStart>();
    private readonly ConcurrentDictionary<string, int> _activeByKey = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();
    private int _active;

    public ChannelReader<KeySequencedStart> Starts => _starts.Reader;

    public int Started { get; private set; }

    public int Finished { get; private set; }

    public int Overlaps { get; private set; }

    public int MostAtOnce { get; private set; }

    public TaskCompletionSource Enter(KeySequencedOrder order)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            Started++;
            _active++;
            MostAtOnce = Math.Max(MostAtOnce, _active);
            if (_activeByKey.AddOrUpdate(order.Key, 1, static (_, count) => count + 1) > 1)
            {
                Overlaps++;
            }
        }

        _starts.Writer.TryWrite(new KeySequencedStart(order, release));

        return release;
    }

    public void Exit(KeySequencedOrder order)
    {
        lock (_sync)
        {
            _active--;
            _activeByKey.AddOrUpdate(order.Key, 0, static (_, count) => count - 1);
            Finished++;
        }
    }
}

internal sealed class KeySequencedOrderConsumer(KeySequencingProbe probe) : IConsume<KeySequencedOrder>
{
    public async ValueTask ConsumeAsync(ConsumeContext<KeySequencedOrder> context, CancellationToken cancellationToken)
    {
        var release = probe.Enter(context.Message);
        try
        {
            // Host shutdown cancels the token, so a scenario that fails before releasing never hangs disposal.
            await release.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            probe.Exit(context.Message);
        }
    }
}

/// <summary>
/// What the source generator emits for the consumer above, written out because the harness assembly does not run the
/// generator.
/// </summary>
internal sealed class KeySequencingConformanceModule : IMessagingModule
{
    public const string Identity = "conformance.key-sequencing.projection";

    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddQueueConsumer<KeySequencedOrderConsumer, KeySequencedOrder>(
            Identity,
            static (services, context, cancellationToken) =>
                ActivatorUtilities
                    .CreateInstance<KeySequencedOrderConsumer>(services)
                    .ConsumeAsync((ConsumeContext<KeySequencedOrder>)context, cancellationToken)
        );
    }
}
