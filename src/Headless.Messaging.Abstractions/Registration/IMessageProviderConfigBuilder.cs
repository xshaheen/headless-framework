// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Attaches a provider's per-contract config to a message contract. Every <see cref="IBusContractBuilder{TMessage}"/>
/// and <see cref="IQueueContractBuilder{TMessage}"/> the core hands an application implements it explicitly, so it stays
/// out of the application's code completion while a provider's <c>Use{Provider}</c> extension casts to it.
/// </summary>
/// <remarks>
/// <para>
/// A config attached here reaches the provider through <c>MessageMetadata.ProviderConfigs</c>, keyed by its
/// runtime type: a later config of the same type replaces an earlier one. A config that also implements
/// <c>IProviderHeaderContributions</c> or <c>IProviderMissingPayloadValidation</c> takes part in publishing.
/// </para>
/// <para>
/// The interface lives beside the contract builders that implement it, which is why it ships in the abstractions
/// package. <b>Evolution policy:</b> the core implements it and providers call it; new members arrive as default interface members,
/// and existing members never gain parameters.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The message contract type.</typeparam>
public interface IMessageProviderConfigBuilder<out TMessage>
    where TMessage : class
{
    /// <summary>Attaches <paramref name="config"/> to the contract's lane.</summary>
    /// <param name="config">The provider's config object; keyed by its runtime type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The contract's registration already completed.</exception>
    void SetMessageProviderConfig(object config);
}
