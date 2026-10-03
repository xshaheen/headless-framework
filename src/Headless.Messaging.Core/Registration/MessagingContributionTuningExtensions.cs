// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;

namespace Headless.Messaging;

/// <summary>Adds consumer tuning to a <c>ConfigureMessaging</c> contribution.</summary>
[PublicAPI]
public static class MessagingContributionTuningExtensions
{
    /// <summary>
    /// Tunes the deployment settings of one declared consumer, for example
    /// <c>Tune("billing.invoice-projection", consumer =&gt; consumer.Concurrency(16))</c>.
    /// </summary>
    /// <remarks>
    /// The identity is checked when messaging starts: an identity no registered consumer declares fails startup.
    /// <paramref name="configure"/> runs once, synchronously, during this call. Tuning lives in
    /// <c>Headless.Messaging.Core</c> because it configures the runtime's middleware, inbox, and circuit breaker.
    /// </remarks>
    /// <param name="builder">The contribution being configured.</param>
    /// <param name="identity">The consumer's identity.</param>
    /// <param name="configure">Changes the consumer's deployment settings.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="identity"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static MessagingContributionBuilder Tune(
        this MessagingContributionBuilder builder,
        string identity,
        [InstantHandle] Action<ConsumerTuningBuilder> configure
    )
    {
        Argument.IsNotNull(builder);

        builder.Services.AddConsumerTuning(identity, configure);

        return builder;
    }
}
