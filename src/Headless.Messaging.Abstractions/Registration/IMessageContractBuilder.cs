// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;

namespace Headless.Messaging.Registration;

/// <summary>
/// Configures one message contract, declared once for both lanes with <c>Message&lt;T&gt;(name, version)</c>.
/// </summary>
/// <remarks>
/// The contract belongs to the message schema rather than to a lane: publishing on the Bus and sending on the Queue both
/// resolve the message type to the same name and version. Settings that only make sense for one lane's route are chained
/// with <see cref="OnBus"/> and <see cref="OnQueue"/>, and they touch only that route. The message type itself needs no
/// attribute and no reference to Headless.
/// </remarks>
/// <typeparam name="TMessage">The message type the contract describes.</typeparam>
[PublicAPI]
public interface IMessageContractBuilder<TMessage>
    where TMessage : class
{
    /// <summary>Derives a correlation identifier from the outgoing payload on both lanes.</summary>
    /// <param name="selector">Reads the correlation identifier from the message.</param>
    /// <returns>This builder, for chaining.</returns>
    IMessageContractBuilder<TMessage> CorrelateBy(Func<TMessage, string?> selector);

    /// <summary>Configures settings of the message's Bus route only.</summary>
    /// <param name="configure">Changes the Bus route settings.</param>
    /// <returns>This builder, for chaining.</returns>
    IMessageContractBuilder<TMessage> OnBus([InstantHandle] Action<IBusContractBuilder<TMessage>> configure);

    /// <summary>Configures settings of the message's Queue route only.</summary>
    /// <param name="configure">Changes the Queue route settings.</param>
    /// <returns>This builder, for chaining.</returns>
    IMessageContractBuilder<TMessage> OnQueue([InstantHandle] Action<IQueueContractBuilder<TMessage>> configure);
}
