// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// <c>GetFeature</c> is a lookup into the scope that owns the unit's manager, gated by the
/// <see cref="IUnitOfWorkFeature" /> marker: nothing is created or cached per unit, every handle over a unit sees the
/// same instance, and a completed nested view refuses registrations while still resolving features.
/// </summary>
public sealed class UnitOfWorkFeatureTests : TestBase
{
    [Fact]
    public async Task should_resolve_the_registered_feature_from_the_units_scope()
    {
        await using var provider = _BuildProvider(services => services.AddSingleton<ProbeFeature>());
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        var feature = unit.GetFeature<ProbeFeature>();

        feature.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<ProbeFeature>());
    }

    [Fact]
    public async Task should_resolve_the_same_instance_through_a_child_view_and_the_root()
    {
        await using var provider = _BuildProvider(services => services.AddSingleton<ProbeFeature>());
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var root = await manager.BeginAsync(cancellationToken: AbortToken);
        var child = await manager.BeginAsync(cancellationToken: AbortToken);

        var fromChild = child.GetFeature<ProbeFeature>();
        await child.CompleteAsync(AbortToken);
        var fromRoot = root.GetFeature<ProbeFeature>();

        fromRoot.Should().BeSameAs(fromChild);
    }

    [Fact]
    public async Task should_return_null_when_no_feature_of_that_type_is_registered()
    {
        await using var provider = _BuildProvider(static _ => { });
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        unit.GetFeature<ProbeFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_when_the_manager_was_constructed_outside_dependency_injection()
    {
        // Tests construct the manager directly; with no scope to resolve from, no feature exists.
        using var manager = new UnitOfWorkManager();
        await using var unit = await manager.BeginAsync(cancellationToken: AbortToken);

        unit.GetFeature<ProbeFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_still_resolve_a_feature_on_a_completed_child_view_but_refuse_its_registrations()
    {
        // Resolving is a lookup, so it stays available; attaching work is a registration, and the view that
        // completed can no longer carry one even though the root — and therefore State — is still Active.
        await using var provider = _BuildProvider(services => services.AddSingleton<ProbeFeature>());
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        await using var root = await manager.BeginAsync(cancellationToken: AbortToken);
        var child = await manager.BeginAsync(cancellationToken: AbortToken);
        await child.CompleteAsync(AbortToken);
        child.State.Should().Be(UnitOfWorkState.Active, "the view forwards the still-open root's state");

        child.GetFeature<ProbeFeature>().Should().NotBeNull();

        var onCompleted = () => child.OnCompleted(static () => ValueTask.CompletedTask);
        var onFailed = () => child.OnFailed(static _ => ValueTask.CompletedTask);
        var getOrAdd = () => child.GetOrAdd(static _ => new object());
        var getOrAddWithArg = () => child.GetOrAdd(1, static (_, _) => new object());
        const string expected = "*nested unit of work has already completed*";
        onCompleted.Should().Throw<InvalidOperationException>().WithMessage(expected);
        onFailed.Should().Throw<InvalidOperationException>().WithMessage(expected);
        getOrAdd.Should().Throw<InvalidOperationException>().WithMessage(expected);
        getOrAddWithArg.Should().Throw<InvalidOperationException>().WithMessage(expected);

        // The root is untouched by the child's refusal.
        var rootRegistration = () => root.OnCompleted(static () => ValueTask.CompletedTask);
        rootRegistration.Should().NotThrow();
    }

    [Fact]
    public async Task should_throw_object_disposed_when_resolving_on_a_disposed_handle()
    {
        await using var provider = _BuildProvider(services => services.AddSingleton<ProbeFeature>());
        using var scope = provider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();
        var unit = await manager.BeginAsync(cancellationToken: AbortToken);
        await unit.DisposeAsync();

        var act = () => unit.GetFeature<ProbeFeature>();

        act.Should().Throw<ObjectDisposedException>();
    }

    private static ServiceProvider _BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork();
        configure(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

/// <summary>A feature a bridge package would register; the marker is what lets a unit hand it out.</summary>
internal sealed class ProbeFeature : IUnitOfWorkFeature;
