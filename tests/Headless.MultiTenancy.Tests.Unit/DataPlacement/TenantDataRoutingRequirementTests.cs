// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.DataPlacement;

public sealed class TenantDataRoutingRequirementTests : TestBase
{
    [Fact]
    public async Task should_name_each_feature_that_runs_over_a_routed_context()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(new TenantDataRoutedContextRegistration(typeof(RoutedContext)));
        services.RequireUnroutedTenantDataContext(typeof(RoutedContext), "Jobs UseApplicationDbContext");
        services.RequireUnroutedTenantDataContext(typeof(RoutedContext), "Messaging UseEntityFramework");
        services.RequireUnroutedTenantDataContext(typeof(UnroutedContext), "Settings UseEntityFramework");
        await using var provider = services.BuildServiceProvider();

        // when
        var diagnostics = provider
            .GetServices<IHeadlessTenancyValidator>()
            .SelectMany(validator => validator.Validate(new(provider, new TenantPostureManifest())))
            .ToList();

        // then
        diagnostics.Should().HaveCount(2);
        diagnostics.Should().OnlyContain(d => d.Severity == HeadlessTenancyDiagnosticSeverity.Error);
        diagnostics.Select(d => d.Message).Should().ContainMatch("Jobs UseApplicationDbContext*RoutedContext*");
        diagnostics.Select(d => d.Message).Should().ContainMatch("Messaging UseEntityFramework*RoutedContext*");
    }

    [Fact]
    public void should_reject_a_blank_owner()
    {
        var act = () => new ServiceCollection().RequireUnroutedTenantDataContext(typeof(RoutedContext), " ");

        act.Should().Throw<ArgumentException>();
    }

    private static class RoutedContext;

    private static class UnroutedContext;
}
