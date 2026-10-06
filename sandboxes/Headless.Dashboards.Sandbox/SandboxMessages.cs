// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Headless.Dashboards.Sandbox;

/// <summary>A message whose consumer succeeds or fails as the publisher asks, so failed rows are deterministic.</summary>
/// <param name="OrderId">Sequence number, visible in the message content.</param>
/// <param name="ShouldFail">Whether the consumer throws.</param>
public sealed record SandboxOrderPlaced(int OrderId, bool ShouldFail);

/// <summary>Consumes <see cref="SandboxOrderPlaced"/>; throws when the message asks to fail.</summary>
[BusConsumer("sandbox.orders")]
public sealed class SandboxOrderPlacedConsumer : IConsume<SandboxOrderPlaced>
{
    public ValueTask ConsumeAsync(ConsumeContext<SandboxOrderPlaced> context, CancellationToken cancellationToken)
    {
        if (context.Message.ShouldFail)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Sandbox order {context.Message.OrderId} failed on purpose."
                )
            );
        }

        return ValueTask.CompletedTask;
    }
}
