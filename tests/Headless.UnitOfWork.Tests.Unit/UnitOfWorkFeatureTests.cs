// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class UnitOfWorkFeatureTests : TestBase
{
    [Fact]
    public async Task should_return_the_same_feature_instance_for_repeated_resolutions_on_one_unit()
    {
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        var first = unit.GetFeature<ProbeFeature>();
        var second = unit.GetFeature<ProbeFeature>();

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
        provider.CreateCount.Should().Be(1);
    }

    [Fact]
    public async Task should_cache_one_feature_on_the_root_when_a_child_view_resolves_it_first()
    {
        // The hazard: a child-first construction that captured the child view would keep it after the child
        // completed, so the cached feature would hold a view that can no longer carry work.
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var root = await manager.BeginAsync(cancellationToken: AbortToken);
        var child = await manager.BeginAsync(cancellationToken: AbortToken);

        var fromChild = child.GetFeature<ProbeFeature>();
        await child.CompleteAsync(AbortToken);
        var fromRoot = root.GetFeature<ProbeFeature>();

        fromRoot.Should().BeSameAs(fromChild);
        provider.CreateCount.Should().Be(1);
        fromChild!.Unit.Should().NotBeSameAs(child);
        fromChild.Unit.Should().BeSameAs(root);

        var stillUsable = fromChild.Unit.ThrowIfUnusable;
        stillUsable.Should().NotThrow();
    }

    [Fact]
    public async Task should_throw_when_a_feature_is_resolved_on_a_completed_unit()
    {
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        await unit.CompleteAsync(AbortToken);

        var resolve = () => unit.GetFeature<ProbeFeature>();

        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*registrations are accepted only while it is Active.*");
        provider.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task should_throw_when_a_feature_is_resolved_on_a_rolled_back_unit()
    {
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        await unit.RollbackAsync();

        var resolve = () => unit.GetFeature<ProbeFeature>();

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task should_return_null_when_no_provider_is_registered_for_the_feature()
    {
        await using var manager = new UnitOfWorkManager();
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        unit.GetFeature<ProbeFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_for_an_unclaimed_feature_when_another_provider_is_registered()
    {
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        unit.GetFeature<OtherFeature>().Should().BeNull();
        provider.CreateCount.Should().Be(0);
    }

    [Fact]
    public async Task should_dispose_the_feature_after_the_unit_completes()
    {
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);
        var feature = unit.GetFeature<ProbeFeature>();

        await unit.CompleteAsync(AbortToken);

        feature!.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task should_dispose_the_feature_after_the_unit_rolls_back()
    {
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);
        var feature = unit.GetFeature<ProbeFeature>();

        await unit.RollbackAsync();

        feature!.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task should_report_a_completed_child_view_as_unusable_while_the_root_stays_usable()
    {
        await using var manager = new UnitOfWorkManager();
        await using var root = await manager.BeginAsync(cancellationToken: AbortToken);
        var child = await manager.BeginAsync(cancellationToken: AbortToken);

        await child.CompleteAsync(AbortToken);

        // State forwards to the root, so it cannot answer this: the child is terminal, the root is not.
        child.State.Should().Be(UnitOfWorkState.Active);

        var childUsable = child.ThrowIfUnusable;
        childUsable.Should().Throw<InvalidOperationException>();

        var rootUsable = root.ThrowIfUnusable;
        rootUsable.Should().NotThrow();
    }

    [Fact]
    public async Task should_throw_when_a_feature_is_resolved_on_a_completed_child_view()
    {
        var provider = new ProbeFeatureProvider();
        await using var manager = new UnitOfWorkManager(logger: null, [provider]);
        await using var root = await manager.BeginAsync(cancellationToken: AbortToken);
        var child = await manager.BeginAsync(cancellationToken: AbortToken);

        await child.CompleteAsync(AbortToken);

        var resolve = () => child.GetFeature<ProbeFeature>();

        resolve.Should().Throw<InvalidOperationException>();
        provider.CreateCount.Should().Be(0);
        root.GetFeature<ProbeFeature>().Should().NotBeNull();
    }

    [Fact]
    public async Task should_report_a_disposed_handle_as_unusable()
    {
        await using var manager = new UnitOfWorkManager();
        var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        await unit.DisposeAsync();

        var usable = unit.ThrowIfUnusable;
        usable.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void should_throw_when_two_providers_claim_the_same_feature()
    {
        var construct = () =>
            new UnitOfWorkManager(logger: null, [new ProbeFeatureProvider(), new ProbeFeatureProvider()]);

        construct.Should().Throw<InvalidOperationException>().WithMessage("*ProbeFeature*");
    }

    [Fact]
    public async Task should_resolve_a_registered_feature_through_dependency_injection()
    {
        var services = new ServiceCollection();

        services.AddUnitOfWorkFeature<ProbeFeatureProvider>();
        services.AddUnitOfWorkFeature<ProbeFeatureProvider>();

        services.Count(d => d.ServiceType == typeof(IUnitOfWorkFeatureProvider)).Should().Be(1);

        await using var root = services.BuildServiceProvider();
        using var scope = root.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        var feature = unit.GetFeature<ProbeFeature>();

        feature.Should().NotBeNull();
        feature!.Unit.Should().BeSameAs(unit);
    }
}

/// <summary>A capability a bridge package would attach; records the unit its factory was handed.</summary>
internal sealed class ProbeFeature(IUnitOfWork unit) : IDisposable
{
    public IUnitOfWork Unit { get; } = unit;

    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

/// <summary>A feature type nothing registers a provider for.</summary>
internal sealed class OtherFeature;

internal sealed class ProbeFeatureProvider : IUnitOfWorkFeatureProvider
{
    public int CreateCount { get; private set; }

    public Type FeatureType => typeof(ProbeFeature);

    public object Create(IUnitOfWork unitOfWork)
    {
        CreateCount++;

        return new ProbeFeature(unitOfWork);
    }
}
