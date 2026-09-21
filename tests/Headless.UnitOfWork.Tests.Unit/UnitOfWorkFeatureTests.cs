// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// <c>GetFeature</c> is a lookup into the host container, gated by the <see cref="IUnitOfWorkFeature" /> marker:
/// nothing is created or cached per unit, every unit sees the same singleton instance, and a scoped or transient
/// registration is refused from its recorded lifetime — with or without scope validation — rather than resolved
/// from the root.
/// </summary>
public sealed class UnitOfWorkFeatureTests : TestBase
{
    [Fact]
    public async Task should_resolve_the_registered_singleton_feature()
    {
        await using var provider = _BuildProvider(services => services.AddSingleton<ProbeFeature>());
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var first = await factory.BeginAsync(cancellationToken: AbortToken);
        await using var second = await factory.BeginAsync(cancellationToken: AbortToken);

        first.GetFeature<ProbeFeature>().Should().BeSameAs(provider.GetRequiredService<ProbeFeature>());
        second.GetFeature<ProbeFeature>().Should().BeSameAs(first.GetFeature<ProbeFeature>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task should_refuse_a_scoped_feature_whether_or_not_scopes_are_validated(bool validateScopes)
    {
        // A feature is a singleton by contract: the factory resolves from the root, so a scoped registration is
        // a captive-dependency error. Scope validation is off in production by default, so the refusal comes
        // from the recorded lifetime and names the type, before any instance is root-resolved.
        await using var provider = _BuildProvider(services => services.AddScoped<ScopedProbeFeature>(), validateScopes);
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        var act = () => unit.GetFeature<ScopedProbeFeature>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{typeof(ScopedProbeFeature).FullName}' is registered as Scoped*must be a singleton*");
    }

    [Fact]
    public async Task should_refuse_a_transient_feature()
    {
        // Scope validation never catches a transient: the root would hand out a fresh, never-disposed instance
        // per call, and two handles would no longer see the same feature.
        await using var provider = _BuildProvider(
            services => services.AddTransient<ScopedProbeFeature>(),
            validateScopes: false
        );
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        var act = () => unit.GetFeature<ScopedProbeFeature>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*is registered as Transient*must be a singleton*");
    }

    [Fact]
    public async Task should_resolve_the_last_registration_when_a_singleton_replaces_a_scoped_one()
    {
        // GetService returns the last registration of a type, so that is the lifetime the guard judges.
        await using var provider = _BuildProvider(
            services => services.AddScoped<ScopedProbeFeature>().AddSingleton<ScopedProbeFeature>(),
            validateScopes: false
        );
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        unit.GetFeature<ScopedProbeFeature>().Should().BeSameAs(provider.GetRequiredService<ScopedProbeFeature>());
    }

    [Fact]
    public async Task should_return_null_when_no_feature_of_that_type_is_registered()
    {
        await using var provider = _BuildProvider(static _ => { });
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        unit.GetFeature<ProbeFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_return_null_when_the_factory_was_constructed_outside_dependency_injection()
    {
        // Tests construct the factory directly; with no container to resolve from, no feature exists.
        var factory = new UnitOfWorkFactory();
        await using var unit = await factory.BeginAsync(cancellationToken: AbortToken);

        unit.GetFeature<ProbeFeature>().Should().BeNull();
    }

    [Fact]
    public async Task should_throw_object_disposed_when_resolving_on_a_disposed_handle()
    {
        await using var provider = _BuildProvider(services => services.AddSingleton<ProbeFeature>());
        var factory = provider.GetRequiredService<IUnitOfWorkFactory>();
        var unit = await factory.BeginAsync(cancellationToken: AbortToken);
        await unit.DisposeAsync();

        var act = () => unit.GetFeature<ProbeFeature>();

        act.Should().Throw<ObjectDisposedException>();
    }

    private static ServiceProvider _BuildProvider(Action<IServiceCollection> configure, bool validateScopes = true)
    {
        var services = new ServiceCollection();
        services.AddUnitOfWork();
        configure(services);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validateScopes });
    }
}

/// <summary>A feature a bridge package would register; the marker is what lets a unit hand it out.</summary>
internal sealed class ProbeFeature : IUnitOfWorkFeature;

internal sealed class ScopedProbeFeature : IUnitOfWorkFeature, IDisposable
{
    public bool IsDisposed { get; private set; }

    public void Touch() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    public void Dispose() => IsDisposed = true;
}
