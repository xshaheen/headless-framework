// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Creates, checks, clears, and deletes the shared broker objects a <see cref="MessagingTopology"/> describes. A
/// transport implements it so the deployment's <c>resources setup</c> creates its topology instead of the running host.
/// </summary>
/// <remarks>
/// <para>
/// Register one implementation per transport from its <c>Use{Provider}</c> setup with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ITransportTopologyProvisioner, TProvisioner&gt;())</c>.
/// Messaging adapts each into one stateful resource of type <see cref="Broker"/> named <c>topology</c>, and builds the
/// topology from the registered consumers and messages each time an operation runs.
/// </para>
/// <para>
/// Every operation must be idempotent. Skip <see cref="ConsumerSubscriptionKind.EveryInstance"/> subscriptions: a
/// process creates its own. An operation the broker cannot express throws
/// <see cref="Headless.Hosting.StatefulResourceUnsupportedException"/>, which the resource reports as unsupported; any
/// other exception is a failure. Once a transport provisions here, its consumer clients create shared objects
/// only when <see cref="ConsumerClientRequest.ProvisionsTopology"/> is set, and otherwise declare nothing for them: unless
/// <see cref="MessagingOptions.VerifyTopology"/> is off, the running host calls <see cref="CheckAsync"/> once after it
/// starts, behind its readiness check.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITransportTopologyProvisioner
{
    /// <summary>Gets the broker's name, such as <c>RabbitMQ</c>: the resource's type.</summary>
    string Broker { get; }

    /// <summary>Creates whatever <paramref name="topology"/> needs that the broker lacks.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the topology exists.</returns>
    Task SetupAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Checks, without creating anything, that the broker holds what <paramref name="topology"/> needs.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the topology exists.</returns>
    /// <exception cref="Headless.Hosting.StatefulResourceException">An object is missing.</exception>
    Task CheckAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Removes the messages the topology's queues or streams hold, and keeps the objects.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the messages are gone.</returns>
    Task ClearAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Deletes what <see cref="SetupAsync"/> created.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the objects are deleted.</returns>
    Task TeardownAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Reads the topology's current state as ordered name and value pairs, for an operator to read.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The state, in display order.</returns>
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
        MessagingTopology topology,
        CancellationToken cancellationToken
    );
}
