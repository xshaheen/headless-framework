// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Registration;
using Headless.Reliability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging;

/// <summary>
/// Changes the deployment settings of one declared consumer, identified by its consumer identity. Tuning cannot declare
/// a consumer or change its identity, lane, or messages; the consumer's attribute owns those.
/// </summary>
/// <remarks>
/// Each call replaces the previous value of that setting for this tuning. Middleware accumulates. When several
/// <c>Tune</c> calls name the same identity, they apply in registration order, so a later value wins, and
/// <c>Headless:Messaging:Consumers:{identity}</c> configuration applies after all of them. A consumer that handles
/// several messages takes the settings for every one of them.
/// </remarks>
[PublicAPI]
public sealed class ConsumerTuningBuilder : IConsumerProviderConfigBuilder
{
    private readonly List<Type> _middleware = [];
    private readonly ProviderConfigBag _providerConfigs = new();
    private byte? _concurrency;
    private Type? _failurePolicy;

    internal ConsumerTuningBuilder(string identity)
    {
        Identity = identity;
    }

    internal string Identity { get; }

    /// <summary>Limits the number of messages this consumer handles concurrently on this host.</summary>
    /// <param name="maxConcurrent">Maximum concurrent deliveries; must be greater than zero.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is zero.</exception>
    public ConsumerTuningBuilder Concurrency(byte maxConcurrent)
    {
        _concurrency = Argument.IsPositive(maxConcurrent);
        return this;
    }

    /// <summary>Overrides the failure policy the consumer declares in its attribute.</summary>
    /// <typeparam name="TPolicy">The failure policy type.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    public ConsumerTuningBuilder FailurePolicy<TPolicy>()
        where TPolicy : IFailurePolicy
    {
        _failurePolicy = typeof(TPolicy);
        return this;
    }

    /// <summary>
    /// Runs <typeparamref name="TMiddleware"/> around every delivery to this consumer on this host, inside the global and
    /// per-message consume middleware. The middleware is resolved from the delivery's service scope; when it is not
    /// registered yet, it is registered as scoped.
    /// </summary>
    /// <typeparam name="TMiddleware">
    /// The consume middleware type. It sees the untyped <see cref="ConsumeContext"/> because one consumer may handle
    /// several messages.
    /// </typeparam>
    /// <returns>This builder, for chaining.</returns>
    public ConsumerTuningBuilder UseMiddleware<TMiddleware>()
        where TMiddleware : class, IConsumeMiddleware<ConsumeContext>
    {
        // The same middleware tuned twice onto one consumer still runs once.
        if (!_middleware.Contains(typeof(TMiddleware)))
        {
            _middleware.Add(typeof(TMiddleware));
        }

        return this;
    }

    void IConsumerProviderConfigBuilder.SetConsumerProviderConfig(object config)
    {
        _providerConfigs.Set(config);
    }

    internal ConsumerTuning Build() =>
        new(Identity, _concurrency, _failurePolicy, [.. _middleware], _providerConfigs.Build());
}

/// <summary>One immutable <c>Tune</c> call, applied to the host's consumers when its registrations drain.</summary>
internal sealed record ConsumerTuning(
    string Identity,
    byte? Concurrency,
    Type? FailurePolicy,
    Type[] Middleware,
    IReadOnlyDictionary<Type, object> ProviderConfigs
);

/// <summary>One <c>Tune</c> call recorded in the service collection.</summary>
internal sealed record MessagingTuningContribution(ConsumerTuning Tuning);

/// <summary>The <c>ConsumeOnly</c> entries one <c>AddHeadlessMessaging</c> call authored.</summary>
internal sealed record MessagingConsumeOnlyContribution(string[] Entries);

internal static class MessagingTuningRecording
{
    public static void AddConsumerTuning(
        this IServiceCollection services,
        string identity,
        Action<ConsumerTuningBuilder> configure
    )
    {
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.IsNotNull(configure);

        var builder = new ConsumerTuningBuilder(identity);
        configure(builder);
        var tuning = builder.Build();

        foreach (var middleware in tuning.Middleware)
        {
            services.TryAdd(ServiceDescriptor.Scoped(middleware, middleware));
        }

        services.AddSingleton(new MessagingTuningContribution(tuning));
    }
}
