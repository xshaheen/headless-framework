// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Tests.Modules.Fixtures;

namespace Tests.Modules;

public sealed class ProductSidebarTests : TestBase
{
    private static readonly DashboardModuleAccess _Viewer = new() { CanView = true, CanOperate = false };

    [Fact]
    public async Task should_merge_two_modules_of_one_product_into_one_product_with_sections_in_registration_order()
    {
        await using var provider = _Build(contributions: null);

        var state = _State(provider, ("audit", _Viewer), ("diag", _Viewer));

        var product = state.Products.Should().ContainSingle().Subject;
        product.Id.Should().Be("platform");
        product.Label.Should().Be("Platform");
        product.Modules.Should().Equal("audit", "diag");
        product.Sections.Select(section => section.Module).Should().Equal("audit", "diag");
        product
            .Sections[1]
            .Entries.Select(entry => entry.Id)
            .Should()
            .Equal("diag.nav.runs", "diag.nav.overview", "diag.nav.settings");
    }

    [Fact]
    public async Task should_leave_out_the_disabled_sidebar_entry_and_the_section_the_host_emptied()
    {
        await using var provider = _Build(c => c.Disable("diag.nav.overview", "audit.nav.entries"));

        var state = _State(provider, ("audit", _Viewer), ("diag", _Viewer));

        var section = state.Products.Should().ContainSingle().Subject.Sections.Should().ContainSingle().Subject;
        section.Module.Should().Be("diag");
        section.Entries.Select(entry => entry.Id).Should().Equal("diag.nav.runs", "diag.nav.settings");
    }

    [Fact]
    public async Task should_list_no_product_when_the_session_may_view_none_of_its_modules()
    {
        await using var provider = _Build(contributions: null);

        var state = _State(provider);

        state.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task should_mark_only_the_development_only_product_development_only_in_the_bootstrap_json()
    {
        await using var provider = _Build(contributions: null, withDevInbox: true);

        var state = _State(provider, ("audit", _Viewer), ("devinbox", _Viewer));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(state, ShellJson.BootstrapStateInfo));
        var products = json.RootElement.GetProperty("products");
        products
            .EnumerateArray()
            .Select(product => product.GetProperty("id").GetString())
            .Should()
            .Equal("platform", "dev-inbox");
        products[0].TryGetProperty("developmentOnly", out _).Should().BeFalse();
        products[1].GetProperty("developmentOnly").GetBoolean().Should().BeTrue();
    }

    private static ServiceProvider _Build(Action<ContributionOverrides>? contributions, bool withDevInbox = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDashboard(dashboard =>
        {
            dashboard
                .WithNoAuth()
                .AddModule<AuditModule, AuditModuleOptions>("audit")
                .AddModule<DiagModule, DiagModuleOptions>("diag");

            if (withDevInbox)
            {
                dashboard.AddModule<DevInboxModule, DevInboxModuleOptions>("devinbox");
            }

            if (contributions is not null)
            {
                dashboard.Contributions(contributions);
            }
        });

        return services.BuildServiceProvider();
    }

    private static BootstrapState _State(
        IServiceProvider provider,
        params (string Module, DashboardModuleAccess Access)[] access
    )
    {
        var session = new DashboardSessionSnapshot
        {
            Authenticated = true,
            Name = "viewer",
            CanOperate = false,
            Modules = access.ToDictionary(item => item.Module, item => item.Access, StringComparer.Ordinal),
        };

        return BootstrapState.Create(
            "/headless",
            provider.GetRequiredService<DashboardManifestProvider>().Manifest,
            session,
            new HeadlessDashboardOptions(),
            provider.GetRequiredService<DashboardAssetCatalog>()
        );
    }
}
