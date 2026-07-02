// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging;
using Headless.Messaging.OpenTelemetry;
using Headless.Messaging.OpenTelemetry.Internal;
using Headless.Testing.Tests;

namespace Tests;

public sealed class IntentTagEnricherTests : TestBase
{
    private readonly IntentTagEnricher _enricher = new();

    [Theory]
    [InlineData(IntentType.Bus, "bus", "topic")]
    [InlineData(IntentType.Queue, "queue", "queue")]
    public async Task should_set_intent_and_destination_kind_tags(
        IntentType intentType,
        string expectedIntent,
        string expectedDestinationKind
    )
    {
        // given
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            IntentType = intentType,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem(MessagingTags.Intent).Should().Be(expectedIntent);
        activity.GetTagItem(MessagingTags.DestinationKind).Should().Be(expectedDestinationKind);
    }

    [Fact]
    public async Task should_not_set_tags_for_unknown_intent()
    {
        // given
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            IntentType = (IntentType)99,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem(MessagingTags.Intent).Should().BeNull();
        activity.GetTagItem(MessagingTags.DestinationKind).Should().BeNull();
    }

    private static Activity _CreateActivity()
    {
        using var source = new ActivitySource("test");
#pragma warning disable CA2000 // Dispose objects before losing scope
        // ReSharper disable once ExplicitCallerInfoArgument
        return source.StartActivity("test") ?? new Activity("test").Start();
#pragma warning restore CA2000
    }
}
