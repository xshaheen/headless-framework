// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Context;

namespace Tests;

public sealed class CausalScopeTests
{
    [Fact]
    public void should_return_null_when_no_scope_is_open()
    {
        CausalScope.Current.Should().BeNull();
    }

    [Fact]
    public void should_expose_the_ids_while_the_scope_is_open()
    {
        using (CausalScope.Begin("order-7", "run-1"))
        {
            CausalScope.Current.Should().Be(new CausalContext("order-7", "run-1"));
        }

        CausalScope.Current.Should().BeNull();
    }

    [Fact]
    public void should_restore_the_outer_scope_when_a_nested_scope_closes()
    {
        using var outer = CausalScope.Begin("order-7", "run-1");

        var inner = CausalScope.Begin("order-8", "delivery-2");
        CausalScope.Current.Should().Be(new CausalContext("order-8", "delivery-2"));
        inner.Dispose();

        CausalScope.Current.Should().Be(new CausalContext("order-7", "run-1"));
    }

    [Fact]
    public async Task should_restore_the_outer_scope_when_the_inner_one_closes_after_an_await()
    {
        using var outer = CausalScope.Begin("order-7", "run-1");

        await _InnerAsync();

        CausalScope.Current.Should().Be(new CausalContext("order-7", "run-1"));

        static async Task _InnerAsync()
        {
            using var inner = CausalScope.Begin("order-8", "delivery-2");
            // Yield so the inner scope closes on a thread-pool continuation, not the thread that opened it.
            await Task.Yield();
            CausalScope.Current.Should().Be(new CausalContext("order-8", "delivery-2"));
        }
    }

    [Fact]
    public async Task should_keep_concurrent_scopes_apart()
    {
        using var bothOpened = new Barrier(2);

        var observed = await Task.WhenAll(_RunAsync("order-a"), _RunAsync("order-b"));

        observed.Should().Equal("order-a", "order-b");

        Task<string?> _RunAsync(string correlationId) =>
            Task.Run(() =>
            {
                using var scope = CausalScope.Begin(correlationId, Guid.NewGuid().ToString("D"));
                // Both scopes are open at the same moment before either reads, so a shared slot would be visible.
                bothOpened.SignalAndWait();
                return CausalScope.Current?.CorrelationId;
            });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void should_hide_the_outer_scope_when_the_correlation_id_is_blank(string? correlationId)
    {
        using var outer = CausalScope.Begin("order-7", "run-1");

        using (CausalScope.Begin(correlationId, "delivery-2"))
        {
            // The inner work is not the outer scope's, so it must not borrow that correlation.
            CausalScope.Current.Should().BeNull();
        }

        CausalScope.Current.Should().Be(new CausalContext("order-7", "run-1"));
    }

    [Fact]
    public void should_hide_the_outer_scope_when_the_correlation_id_is_longer_than_the_limit()
    {
        using var outer = CausalScope.Begin("order-7", "run-1");

        using (CausalScope.Begin(new string('c', CausalScope.IdMaxLength + 1), "delivery-2"))
        {
            CausalScope.Current.Should().BeNull();
        }

        CausalScope.Current.Should().Be(new CausalContext("order-7", "run-1"));
    }

    [Fact]
    public void should_refuse_to_close_a_scope_out_of_order()
    {
        var outer = CausalScope.Begin("order-7", "run-1");
        var inner = CausalScope.Begin("order-8", "delivery-2");

        var closeOuterFirst = outer.Dispose;

        closeOuterFirst.Should().Throw<InvalidOperationException>();
        CausalScope.Current.Should().Be(new CausalContext("order-8", "delivery-2"));
        inner.Dispose();
        outer.Dispose();
        CausalScope.Current.Should().BeNull();
    }

    [Fact]
    public void should_ignore_a_second_dispose()
    {
        using var outer = CausalScope.Begin("order-7", "run-1");
        var inner = CausalScope.Begin("order-8", "delivery-2");

        inner.Dispose();
        inner.Dispose();

        CausalScope.Current.Should().Be(new CausalContext("order-7", "run-1"));
    }

    [Fact]
    public void should_open_a_scope_when_the_correlation_id_is_exactly_the_limit()
    {
        var correlationId = new string('c', CausalScope.IdMaxLength);

        using (CausalScope.Begin(correlationId, "run-1"))
        {
            CausalScope.Current!.CorrelationId.Should().Be(correlationId);
        }
    }

    [Fact]
    public void should_refuse_a_causation_id_longer_than_the_limit()
    {
        var begin = () => CausalScope.Begin("order-7", new string('c', CausalScope.IdMaxLength + 1));

        begin.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_expose_the_causation_trace_id_only_while_its_scope_is_open()
    {
        var traceId = ActivityTraceId.CreateFromString("4bf92f3577b34da6a3ce929d0e0e4736");

        using (CausalScope.Begin("order-7", "run-1", traceId))
        {
            CausalScope.CausationTraceId.Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        }

        CausalScope.CausationTraceId.Should().BeNull();
    }

    [Fact]
    public void should_expose_no_causation_trace_id_when_the_work_stored_none()
    {
        using (CausalScope.Begin("order-7", "run-1"))
        {
            CausalScope.CausationTraceId.Should().BeNull();
        }
    }

    [Fact]
    public void should_keep_the_causation_trace_id_when_the_scope_has_no_usable_correlation()
    {
        // A foreign correlation too long to store still came from a traced cause.
        var traceId = ActivityTraceId.CreateRandom();

        using (CausalScope.Begin(new string('x', CausalScope.IdMaxLength + 1), "delivery-2", traceId))
        {
            CausalScope.Current.Should().BeNull();
            CausalScope.CausationTraceId.Should().Be(traceId.ToHexString());
        }
    }

    [Fact]
    public void should_not_inherit_the_outer_causation_trace_id_in_a_nested_scope_and_restore_it_after()
    {
        var outerTraceId = ActivityTraceId.CreateRandom();
        using var outer = CausalScope.Begin("order-7", "run-1", outerTraceId);

        var inner = CausalScope.Begin("order-8", "delivery-2");
        CausalScope.CausationTraceId.Should().BeNull("the inner work has its own cause");
        inner.Dispose();

        CausalScope.CausationTraceId.Should().Be(outerTraceId.ToHexString());
    }
}
