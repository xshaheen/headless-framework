// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tests.Modules.Fixtures;

namespace Tests.Modules;

public sealed class ContributionOverrideTests : TestBase
{
    [Fact]
    public async Task should_remove_the_nav_entry_from_the_merged_manifest_when_the_host_disables_it()
    {
        await using var provider = _Build(dashboard => dashboard.Contributions(c => c.Disable("diag.nav.runs")));

        _NavIds(provider).Should().Equal("diag.nav.overview", "diag.nav.settings");
    }

    [Fact]
    public async Task should_drop_the_server_command_and_related_group_when_the_host_disables_them()
    {
        await using var provider = _Build(dashboard =>
            dashboard.Contributions(c => c.Disable("diag.requeue", "audit.run-related"))
        );

        var manifest = provider.GetRequiredService<DashboardManifestProvider>().Manifest;
        manifest
            .Modules.Single(module => string.Equals(module.Id, "diag", StringComparison.Ordinal))
            .ServerCommands.Should()
            .BeEmpty();
        manifest.IsDisabled("diag.requeue").Should().BeTrue();
        manifest.RelatedGroupIds.Should().Equal("diag.run-related");
    }

    [Fact]
    public async Task should_reorder_only_the_named_contributions_when_the_host_orders_them()
    {
        await using var provider = _Build(dashboard =>
            dashboard.Contributions(c =>
                c.Order("diag.nav.settings", "diag.nav.runs").Order("audit.run-related", "diag.run-related")
            )
        );

        _NavIds(provider).Should().Equal("diag.nav.settings", "diag.nav.overview", "diag.nav.runs");
        provider
            .GetRequiredService<DashboardManifestProvider>()
            .Manifest.RelatedGroupIds.Should()
            .Equal("audit.run-related", "diag.run-related");
    }

    [Fact]
    public async Task should_fail_startup_validation_naming_the_id_when_an_override_matches_no_contribution()
    {
        await using var provider = _Build(dashboard => dashboard.Contributions(c => c.Disable("diag.nav.typo")));

        var act = () => _RunStartupValidatorsAsync(provider);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'diag.nav.typo'*");
    }

    [Fact]
    public async Task should_pass_startup_validation_when_every_override_matches_a_contribution()
    {
        await using var provider = _Build(dashboard =>
            dashboard.Contributions(c => c.Disable("diag.nav.runs").Order("audit.run-related", "diag.run-related"))
        );

        await _RunStartupValidatorsAsync(provider);
    }

    [Fact]
    public async Task should_apply_overrides_bound_from_configuration_as_the_code_form_does()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Headless:Dashboard:Contributions:Disable:0"] = "diag.nav.overview",
                    ["Headless:Dashboard:Contributions:Order:0:0"] = "diag.nav.settings",
                    ["Headless:Dashboard:Contributions:Order:0:1"] = "diag.nav.runs",
                }
            )
            .Build();

        await using var bound = _Build(dashboard =>
            dashboard.Contributions(configuration.GetSection("Headless:Dashboard:Contributions"))
        );
        await using var code = _Build(dashboard =>
            dashboard.Contributions(c => c.Disable("diag.nav.overview").Order("diag.nav.settings", "diag.nav.runs"))
        );

        _NavIds(bound).Should().Equal("diag.nav.settings", "diag.nav.runs");
        _NavIds(code).Should().Equal(_NavIds(bound));
        await _RunStartupValidatorsAsync(bound);
    }

    private static ServiceProvider _Build(Action<HeadlessDashboardSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDashboard(dashboard =>
        {
            dashboard
                .WithNoAuth()
                .AddModule<DiagModule, DiagModuleOptions>("diag")
                .AddModule<AuditModule, AuditModuleOptions>("audit");
            configure(dashboard);
        });

        return services.BuildServiceProvider();
    }

    private static IEnumerable<string> _NavIds(IServiceProvider provider)
    {
        var manifest = provider.GetRequiredService<DashboardManifestProvider>().Manifest;

        return manifest
            .Modules.Single(module => string.Equals(module.Id, "diag", StringComparison.Ordinal))
            .Manifest!.Nav.Select(entry => entry.Id);
    }

    private static async Task _RunStartupValidatorsAsync(IServiceProvider provider)
    {
        foreach (var validator in provider.GetServices<IHeadlessStartupValidator>())
        {
            await validator.ValidateAsync(AbortToken);
        }
    }
}
