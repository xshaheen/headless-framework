// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.CircuitBreaker;
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
    private TimeSpan? _inboxRetention;
    private ConsumerCircuitBreakerOptions? _circuitBreaker;
    private FailurePolicyDefinition? _failurePolicy;

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

    /// <summary>
    /// Overrides how long the consumer's terminal inbox rows are kept, for inbox generations admitted from now on. The
    /// default is 30 days. An every-instance consumer keeps no inbox, so tuning its retention fails startup.
    /// </summary>
    /// <param name="retention">A positive whole-second duration no greater than <see cref="int.MaxValue"/> seconds.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="retention"/> is not a positive whole-second duration.</exception>
    public ConsumerTuningBuilder InboxRetention(TimeSpan retention)
    {
        _inboxRetention = ValidateInboxRetention(retention);
        return this;
    }

    /// <summary>
    /// Overrides the host circuit breaker for this consumer. A setting left <see langword="null"/> falls back to
    /// <see cref="CircuitBreakerOptions"/>. An every-instance consumer has no retry backlog to protect, so tuning its
    /// circuit breaker fails startup.
    /// </summary>
    /// <param name="configure">Changes the consumer's circuit breaker overrides.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    public ConsumerTuningBuilder CircuitBreaker(Action<ConsumerCircuitBreakerOptions> configure)
    {
        Argument.IsNotNull(configure);

        var options = new ConsumerCircuitBreakerOptions();
        configure(options);
        _circuitBreaker = options;
        return this;
    }

    /// <summary>
    /// Replaces the failure policy the consumer declares, or the host default when it declares none, on this host.
    /// <c>Headless:Messaging:Consumers:{identity}:FailurePolicy</c> configuration still adjusts its retry counts and
    /// delays. An every-instance consumer never retries, so tuning its failure policy fails startup.
    /// </summary>
    /// <typeparam name="TPolicy">The policy type; it is built once, during this call.</typeparam>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public ConsumerTuningBuilder FailurePolicy<TPolicy>()
        where TPolicy : Reliability.FailurePolicy, new()
    {
        _failurePolicy = FailurePolicyDefinition.Create<TPolicy>();
        return this;
    }

    /// <summary>
    /// Replaces the consumer's failure policy with one described inline. See <see cref="FailurePolicy{TPolicy}"/>.
    /// </summary>
    /// <param name="configure">Describes the policy; it runs once, synchronously, during this call.</param>
    /// <returns>This builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The policy's retry counts or delays are out of range.</exception>
    public ConsumerTuningBuilder FailurePolicy([InstantHandle] Action<FailurePolicyBuilder> configure)
    {
        _failurePolicy = FailurePolicyDefinition.Create(configure);
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
        new(
            Identity,
            _concurrency,
            [.. _middleware],
            _providerConfigs.Build(),
            _inboxRetention,
            _circuitBreaker,
            _failurePolicy
        );

    internal static TimeSpan ValidateInboxRetention(TimeSpan retention)
    {
        const string message =
            "Inbox retention must be a positive whole-second duration no greater than Int32.MaxValue seconds.";
        Argument.IsPositive(retention, message);
        Argument.IsZero(retention.Ticks % TimeSpan.TicksPerSecond, message, nameof(retention));
        Argument.IsLessThanOrEqualTo(retention.TotalSeconds, int.MaxValue, message, nameof(retention));

        return retention;
    }
}

/// <summary>One immutable <c>Tune</c> call, applied to the host's consumers when its consumer registry is built.</summary>
internal sealed record ConsumerTuning(
    string Identity,
    byte? Concurrency,
    Type[] Middleware,
    IReadOnlyDictionary<Type, object> ProviderConfigs,
    TimeSpan? InboxRetention,
    ConsumerCircuitBreakerOptions? CircuitBreaker,
    FailurePolicyDefinition? FailurePolicy = null
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
