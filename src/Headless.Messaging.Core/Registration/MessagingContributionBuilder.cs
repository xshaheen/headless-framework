// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Registration;

/// <summary>
/// Collects one module's Messaging registrations for <c>services.ConfigureMessaging(...)</c>. Every registration is
/// recorded as an immutable descriptor in the service collection and applied when messaging starts, so a module can
/// contribute before or after the host calls <c>AddHeadlessMessaging</c>.
/// </summary>
/// <remarks>
/// A contribution never configures the Messaging runtime itself: transports, storage, and host options stay with the
/// single <c>AddHeadlessMessaging</c> call. In a host that never calls it, contributions stay inert.
/// </remarks>
[PublicAPI]
public sealed class MessagingContributionBuilder
{
    private readonly MessageRegistrationSink _sink;

    internal MessagingContributionBuilder(IServiceCollection services, ConsumerRegistry registry)
    {
        _sink = new MessageRegistrationSink(services, registry);
        Bus = new BusRegistrationBuilder(_sink);
        Queue = new QueueRegistrationBuilder(_sink);
    }

    /// <summary>Gets the registration root for Bus consumers.</summary>
    public IBusRegistrationBuilder Bus { get; }

    /// <summary>Gets the registration root for Queue consumers.</summary>
    public IQueueRegistrationBuilder Queue { get; }

    /// <summary>
    /// Contributes one consumer without declaring the message's contract settings, so it can join a message that
    /// another registration declares. Contributing the same consumer twice with identical settings is harmless; any
    /// difference fails when messaging starts.
    /// </summary>
    internal MessagingContributionBuilder AddConsumerContribution<TMessage, TConsumer>(
        MessageLane lane,
        string consumerIdentity,
        string messageContractVersion,
        string? messageName = null,
        string? group = null,
        byte concurrency = 1
    )
        where TMessage : class
        where TConsumer : class, IConsume<TMessage>
    {
        Argument.IsNotNullOrWhiteSpace(consumerIdentity);
        MessagingOptions.ValidateContractVersion(messageContractVersion);

        _sink.Services.TryAddScoped<TConsumer>();
        _sink.Services.TryAddScoped<IConsume<TMessage>>(sp => sp.GetRequiredService<TConsumer>());
        _sink.Register(
            new MessageRegistration(
                typeof(TMessage),
                lane,
                messageName,
                CorrelationSelector: null,
                ProviderConfigs: new Dictionary<Type, object>(),
                Consumers:
                [
                    new MessageConsumerRegistration(
                        typeof(TConsumer),
                        lane,
                        IsAssemblyScan: false,
                        group,
                        concurrency,
                        HandlerId: null,
                        ConsumerIdentity: consumerIdentity,
                        CircuitBreakerOverride: null,
                        ProviderConfigs: new Dictionary<Type, object>()
                    ),
                ],
                ContractVersion: messageContractVersion,
                DeclaresMessage: false
            )
        );

        return this;
    }
}
