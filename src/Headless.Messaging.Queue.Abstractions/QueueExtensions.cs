// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>Offers fluent options callbacks for point-to-point messages.</summary>
/// <remarks>
/// Each callback runs synchronously once on a fresh builder before delegating to the existing options overload.
/// Async-void callbacks are unsupported. The publisher retains ownership of validation and delivery policy.
/// </remarks>
[PublicAPI]
public static class QueueExtensions
{
    extension(IQueue queue)
    {
        /// <summary>Enqueues a nullable message payload with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The queue or configuration callback is null.</exception>
        public Task EnqueueAsync<T>(
            T? contentObj,
            Action<QueueOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(queue);
            Argument.IsNotNull(configure);
            var builder = new QueueOptionsBuilder();
            configure(builder);
            return queue.EnqueueAsync(contentObj, builder.Build(), cancellationToken);
        }
    }
}
