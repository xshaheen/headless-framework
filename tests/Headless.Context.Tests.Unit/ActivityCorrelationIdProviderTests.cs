// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;

namespace Tests;

public sealed class ActivityCorrelationIdProviderTests
{
    private readonly ActivityCorrelationIdProvider _provider = new();

    [Fact]
    public void should_return_trace_id_when_activity_is_active()
    {
        // given
        using var activity = _Start("request", ActivityIdFormat.W3C);

        // then
        _provider.CorrelationId.Should().Be(activity.TraceId.ToHexString()).And.MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void should_return_same_id_for_every_span_in_one_trace()
    {
        // given
        using var parent = _Start("request", ActivityIdFormat.W3C);
        var parentId = _provider.CorrelationId;

        // when
        using var child = _Start("query", ActivityIdFormat.W3C);

        // then the span ids differ, the correlation id does not
        child.Id.Should().NotBe(parent.Id);
        _provider.CorrelationId.Should().Be(parentId);
    }

    [Fact]
    public void should_return_null_when_no_activity_is_active()
    {
        // given
        Activity.Current = null;

        // then
        _provider.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void should_return_null_when_activity_uses_hierarchical_ids()
    {
        // given
        using var activity = _Start("request", ActivityIdFormat.Hierarchical);

        // then
        _provider.CorrelationId.Should().BeNull();
    }

    [Fact]
    public void should_return_the_causal_scope_correlation_id_over_the_trace_id()
    {
        // given
        using var activity = _Start("job", ActivityIdFormat.W3C);
        using var scope = CausalScope.Begin("order-7", "run-1");

        // then
        _provider.CorrelationId.Should().Be("order-7");
    }

    [Fact]
    public void should_return_the_trace_id_again_once_the_causal_scope_closes()
    {
        // given
        using var activity = _Start("job", ActivityIdFormat.W3C);
        CausalScope.Begin("order-7", "run-1").Dispose();

        // then
        _provider.CorrelationId.Should().Be(activity.TraceId.ToHexString());
    }

    private static Activity _Start(string name, ActivityIdFormat format)
    {
        var activity = new Activity(name);
        activity.SetIdFormat(format);
        activity.Start();

        return activity;
    }
}
