// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.OpenTelemetry;
using Headless.Messaging.OpenTelemetry.Internal;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RetryCountTagEnricherTests : TestBase
{
    private readonly RetryCountTagEnricher _enricher = new();

    [Fact]
    public async Task should_set_retry_count_tag_when_retry_count_is_positive()
    {
        // given
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            Kind = MessagingEventKind.SubscriberInvoke,
            RetryCount = 3,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem(MessagingTags.RetryCount).Should().Be(3);
    }

    [Fact]
    public async Task should_not_set_retry_count_tag_when_retry_count_is_zero()
    {
        // given
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            Kind = MessagingEventKind.SubscriberInvoke,
            RetryCount = 0,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem(MessagingTags.RetryCount).Should().BeNull();
    }

    [Fact]
    public async Task should_not_set_retry_count_tag_when_retry_count_is_negative()
    {
        // given - defensive against future signed-int regressions
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            Kind = MessagingEventKind.SubscriberInvoke,
            RetryCount = -1,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem(MessagingTags.RetryCount).Should().BeNull();
    }

    private static Activity _CreateActivity()
    {
        using var source = new ActivitySource("test");
#pragma warning disable CA2000  // Dispose objects before losing scope
        // ReSharper disable once ExplicitCallerInfoArgument
        return source.StartActivity("test") ?? new Activity("test").Start();
#pragma warning restore CA2000
    }
}
