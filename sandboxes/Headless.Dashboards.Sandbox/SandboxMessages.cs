// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

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
[BusConsumer("sandbox.orders")]
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
