// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tests.Modules.Fixtures;

namespace Tests.Setup;

public sealed class SetupDashboardTests : TestBase
{
    [Fact]
    public void should_throw_naming_every_auth_mode_when_no_auth_mode_is_chosen()
    {
        var services = new ServiceCollection();

        var act = () => services.AddHeadlessDashboard(dashboard => dashboard.SetBasePath("/ops"));

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("WithHostAuthentication");
        message.Should().Contain("WithBasicAuth");
        message.Should().Contain("WithApiKey");
        message.Should().Contain("WithNoAuth");
    }

    [Fact]
    public void should_throw_naming_the_id_when_two_modules_share_an_id()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessDashboard(dashboard =>
                dashboard
                    .WithNoAuth()
                    .AddModule<DiagModule, DiagModuleOptions>("diag")
                    .AddModule<AuditModule, AuditModuleOptions>("diag")
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*'diag'*");
    }

    [Theory]
    [InlineData("api")]
    [InlineData("needs-you")]
    public void should_throw_as_reserved_when_a_module_id_names_a_shell_path(string id)
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessDashboard(dashboard =>
                dashboard.WithNoAuth().AddModule<DiagModule, DiagModuleOptions>(id)
            );

        act.Should().Throw<ArgumentException>().WithMessage($"*'{id}' is reserved*");
    }

    [Fact]
    public async Task should_fail_start_when_the_dashboard_is_registered_but_not_mapped()
    {
        await using var app = _Build(dashboard => dashboard.WithNoAuth());

        var act = () => app.StartAsync(AbortToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*MapHeadlessDashboard()*");
    }

    [Fact]
    public async Task should_start_when_the_dashboard_is_registered_and_mapped()
    {
        await using var app = _Build(dashboard =>
            dashboard.WithNoAuth().AddModule<DiagModule, DiagModuleOptions>("diag")
        );
        app.MapHeadlessDashboard();

        await app.StartAsync(AbortToken);
        await app.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_throw_when_the_dashboard_is_mapped_twice()
    {
        await using var app = _Build(dashboard => dashboard.WithNoAuth());
        app.MapHeadlessDashboard();

        var act = () => app.MapHeadlessDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*already called*");
    }

    [Fact]
    public async Task should_throw_when_mapping_a_dashboard_that_is_not_registered()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();

        var act = () => app.MapHeadlessDashboard();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessDashboard*");
    }

    [Fact]
    public async Task should_use_the_module_operate_policy_and_inherit_the_shell_policies_when_the_module_sets_none()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<DiagModuleOptions>(options => options.OperatePolicy = "diag.operate");
        services.AddHeadlessDashboard(dashboard =>
            dashboard
                .WithHostAuthentication(host =>
                {
                    host.ViewPolicy = "ops.view";
                    host.OperatePolicy = "ops.operate";
                })
                .AddModule<DiagModule, DiagModuleOptions>("diag")
                .AddModule<AuditModule, AuditModuleOptions>("audit")
        );

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<DashboardModuleCatalog>();

        catalog
            .GetPolicy("diag")
            .Should()
            .Be(new DashboardModulePolicy { ViewPolicy = "ops.view", OperatePolicy = "diag.operate" });
        catalog
            .GetPolicy("audit")
            .Should()
            .Be(new DashboardModulePolicy { ViewPolicy = "ops.view", OperatePolicy = "ops.operate" });
    }

    [Fact]
    public async Task should_refuse_the_options_when_the_base_path_is_the_root()
    {
        await using var provider = _BuildProvider(dashboard => dashboard.WithNoAuth().SetBasePath("/"));

        var act = () => provider.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*root path*");
    }

    [Fact]
    public async Task should_refuse_the_options_when_host_authentication_allows_cors_origins()
    {
        await using var provider = _BuildProvider(dashboard =>
            dashboard.WithHostAuthentication().SetCorsOrigins("https://admin.example.com")
        );

        var act = () => provider.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Host authentication*");
    }

    [Fact]
    public async Task should_refuse_the_options_when_the_trace_link_template_has_no_trace_id()
    {
        await using var provider = _BuildProvider(dashboard =>
            dashboard.WithNoAuth().SetTraceLinkTemplate("https://apm.example/trace/")
        );

        var act = () => provider.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*{traceId}*");
    }

    private static ServiceProvider _BuildProvider(Action<HeadlessDashboardSetupBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDashboard(configure);

        return services.BuildServiceProvider();
    }

    private static WebApplication _Build(Action<HeadlessDashboardSetupBuilder> configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddHeadlessDashboard(configure);

        return builder.Build();
    }
}
