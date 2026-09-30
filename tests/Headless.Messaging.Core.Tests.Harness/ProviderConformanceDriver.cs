// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;
using Headless.Messaging.Configuration;
using Tests.Capabilities;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>Logical endpoint requested from a provider-specific conformance driver.</summary>
/// <param name="Lane">The lane the endpoint consumes.</param>
/// <param name="LogicalName">The message name.</param>
/// <param name="SubscriptionName">
/// What the consumer subscribes as: its consumer identity on the Bus lane, or the message name on the Queue lane.
/// </param>
/// <param name="Replica">Which process the session stands for.</param>
[PublicAPI]
public sealed record TransportConformanceEndpoint(
    MessageLane Lane,
    string LogicalName,
    string SubscriptionName,
    string Replica
);

/// <summary>Optional broker operations exposed by a provider-specific conformance driver.</summary>
[PublicAPI]
public sealed record TransportConformanceDriverCapabilities(
    bool SupportsRawEnvelopeInjection,
    bool SupportsTerminalStateObservation,
    bool SupportsTopologyInspection,
    bool SupportsStartupSideEffectObservation
);

/// <summary>Observed provider-native terminal handling for one malformed transport delivery.</summary>
[PublicAPI]
public sealed record TransportMalformedEnvelopeObservation(
    string TerminalState,
    int DeliveryCount,
    int UserHandlerInvocations,
    int DeliveriesAfterRestart
);

/// <summary>Observed host startup state for a rejected provider configuration.</summary>
[PublicAPI]
public sealed record TransportStartupObservation(
    bool ReadinessAdvertised,
    int ProvisioningSideEffects,
    int PersistenceSideEffects,
    int UserHandlerInvocations,
    Exception Failure
);

/// <summary>Provider-local topology projection used only by conformance assertions.</summary>
[PublicAPI]
public sealed record TransportTopologyObservation(
    IReadOnlyCollection<string> BusAddresses,
    IReadOnlyCollection<string> QueueAddresses
);

/// <summary>
/// Test-only broker adapter. Provider leaves implement broker mechanics while the shared harness owns semantics.
/// </summary>
[PublicAPI]
public abstract class TransportProviderConformanceDriver
{
    public abstract string ProviderName { get; }

    public virtual bool SupportsRoutingAffinity => false;

    public virtual void ConfigureRoutingAffinityTransport(MessagingSetupBuilder setup) =>
        throw new NotSupportedException($"{ProviderName} does not support affinity.");

    public virtual Task AssertNativePublisherPathsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual ValueTask<TransportConsumerConformanceSession> CreateRoutingAffinitySessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    ) => CreateSessionAsync(endpoint, cancellationToken);

    public virtual void AssertNativeRoutingAffinity(TransportConformanceDelivery delivery, string expectedKey) { }

    public virtual string? GetNativeRoutingPlacement(TransportConformanceDelivery delivery) => null;

    public virtual TransportConformanceDriverCapabilities Capabilities { get; } = new(false, false, false, false);

    public abstract TransportMalformedEnvelopeBound MalformedEnvelopeBound { get; }

    public abstract ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    );

    public virtual ValueTask InjectRawEnvelopeAsync(
        TransportConformanceEndpoint endpoint,
        IReadOnlyDictionary<string, string?> headers,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    ) => ValueTask.FromException(_Unsupported(nameof(InjectRawEnvelopeAsync)));

    public virtual ValueTask<TransportMalformedEnvelopeObservation> ObserveMalformedEnvelopeAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromException<TransportMalformedEnvelopeObservation>(
            _Unsupported(nameof(ObserveMalformedEnvelopeAsync))
        );

    public virtual ValueTask<TransportTopologyObservation> ObserveTopologyAsync(CancellationToken cancellationToken) =>
        ValueTask.FromException<TransportTopologyObservation>(_Unsupported(nameof(ObserveTopologyAsync)));

    public virtual ValueTask<TransportStartupObservation> ObserveRejectedStartupAsync(
        CancellationToken cancellationToken
    ) => ValueTask.FromException<TransportStartupObservation>(_Unsupported(nameof(ObserveRejectedStartupAsync)));

    private NotSupportedException _Unsupported(string operation)
    {
        return new NotSupportedException($"{ProviderName} does not expose {operation} to the conformance harness.");
    }
}

/// <summary>Shared semantic assertions executed by provider integration leaves.</summary>
[PublicAPI]
public static class TransportProviderConformance
{
    /// <summary>
    /// Two replicas registering one Bus consumer identity compete for one copy, and every distinct identity gets its
    /// own copy. The second identity is longer than any broker's subscription name limit and contains characters some
    /// brokers reject, so each provider must map it to a valid, stable name that its replicas share.
    /// </summary>
    public static async Task AssertBusConsumerIdentitiesAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        var logicalName = $"conformance-{Guid.NewGuid():N}";
        var run = Guid.NewGuid().ToString("N");
        var shortIdentity = $"conformance.{run}-a";
        var longIdentity = $"conformance.{run}-b.".PadRight(120, 'x');
        var shortDeliveries = new ConcurrentBag<TransportConformanceDelivery>();
        var longDeliveries = new ConcurrentBag<TransportConformanceDelivery>();

        await using var shortFirst = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Bus, logicalName, shortIdentity, "replica-1"),
            cancellationToken: cancellationToken
        );
        await using var shortSecond = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Bus, logicalName, shortIdentity, "replica-2"),
            cancellationToken
        );
        await using var longFirst = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Bus, logicalName, longIdentity, "replica-1"),
            cancellationToken
        );
        await using var longSecond = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Bus, logicalName, longIdentity, "replica-2"),
            cancellationToken
        );

        await Task.WhenAll(
            _StartAndCommitAsync(shortFirst, shortDeliveries, cancellationToken),
            _StartAndCommitAsync(shortSecond, shortDeliveries, cancellationToken),
            _StartAndCommitAsync(longFirst, longDeliveries, cancellationToken),
            _StartAndCommitAsync(longSecond, longDeliveries, cancellationToken)
        );

        var result = await shortFirst.PublishAsync(_CreateMessage(MessageLane.Bus, logicalName), cancellationToken);
        result.Succeeded.Should().BeTrue();

        await _WaitForCountAsync(shortDeliveries, 1, cancellationToken);
        await _WaitForCountAsync(longDeliveries, 1, cancellationToken);
        await Task.Delay(_NegativeObservationWindow(driver), cancellationToken);

        shortDeliveries.Should().ContainSingle("replicas of one consumer identity must compete for one copy");
        longDeliveries
            .Should()
            .ContainSingle("replicas of a shortened consumer identity must share one subscription and compete");
    }

    public static async Task AssertQueueOwnershipAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        var logicalName = $"conformance-{Guid.NewGuid():N}";
        var deliveries = new ConcurrentBag<TransportConformanceDelivery>();
        await using var first = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Queue, logicalName, logicalName, "replica-1"),
            cancellationToken
        );
        await using var second = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Queue, logicalName, logicalName, "replica-2"),
            cancellationToken
        );

        await Task.WhenAll(
            _StartAndCommitAsync(first, deliveries, cancellationToken),
            _StartAndCommitAsync(second, deliveries, cancellationToken)
        );

        var result = await first.PublishAsync(_CreateMessage(MessageLane.Queue, logicalName), cancellationToken);
        result.Succeeded.Should().BeTrue();

        await _WaitForCountAsync(deliveries, 1, cancellationToken);
        await Task.Delay(_NegativeObservationWindow(driver), cancellationToken);
        deliveries.Should().ContainSingle("Queue replicas must compete for one owned copy");
    }

    public static async Task AssertSameNameLaneIsolationAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        var logicalName = $"conformance-{Guid.NewGuid():N}";
        var busDeliveries = new ConcurrentBag<TransportConformanceDelivery>();
        var queueDeliveries = new ConcurrentBag<TransportConformanceDelivery>();
        await using var bus = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Bus, logicalName, "conformance.lane-isolation", "replica-1"),
            cancellationToken
        );
        await using var queue = await driver.CreateSessionAsync(
            new TransportConformanceEndpoint(MessageLane.Queue, logicalName, logicalName, "replica-1"),
            cancellationToken
        );

        await _StartAndCommitAsync(bus, busDeliveries, cancellationToken);
        await _StartAndCommitAsync(queue, queueDeliveries, cancellationToken);

        (await bus.PublishAsync(_CreateMessage(MessageLane.Bus, logicalName), cancellationToken))
            .Succeeded.Should()
            .BeTrue();
        await _WaitForCountAsync(busDeliveries, 1, cancellationToken);
        queueDeliveries.Should().BeEmpty("a Bus publication cannot enter the same-name Queue destination");

        (await queue.PublishAsync(_CreateMessage(MessageLane.Queue, logicalName), cancellationToken))
            .Succeeded.Should()
            .BeTrue();
        await _WaitForCountAsync(queueDeliveries, 1, cancellationToken);
        await Task.Delay(_NegativeObservationWindow(driver), cancellationToken);

        busDeliveries.Should().ContainSingle("the Queue send cannot cross-deliver into the Bus subscription");
        queueDeliveries.Should().ContainSingle();
    }

    public static async Task AssertRejectedStartupHasNoSideEffectsAsync(
        TransportProviderConformanceDriver driver,
        CancellationToken cancellationToken
    )
    {
        driver.Capabilities.SupportsStartupSideEffectObservation.Should().BeTrue();
        var observation = await driver.ObserveRejectedStartupAsync(cancellationToken);

        observation.Failure.Should().NotBeNull();
        observation.ReadinessAdvertised.Should().BeFalse();
        observation.ProvisioningSideEffects.Should().Be(0);
        observation.PersistenceSideEffects.Should().Be(0);
        observation.UserHandlerInvocations.Should().Be(0);
    }

    public static async Task AssertMalformedEnvelopeTerminatesAsync(
        TransportProviderConformanceDriver driver,
        TransportConformanceEndpoint endpoint,
        IReadOnlyDictionary<string, string?> headers,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        driver.Capabilities.SupportsRawEnvelopeInjection.Should().BeTrue();
        driver.Capabilities.SupportsTerminalStateObservation.Should().BeTrue();

        await driver.InjectRawEnvelopeAsync(endpoint, headers, body, cancellationToken);
        var observation = await driver.ObserveMalformedEnvelopeAsync(endpoint, cancellationToken);
        var bound = driver.MalformedEnvelopeBound;

        observation.TerminalState.Should().Be(bound.TerminalInvariant);
        observation.DeliveryCount.Should().BeLessThanOrEqualTo(bound.MaximumDeliveryCount);
        observation.UserHandlerInvocations.Should().Be(0);
        observation.DeliveriesAfterRestart.Should().Be(0);
    }

    private static async Task _StartAndCommitAsync(
        TransportConsumerConformanceSession session,
        ConcurrentBag<TransportConformanceDelivery> deliveries,
        CancellationToken cancellationToken
    )
    {
        await session.StartAsync(
            async delivery =>
            {
                deliveries.Add(delivery);
                await session.Consumer.CommitAsync(delivery.SettlementValue, CancellationToken.None);
            },
            cancellationToken: cancellationToken
        );
    }

    private static async Task _WaitForCountAsync(
        ConcurrentBag<TransportConformanceDelivery> deliveries,
        int expected,
        CancellationToken cancellationToken
    )
    {
        using var timeout = TimeSpan.FromSeconds(20).ToCancellationTokenSource(cancellationToken);
        while (deliveries.Count < expected)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static TimeSpan _NegativeObservationWindow(TransportProviderConformanceDriver driver)
    {
        return driver.MalformedEnvelopeBound.ObservationWindow;
    }

    private static TransportMessage _CreateMessage(MessageLane lane, string logicalName)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.MessageName] = logicalName,
            [MessagingHeaders.Intent] = lane.ToString(),
            ["x-headless-conformance"] = "provider-semantics",
        };

        return new TransportMessage(headers, "provider-conformance"u8.ToArray());
    }
}
