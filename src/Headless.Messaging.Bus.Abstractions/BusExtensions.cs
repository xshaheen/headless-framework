// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>Offers fluent options callbacks for broadcast messages.</summary>
/// <remarks>
/// Each callback runs synchronously once on a fresh builder before delegating to the existing options overload.
/// Async-void callbacks are unsupported. The publisher retains ownership of validation and delivery policy.
/// </remarks>
[PublicAPI]
public static class BusExtensions
{
    extension(IBus bus)
    {
        /// <summary>Publishes a nullable message payload with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The bus or configuration callback is null.</exception>
        public Task PublishAsync<T>(
            T? contentObj,
            Action<PublishOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(bus);
            Argument.IsNotNull(configure);
            var builder = new PublishOptionsBuilder();
            configure(builder);
            return bus.PublishAsync(contentObj, builder.Build(), cancellationToken);
        }
    }
}
