// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Messaging.OpenTelemetry;
using Headless.Messaging.OpenTelemetry.Internal;
using Headless.Testing.Tests;

namespace Tests;

public sealed class TenantIdTagEnricherTests : TestBase
{
    private readonly TenantIdTagEnricher _enricher = new();

    [Fact]
    public async Task should_set_tenant_id_tag_when_tenant_id_is_present()
    {
        // given
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            Kind = MessagingEventKind.Publish,
            TenantId = "tenant-123",
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem("headless.messaging.tenant_id").Should().Be("tenant-123");
    }

    [Fact]
    public async Task should_not_set_tag_when_tenant_id_is_null()
    {
        // given
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            Kind = MessagingEventKind.Publish,
            TenantId = null,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem("headless.messaging.tenant_id").Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_not_set_tag_when_tenant_id_is_empty_or_whitespace(string tenantId)
    {
        // given
        using var activity = _CreateActivity();
        var context = new MessagingEnrichmentContext
        {
            Kind = MessagingEventKind.Publish,
            TenantId = tenantId,
            Headers = new Dictionary<string, string?>(StringComparer.Ordinal),
        };

        // when
        await _enricher.Enrich(activity, context, AbortToken);

        // then
        activity.GetTagItem("headless.messaging.tenant_id").Should().BeNull();
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
