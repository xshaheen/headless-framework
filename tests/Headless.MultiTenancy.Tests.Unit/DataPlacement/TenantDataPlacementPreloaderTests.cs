// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.DataPlacement;

public sealed class TenantDataPlacementPreloaderTests : TestBase
{
    private static readonly TenantDataPlacement _Acme = new("tenant_acme", connectionString: null);

    [Fact]
    public async Task should_preload_the_tenant_placement_for_next_only()
    {
        // given
        var resolver = new CountingResolver(_Acme);
        await using var services = _Services(resolver);
        var sut = _Create(routed: true);
        (bool Found, TenantDataPlacement? Placement) observed = default;

        // when
        await sut.RunAsync(
            services,
            "acme",
            () =>
            {
                observed.Found = TenantDataPlacementPreloader.TryGetPreloaded("acme", out observed.Placement);
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then
        observed.Found.Should().BeTrue();
        observed.Placement.Should().BeSameAs(_Acme);
        TenantDataPlacementPreloader.TryGetPreloaded("acme", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_not_report_a_placement_preloaded_for_another_tenant()
    {
        await using var services = _Services(new CountingResolver(_Acme));
        var found = true;

        await _Create(routed: true)
            .RunAsync(
                services,
                "acme",
                () =>
                {
                    found = TenantDataPlacementPreloader.TryGetPreloaded("globex", out _);
                    return Task.CompletedTask;
                },
                AbortToken
            );

        found.Should().BeFalse();
    }

    [Fact]
    public async Task should_report_a_tenant_without_placement_as_preloaded_with_none()
    {
        await using var services = _Services(new CountingResolver(placement: null));
        (bool Found, TenantDataPlacement? Placement) observed = default;

        await _Create(routed: true)
            .RunAsync(
                services,
                "acme",
                () =>
                {
                    observed.Found = TenantDataPlacementPreloader.TryGetPreloaded("acme", out observed.Placement);
                    return Task.CompletedTask;
                },
                AbortToken
            );

        observed.Found.Should().BeTrue();
        observed.Placement.Should().BeNull();
    }

    [Fact]
    public async Task should_defer_a_resolver_fault_until_the_placement_is_read()
    {
        // given
        var fault = new InvalidOperationException("placement store down");
        await using var services = _Services(new CountingResolver(fault));
        var nextRan = false;
        Exception? observed = null;

        // when
        await _Create(routed: true)
            .RunAsync(
                services,
                "acme",
                () =>
                {
                    nextRan = true;
                    observed = Record.Exception(() => TenantDataPlacementPreloader.TryGetPreloaded("acme", out _));
                    return Task.CompletedTask;
                },
                AbortToken
            );

        // then
        nextRan.Should().BeTrue();
        observed.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task should_propagate_cancellation_from_the_resolver()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await using var services = _Services(new CountingResolver(new OperationCanceledException(cancelled.Token)));
        var nextRan = false;

        var act = () =>
            _Create(routed: true)
                .RunAsync(
                    services,
                    "acme",
                    () =>
                    {
                        nextRan = true;
                        return Task.CompletedTask;
                    },
                    cancelled.Token
                );

        await act.Should().ThrowAsync<OperationCanceledException>();
        nextRan.Should().BeFalse();
    }

    [Fact]
    public async Task should_defer_a_resolver_timeout_that_is_not_the_pipeline_cancellation()
    {
        // given: an HttpClient-style timeout, whose token is not the pipeline's
        var timeout = new TaskCanceledException("resolver timed out");
        await using var services = _Services(new CountingResolver(timeout));
        Exception? observed = null;

        // when
        await _Create(routed: true)
            .RunAsync(
                services,
                "acme",
                () =>
                {
                    observed = Record.Exception(() => TenantDataPlacementPreloader.TryGetPreloaded("acme", out _));
                    return Task.CompletedTask;
                },
                AbortToken
            );

        // then
        observed.Should().BeSameAs(timeout);
    }

    [Theory]
    [InlineData(false, "acme")]
    [InlineData(true, null)]
    [InlineData(true, " ")]
    public async Task should_pass_through_without_routing_or_tenant(bool routed, string? tenantId)
    {
        var resolver = new CountingResolver(_Acme);
        await using var services = _Services(resolver);
        var nextRan = false;

        await _Create(routed)
            .RunAsync(
                services,
                tenantId,
                () =>
                {
                    nextRan = true;
                    return Task.CompletedTask;
                },
                AbortToken
            );

        nextRan.Should().BeTrue();
        resolver.Calls.Should().Be(0);
    }

    [Fact]
    public async Task should_resolve_once_when_nested_entry_points_preload_the_same_tenant()
    {
        var resolver = new CountingResolver(_Acme);
        await using var services = _Services(resolver);
        var sut = _Create(routed: true);

        await sut.RunAsync(
            services,
            "acme",
            () => sut.RunAsync(services, "acme", () => Task.CompletedTask, AbortToken),
            AbortToken
        );

        resolver.Calls.Should().Be(1);
    }

    private static TenantDataPlacementPreloader _Create(bool routed) =>
        new(routed ? [new TenantDataRoutedContextRegistration(typeof(object))] : []);

    private static ServiceProvider _Services(ITenantDataPlacementResolver resolver) =>
        new ServiceCollection().AddSingleton(resolver).BuildServiceProvider();

    private sealed class CountingResolver : ITenantDataPlacementResolver
    {
        private readonly TenantDataPlacement? _placement;
        private readonly Exception? _fault;

        public CountingResolver(TenantDataPlacement? placement) => _placement = placement;

        public CountingResolver(Exception fault) => _fault = fault;

        public int Calls { get; private set; }

        public Task<TenantDataPlacement?> ResolveAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Calls++;

            return _fault is null ? Task.FromResult(_placement) : Task.FromException<TenantDataPlacement?>(_fault);
        }
    }
}
