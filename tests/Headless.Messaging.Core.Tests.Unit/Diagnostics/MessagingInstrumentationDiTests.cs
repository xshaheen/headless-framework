// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Diagnostics;

/// <summary>
/// Pins the DI wiring in <c>Setup.cs</c> (<c>_RegisterCoreMessagingServices</c>, around the
/// <c>setup.Instrumentation.BuildEnrichers()</c> call) end-to-end: a custom
/// <see cref="IActivityTagEnricher"/> registered via <c>setup.Instrumentation.AddEnricher(...)</c> inside
/// <c>AddHeadlessMessaging</c> must reach the <see cref="MessagingTelemetry"/> singleton actually resolved
/// from the container — not just the standalone <see cref="MessagingInstrumentationOptions"/> composition
/// already covered by <c>MessagingInstrumentationTests</c>.
/// </summary>
public sealed class MessagingInstrumentationDiTests : TestBase
{
    private static readonly BrokerAddress _Broker = new("Test", "localhost");

    [Fact]
    public void should_reach_di_resolved_messaging_telemetry_when_enricher_registered_via_setup_instrumentation()
    {
        // given — the minimal valid AddHeadlessMessaging registration: no message/consumer/transport
        // config needed since we only resolve the MessagingTelemetry singleton and call it directly.
        var services = new ServiceCollection();
        services.AddLogging();
        var enricher = new StubEnricher("app.custom", "from-di");

        services.AddHeadlessMessaging(setup => setup.Instrumentation.AddEnricher(enricher));

        using var provider = services.BuildServiceProvider();
        var telemetry = provider.GetRequiredService<MessagingTelemetry>();

        using var listener = _StartActivityListener();
        var message = _CreateTransportMessage("orders.placed");

        // when
        var publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);

        // then — the enricher captured at setup time reached the DI-resolved MessagingTelemetry instance.
        publish.Should().NotBeNull();
        publish!.GetTagItem("app.custom").Should().Be("from-di");
        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 120);
    }

    [Fact]
    public void should_not_tag_when_no_custom_enricher_registered()
    {
        // given — control case: without AddEnricher, the DI-resolved MessagingTelemetry still resolves
        // (built-in enrichers only) and carries no app.custom tag.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(_ => { });

        using var provider = services.BuildServiceProvider();
        var telemetry = provider.GetRequiredService<MessagingTelemetry>();

        using var listener = _StartActivityListener();
        var message = _CreateTransportMessage("orders.placed");

        // when
        var publish = telemetry.PublishStart(message, IntentType.Bus, _Broker, 100);

        // then
        publish.Should().NotBeNull();
        publish!.GetTagItem("app.custom").Should().BeNull();
        MessagingTelemetry.PublishStop(publish, message, _Broker, 100, 120);
    }

    private static ActivityListener _StartActivityListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source =>
                string.Equals(source.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
        };

        ActivitySource.AddActivityListener(listener);

        return listener;
    }

    private static TransportMessage _CreateTransportMessage(string name)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString(),
            [Headers.MessageName] = name,
            [Headers.Group] = "workers",
        };

        return new TransportMessage(headers, new byte[] { 1, 2, 3 });
    }

    private sealed class StubEnricher(string key, string value) : IActivityTagEnricher
    {
        public void Enrich(Activity activity, in MessagingEnrichmentContext context)
        {
            activity.SetTag(key, value);
        }
    }
}
