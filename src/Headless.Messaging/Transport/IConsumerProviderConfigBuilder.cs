// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Transport;

/// <summary>
/// Attaches a provider's per-consumer config to a declared consumer. <see cref="ConsumerTuningBuilder"/> implements it
/// explicitly, so it stays out of the application's code completion while a provider's <c>Use{Provider}</c> tuning
/// extension calls it.
/// </summary>
/// <remarks>
/// A config attached here is keyed by its runtime type: a later config of the same type replaces an earlier one.
/// <b>Evolution policy:</b> the core implements it and providers call it; new members arrive as default interface members, and
/// existing members never gain parameters.
/// </remarks>
public interface IConsumerProviderConfigBuilder
{
    /// <summary>Attaches <paramref name="config"/> to the consumer being tuned.</summary>
    /// <param name="config">The provider's config object; keyed by its runtime type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    void SetConsumerProviderConfig(object config);
}
