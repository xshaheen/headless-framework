// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Capabilities;

/// <summary>Broker-observed scenarios tracked for every messaging transport provider.</summary>
[PublicAPI]
public enum TransportConformanceScenario
{
    QueueRoundTrip,
    BusRoundTrip,
    HeaderRoundTrip,
    EmptyBodyDispatch,
    CommitSettlement,
    RejectRedelivery,
    ConsumerPauseRecovery,
    BrokerInterruptionRecovery,
    StaleSettlement,
    HandlerFailureRedelivery,
    BoundedGracefulShutdown,
}

/// <summary>Conformance declarations for one provider's broker-backed integration leaf.</summary>
[PublicAPI]
public sealed record TransportConformanceProfile(
    string Provider,
    bool IsRealBrokerLeafEnabled,
    IReadOnlyDictionary<TransportConformanceScenario, ConformanceSupport> Scenarios
)
{
    private const string _GapIssueUrl = "https://github.com/xshaheen/headless-framework/issues/359";

    public static TransportConformanceProfile CreateDisabled(string provider)
    {
        var scenarios = Enum.GetValues<TransportConformanceScenario>()
            .ToDictionary(
                scenario => scenario,
                scenario =>
                    ConformanceSupport.Unsupported(
                        $"{provider} does not yet have executable evidence for {scenario}.",
                        _GapIssueUrl
                    )
            );

        return new TransportConformanceProfile(provider, false, scenarios);
    }

    public TransportConformanceProfile EnableRealBrokerLeaf()
    {
        return this with { IsRealBrokerLeafEnabled = true };
    }

    public TransportConformanceProfile WithScenario(TransportConformanceScenario scenario, ConformanceSupport support)
    {
        var scenarios = Scenarios.ToDictionary();
        scenarios[scenario] = support;
        return this with { Scenarios = scenarios };
    }
}

/// <summary>Authoritative provider/scenario roster for broker-backed messaging conformance tests.</summary>
[PublicAPI]
public static class TransportConformanceManifest
{
    private static readonly TransportConformanceScenario[] _MandatoryBaselineScenarios =
    [
        TransportConformanceScenario.QueueRoundTrip,
        TransportConformanceScenario.HeaderRoundTrip,
        TransportConformanceScenario.CommitSettlement,
        TransportConformanceScenario.RejectRedelivery,
    ];

    public static IReadOnlyDictionary<string, TransportConformanceProfile> Providers { get; } =
        new Dictionary<string, TransportConformanceProfile>(StringComparer.Ordinal)
        {
            ["NATS"] = TransportConformanceProfile
                .CreateDisabled("NATS")
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["RabbitMQ"] = TransportConformanceProfile
                .CreateDisabled("RabbitMQ")
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.EmptyBodyDispatch, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["AWS/LocalStack"] = TransportConformanceProfile
                .CreateDisabled("AWS/LocalStack")
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.EmptyBodyDispatch,
                    ConformanceSupport.NotApplicable(
                        "Amazon SNS rejects empty message bodies at the protocol boundary."
                    )
                )
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Kafka"] = TransportConformanceProfile
                .CreateDisabled("Kafka")
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(
                    TransportConformanceScenario.BusRoundTrip,
                    ConformanceSupport.NotApplicable(
                        "The current Kafka transport contract is queue/consumer-group based and has no fanout bus topology."
                    )
                )
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Pulsar"] = TransportConformanceProfile
                .CreateDisabled("Pulsar")
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
            ["Azure Service Bus"] = TransportConformanceProfile
                .CreateDisabled("Azure Service Bus")
                .WithScenario(TransportConformanceScenario.QueueRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BusRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.HeaderRoundTrip, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.CommitSettlement, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.RejectRedelivery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.ConsumerPauseRecovery, ConformanceSupport.Supported)
                .WithScenario(TransportConformanceScenario.BoundedGracefulShutdown, ConformanceSupport.Supported)
                .EnableRealBrokerLeaf(),
        };

    public static IReadOnlyList<string> GetValidationErrors()
    {
        return Providers.Values.SelectMany(GetValidationErrors).ToList();
    }

    public static IReadOnlyList<string> GetValidationErrors(TransportConformanceProfile profile)
    {
        var errors = new List<string>();
        var expectedScenarios = Enum.GetValues<TransportConformanceScenario>();

        if (string.IsNullOrWhiteSpace(profile.Provider))
        {
            errors.Add("Conformance profiles require a provider name.");
        }

        var missingScenarios = expectedScenarios.Except(profile.Scenarios.Keys).ToList();
        var unknownScenarios = profile.Scenarios.Keys.Except(expectedScenarios).ToList();

        foreach (var scenario in missingScenarios)
        {
            errors.Add($"{profile.Provider} is missing the {scenario} conformance cell.");
        }

        foreach (var scenario in unknownScenarios)
        {
            errors.Add($"{profile.Provider} declares the unknown {scenario} conformance cell.");
        }

        foreach (var (scenario, support) in profile.Scenarios)
        {
            errors.AddRange(support.GetValidationErrors(scenario).Select(error => $"{profile.Provider}: {error}"));
        }

        if (profile.IsRealBrokerLeafEnabled)
        {
            foreach (var scenario in _MandatoryBaselineScenarios)
            {
                if (
                    !profile.Scenarios.TryGetValue(scenario, out var support)
                    || support.Status != ConformanceStatus.Supported
                )
                {
                    errors.Add($"{profile.Provider}: enabled real-broker leaves must support {scenario}.");
                }
            }
        }

        return errors;
    }
}
