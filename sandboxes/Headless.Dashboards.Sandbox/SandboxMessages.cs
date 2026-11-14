// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Reliability;

namespace Headless.Dashboards.Sandbox;

/// <summary>A message whose consumer succeeds or fails as the publisher asks, so failed rows are deterministic.</summary>
/// <param name="OrderId">Sequence number, visible in the message content.</param>
/// <param name="ShouldFail">Whether the consumer throws.</param>
/// <param name="Cause">What the failure says, so failures group by cause; <see langword="null"/> uses the default.</param>
public sealed record SandboxOrderPlaced(int OrderId, bool ShouldFail, string? Cause = null)
{
    /// <summary>The cause whose failures throw <see cref="TimeoutException"/>, a group of their own.</summary>
    public const string StockTimeoutCause = "timed out reserving stock";
}

/// <summary>Consumes <see cref="SandboxOrderPlaced"/>; throws when the message asks to fail.</summary>
[BusConsumer(SandboxConsumerIds.Orders)]
public sealed class SandboxOrderPlacedConsumer : IConsume<SandboxOrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<SandboxOrderPlaced> context, CancellationToken cancellationToken)
    {
        if (context.Message.ShouldFail)
        {
            var text = string.Create(
                CultureInfo.InvariantCulture,
                $"Sandbox order {context.Message.OrderId} {context.Message.Cause ?? "failed on purpose"}."
            );

            // A failure fingerprint hashes the exception type and throw site, never the message, so a cause that
            // must form its own Needs you group throws its own exception type.
            throw context.Message.Cause is SandboxOrderPlaced.StockTimeoutCause
                ? new TimeoutException(text)
                : new InvalidOperationException(text);
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>A Queue-lane message whose consumer retries a failure later instead of ending it.</summary>
/// <param name="ShipmentId">Sequence number, visible in the message content.</param>
/// <param name="ShouldFail">Whether the consumer throws.</param>
public sealed record SandboxShipmentRequested(int ShipmentId, bool ShouldFail);

/// <summary>
/// Retries a failed shipment eight times, a minute after the first failure and then at doubling delays up to 15
/// minutes, so a failing row stays <c>Failed</c> with a retry pending for over an hour and its retry count moves
/// while a tester watches.
/// </summary>
public sealed class SandboxShipmentRetryPolicy : FailurePolicy
{
    protected override void Configure(FailurePolicyBuilder policy)
    {
        policy.Delayed(retries: 8, initialDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromMinutes(15));
    }
}

/// <summary>Consumes <see cref="SandboxShipmentRequested"/>; a failure is retried under <see cref="SandboxShipmentRetryPolicy"/>.</summary>
[QueueConsumer(SandboxConsumerIds.Shipments, FailurePolicy = typeof(SandboxShipmentRetryPolicy))]
public sealed class SandboxShipmentRequestedConsumer : IConsume<SandboxShipmentRequested>
{
    public ValueTask ConsumeAsync(ConsumeContext<SandboxShipmentRequested> context, CancellationToken cancellationToken)
    {
        if (context.Message.ShouldFail)
        {
            // IOException, not TimeoutException: the circuit breaker counts a timeout as transient, and eight retries
            // of three rows would open this consumer's circuit and defer the retries a tester is waiting to see.
            throw new IOException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sandbox shipment {context.Message.ShipmentId} could not be booked: the carrier connection was reset."
                )
            );
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>A message whose consumer times out on request, the failure its circuit breaker counts.</summary>
/// <param name="PaymentId">Sequence number, visible in the message content.</param>
/// <param name="ShouldFail">Whether the consumer throws.</param>
public sealed record SandboxPaymentCaptured(int PaymentId, bool ShouldFail);

/// <summary>
/// Consumes <see cref="SandboxPaymentCaptured"/>. The host tunes its circuit breaker to open after two timeouts in a
/// row and stay open for 30 minutes, so two failing messages leave an open circuit to reset.
/// </summary>
[BusConsumer(SandboxConsumerIds.Payments)]
public sealed class SandboxPaymentCapturedConsumer : IConsume<SandboxPaymentCaptured>
{
    public ValueTask ConsumeAsync(ConsumeContext<SandboxPaymentCaptured> context, CancellationToken cancellationToken)
    {
        if (context.Message.ShouldFail)
        {
            throw new TimeoutException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sandbox payment {context.Message.PaymentId} timed out at the payment gateway."
                )
            );
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>A Queue-lane message whose consumer takes as long as the publisher asks before it succeeds.</summary>
/// <param name="Sku">Sequence number, visible in the message content.</param>
/// <param name="CountSeconds">How long the consumer takes; while it runs, the received row counts as backlog.</param>
public sealed record SandboxStockCounted(int Sku, int CountSeconds);

/// <summary>
/// Consumes <see cref="SandboxStockCounted"/> slowly and always succeeds, so its received rows stay stored and
/// unhandled long enough to read as a backlog.
/// </summary>
[QueueConsumer(SandboxConsumerIds.StockCounts)]
public sealed class SandboxStockCountedConsumer : IConsume<SandboxStockCounted>
{
    public async ValueTask ConsumeAsync(
        ConsumeContext<SandboxStockCounted> context,
        CancellationToken cancellationToken
    )
    {
        await Task.Delay(TimeSpan.FromSeconds(context.Message.CountSeconds), cancellationToken);
    }
}

/// <summary>A Queue-lane message whose consumer the host limits to one delivery every 30 seconds.</summary>
/// <param name="QuoteId">Sequence number, visible in the message content.</param>
public sealed record SandboxQuoteRequested(int QuoteId);

/// <summary>
/// Consumes <see cref="SandboxQuoteRequested"/> at once and always succeeds. The host's rate limit is what makes a
/// burst wait, so its received rows stay stored and unhandled past the dispatch grace, where retry pickup finds them.
/// </summary>
[QueueConsumer(SandboxConsumerIds.CarrierQuotes)]
public sealed class SandboxQuoteRequestedConsumer : IConsume<SandboxQuoteRequested>
{
    public ValueTask ConsumeAsync(ConsumeContext<SandboxQuoteRequested> context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>The stable consumer identities the host tunes and the scenarios name.</summary>
public static class SandboxConsumerIds
{
    public const string Orders = "sandbox.orders";
    public const string Shipments = "sandbox.shipments";
    public const string Payments = "sandbox.payments";
    public const string StockCounts = "sandbox.stock-counts";
    public const string CarrierQuotes = "sandbox.carrier-quotes";
}
