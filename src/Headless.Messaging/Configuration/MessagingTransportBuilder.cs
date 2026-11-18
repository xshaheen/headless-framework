// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>
/// Chooses the provider of one named transport, returned by <see cref="MessagingSetupBuilder.AddTransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// A named transport is a second broker the host publishes to and consumes from beside the default transport, for
/// example RabbitMQ for internal events and another broker for a partner integration. It shares the host's storage,
/// inbox, retry relay, and dashboard; only the broker side is its own. Transport packages add their <c>Use…</c> members
/// to this builder.
/// </para>
/// <para>
/// A transport package registers the named instance's services keyed by <see cref="Name"/>: <c>IBusTransport</c>,
/// <c>IQueueTransport</c>, <c>IConsumerClientFactory</c>, <c>MessagingProviderCapabilities</c>, and any
/// <c>ITransportTopologyProvisioner</c>. Everything a named instance builds, such as its connection pool and its options,
/// must be its own, built from options named <see cref="Name"/>, so two instances of one provider never share a
/// connection or a setting.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class MessagingTransportBuilder
{
    internal MessagingTransportBuilder(MessagingSetupBuilder setup, string name)
    {
        Setup = Argument.IsNotNull(setup);
        Name = Argument.IsNotNullOrWhiteSpace(name);
    }

    /// <summary>
    /// The transport's name, as passed to <see cref="MessagingSetupBuilder.AddTransport"/>. It is the service key of the
    /// instance's keyed services, the name <c>Tune(identity, c =&gt; c.Transport(name))</c> binds a consumer to, and the
    /// value stored on each published row the instance sends.
    /// </summary>
    public string Name { get; }

    internal MessagingSetupBuilder Setup { get; }

    internal bool IsConfigured { get; private set; }

    /// <summary>
    /// Registers the provider of this named transport. Called once by a transport package's <c>Use…</c> member.
    /// </summary>
    /// <param name="extension">
    /// Adds the instance's services, keyed by <see cref="Name"/>, and one unkeyed <see cref="MessageQueueMarkerService"/>
    /// whose <see cref="MessageQueueMarkerService.TransportName"/> is <see cref="Name"/>.
    /// </param>
    /// <returns>The messaging setup builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="extension"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">This transport already has a provider.</exception>
    public MessagingSetupBuilder RegisterTransport(IMessagesOptionsExtension extension)
    {
        Argument.IsNotNull(extension);

        if (IsConfigured)
        {
            throw new InvalidOperationException(
                $"Transport '{Name}' already has a provider. Call AddTransport(name) again to register another broker."
            );
        }

        IsConfigured = true;
        Setup.RegisterExtension(extension);

        return Setup;
    }
}
