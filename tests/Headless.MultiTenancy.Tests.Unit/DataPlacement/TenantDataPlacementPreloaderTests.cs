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

    [Fact]
    public async Task should_preload_one_placement_per_routed_data_store()
    {
        // given: two routed contexts in different data stores, a resolver that places them differently
        var resolver = new StoreAwareResolver();
        var sut = new TenantDataPlacementPreloader([
            new TenantDataRoutedContextRegistration(typeof(object)),
            new TenantDataRoutedContextRegistration(typeof(string), "billing"),
            new TenantDataRoutedContextRegistration(typeof(int), "billing"),
        ]);
        await using var services = _Services(resolver);
        (bool Found, TenantDataPlacement? Placement) primary = default;
        (bool Found, TenantDataPlacement? Placement) billing = default;
        var unrouted = true;

        // when
        await sut.RunAsync(
            services,
            "acme",
            () =>
            {
                primary.Found = TenantDataPlacementPreloader.TryGetPreloaded("acme", out primary.Placement);
                billing.Found = TenantDataPlacementPreloader.TryGetPreloaded(
                    new TenantDataPlacementRequest("acme", "billing"),
                    out billing.Placement
                );
                unrouted = TenantDataPlacementPreloader.TryGetPreloaded(
                    new TenantDataPlacementRequest("acme", "reports"),
                    out _
                );

                return Task.CompletedTask;
            },
            AbortToken
        );

        // then: one resolution per distinct store, none for a store no context is routed under
        resolver.Requests.Should().BeEquivalentTo([new TenantDataPlacementRequest("acme"), new("acme", "billing")]);
        primary.Found.Should().BeTrue();
        primary.Placement!.Schema.Should().Be("acme_default");
        billing.Found.Should().BeTrue();
        billing.Placement!.Schema.Should().Be("acme_billing");
        unrouted.Should().BeFalse();
    }

    [Fact]
    public async Task should_hold_a_fault_per_data_store()
    {
        // given: the billing store faults, the default store resolves
        var resolver = new StoreAwareResolver { FaultingStore = "billing" };
        var sut = new TenantDataPlacementPreloader([
            new TenantDataRoutedContextRegistration(typeof(object)),
            new TenantDataRoutedContextRegistration(typeof(string), "billing"),
        ]);
        await using var services = _Services(resolver);
        Exception? billingFault = null;
        var primaryFound = false;

        // when
        await sut.RunAsync(
            services,
            "acme",
            () =>
            {
                primaryFound = TenantDataPlacementPreloader.TryGetPreloaded("acme", out _);
                billingFault = Record.Exception(() =>
                    TenantDataPlacementPreloader.TryGetPreloaded(
                        new TenantDataPlacementRequest("acme", "billing"),
                        out _
                    )
                );

                return Task.CompletedTask;
            },
            AbortToken
        );

        // then
        primaryFound.Should().BeTrue();
        billingFault.Should().BeOfType<TimeoutException>();
    }

    private static TenantDataPlacementPreloader _Create(bool routed) =>
        new(routed ? [new TenantDataRoutedContextRegistration(typeof(object))] : []);

    private static ServiceProvider _Services(ITenantDataPlacementResolver resolver) =>
        new ServiceCollection().AddSingleton(resolver).BuildServiceProvider();

    private sealed class StoreAwareResolver : ITenantDataPlacementResolver
    {
        public List<TenantDataPlacementRequest> Requests { get; } = [];

        public string? FaultingStore { get; init; }

        public Task<TenantDataPlacement?> ResolveAsync(
            TenantDataPlacementRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);

            return string.Equals(request.DataStore, FaultingStore, StringComparison.Ordinal)
                ? Task.FromException<TenantDataPlacement?>(new TimeoutException("placement store down"))
                : Task.FromResult<TenantDataPlacement?>(
                    new TenantDataPlacement($"{request.TenantId}_{request.DataStore}", connectionString: null)
                );
        }
    }

    private sealed class CountingResolver : ITenantDataPlacementResolver
    {
        private readonly TenantDataPlacement? _placement;
        private readonly Exception? _fault;

        public CountingResolver(TenantDataPlacement? placement) => _placement = placement;

        public CountingResolver(Exception fault) => _fault = fault;

        public int Calls { get; private set; }

        public Task<TenantDataPlacement?> ResolveAsync(
            TenantDataPlacementRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Calls++;

            return _fault is null ? Task.FromResult(_placement) : Task.FromException<TenantDataPlacement?>(_fault);
        }
    }
}
