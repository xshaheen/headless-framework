// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Messaging.Transport;

/// <summary>
/// Provisions, verifies, purges, and deletes the shared broker objects a <see cref="MessagingTopology"/> describes. A
/// transport implements it so the deployment's <c>resources provision</c> creates its topology instead of the running
/// host.
/// </summary>
/// <remarks>
/// <para>
/// Register one implementation per transport from its <c>Use{Provider}</c> setup with
/// <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ITransportTopologyProvisioner, TProvisioner&gt;())</c>.
/// Messaging adapts each into one <see cref="Headless.Hosting.IResourceProvisioner"/> of kind <see cref="Kind"/> named
/// <c>topology</c>, and builds the topology from the registered consumers and messages each time an operation runs.
/// </para>
/// <para>
/// Every operation must be idempotent. Skip <see cref="ConsumerSubscriptionKind.EveryInstance"/> subscriptions: a
/// process creates its own. A broker that cannot purge or delete states it with <see cref="CanPurge"/> or
/// <see cref="CanDelete"/>, and the operation is then skipped and reported as unsupported; any exception an operation
/// throws is a failure. Once a transport provisions here, its consumer clients create shared objects only when
/// <see cref="ConsumerClientRequest.ProvisionsTopology"/> is set, and otherwise declare nothing for them: the running
/// host calls <see cref="VerifyAsync"/> once after it starts, as <see cref="StartupCheck"/> says.
/// </para>
/// </remarks>
[PublicAPI]
public interface ITransportTopologyProvisioner
{
    /// <summary>Gets the broker's name, such as <c>RabbitMQ</c>: the resource's kind.</summary>
    string Kind { get; }

    /// <summary>
    /// Gets how a starting host checks the topology with <see cref="VerifyAsync"/>. Default:
    /// <see cref="ResourceStartupCheck.GateReadiness"/>, so a missing queue keeps the host unready instead of failing it.
    /// </summary>
    ResourceStartupCheck StartupCheck => ResourceStartupCheck.GateReadiness;

    /// <summary>Gets whether <see cref="PurgeAsync"/> applies to this broker. Default: <see langword="true"/>.</summary>
    bool CanPurge => true;

    /// <summary>Gets whether <see cref="DeleteAsync"/> applies to this broker. Default: <see langword="true"/>.</summary>
    bool CanDelete => true;

    /// <summary>Creates whatever <paramref name="topology"/> needs that the broker lacks.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the topology exists.</returns>
    Task ProvisionAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Checks, without creating anything, that the broker holds what <paramref name="topology"/> needs.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the topology exists.</returns>
    /// <exception cref="Headless.Hosting.ResourceProvisioningException">An object is missing.</exception>
    Task VerifyAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Removes the messages the topology's queues or streams hold, and keeps the objects.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the messages are gone.</returns>
    Task PurgeAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Deletes what <see cref="ProvisionAsync"/> created.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the objects are deleted.</returns>
    Task DeleteAsync(MessagingTopology topology, CancellationToken cancellationToken);

    /// <summary>Reads the topology's current state as ordered name and value pairs, for an operator to read.</summary>
    /// <param name="topology">The registered subscriptions and messages.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The state, in display order.</returns>
    Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(
        MessagingTopology topology,
        CancellationToken cancellationToken
    );
}
