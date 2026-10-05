// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Api.Features;
using Headless.Features;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class RequiresFeatureEnforcementTests : TestBase
{
    private const string _Reports = "Reports";
    private const string _Exports = "Exports";

    #region MVC

    [Fact]
    public async Task should_register_the_mvc_filter_once_when_adding_http_features_twice()
    {
        // given
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddHeadlessHttpFeatures();
        services.AddHeadlessHttpFeatures();
        await using var provider = services.BuildServiceProvider();

        // when
        var filters = provider.GetRequiredService<IOptions<MvcOptions>>().Value.Filters;

        // then
        filters.Should().ContainSingle(filter => filter is RequiresFeatureResourceFilter);
    }

    [Fact]
    public async Task should_fail_startup_when_feature_management_is_not_registered()
    {
        // given
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHeadlessHttpFeatures();
        await using var app = builder.Build();

        // when
        var act = () => app.StartAsync(AbortToken);

        // then
        var exception = (await act.Should().ThrowAsync<MissingRequiredServiceException>()).Which;
        exception.MissingServices.Should().ContainSingle().Which.ServiceType.Should().Be<IFeatureManager>();
    }

    [Fact]
    public async Task should_reject_a_controller_action_when_the_controller_feature_is_disabled()
    {
        // given
        await using var app = await _StartAsync();

        // when
        var act = () => app.GetTestClient().GetAsync(new Uri("/reports", UriKind.Relative), AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_run_a_controller_action_when_the_controller_feature_is_enabled()
    {
        // given
        await using var app = await _StartAsync(_Reports);

        // when
        var body = await app.GetTestClient().GetStringAsync(new Uri("/reports", UriKind.Relative), AbortToken);

        // then
        body.Should().Be("reports");
    }

    [Fact]
    public async Task should_apply_the_action_requirement_on_top_of_the_controller_requirement()
    {
        // given - the controller feature is on, the action's own feature is off
        await using var app = await _StartAsync(_Reports);

        // when
        var act = () => app.GetTestClient().GetAsync(new Uri("/reports/export", UriKind.Relative), AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_run_an_action_that_disables_the_feature_check_when_the_controller_feature_is_disabled()
    {
        // given
        await using var app = await _StartAsync();

        // when
        var body = await app.GetTestClient().GetStringAsync(new Uri("/reports/status", UriKind.Relative), AbortToken);

        // then
        body.Should().Be("status");
    }

    [Fact]
    public async Task should_enforce_a_requirement_added_through_the_controller_route_convention()
    {
        // given - the controller's own feature is on, the convention's feature is off
        await using var app = await _StartAsync([_Reports], gateControllersOn: _Exports);

        // when
        var act = () => app.GetTestClient().GetAsync(new Uri("/reports", UriKind.Relative), AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    #endregion

    #region Minimal API

    [Fact]
    public async Task should_reject_a_minimal_api_endpoint_when_its_feature_is_disabled()
    {
        // given
        await using var app = await _StartAsync();

        // when
        var act = () => app.GetTestClient().GetAsync(new Uri("/minimal", UriKind.Relative), AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_run_a_minimal_api_endpoint_when_its_feature_is_enabled()
    {
        // given
        await using var app = await _StartAsync(_Reports);

        // when
        var body = await app.GetTestClient().GetStringAsync(new Uri("/minimal", UriKind.Relative), AbortToken);

        // then
        body.Should().Be("minimal");
    }

    [Fact]
    public async Task should_require_every_feature_when_requires_all_is_set()
    {
        // given - only one of the two required features is on
        await using var app = await _StartAsync(_Reports);

        // when
        var act = () => app.GetTestClient().GetAsync(new Uri("/minimal/all", UriKind.Relative), AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_gate_every_endpoint_of_a_route_group()
    {
        // given
        await using var app = await _StartAsync();

        // when
        var act = () => app.GetTestClient().GetAsync(new Uri("/group/gated", UriKind.Relative), AbortToken);

        // then
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task should_let_a_group_endpoint_opt_out_with_disable_feature_check()
    {
        // given
        await using var app = await _StartAsync();

        // when
        var body = await app.GetTestClient().GetStringAsync(new Uri("/group/open", UriKind.Relative), AbortToken);

        // then
        body.Should().Be("open");
    }

    [Fact]
    public void should_reject_an_empty_feature_list()
    {
        // given
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        using var app = builder.Build();
        var endpoint = app.MapGet("/empty", () => "empty");

        // when
        var act = () => endpoint.RequireFeatures();

        // then
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region Helpers

    private static Task<WebApplication> _StartAsync(params string[] enabledFeatures)
    {
        return _StartAsync(enabledFeatures, gateControllersOn: null);
    }

    private static async Task<WebApplication> _StartAsync(string[] enabledFeatures, string? gateControllersOn)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        var featureManager = Substitute.For<IFeatureManager>();
        featureManager
            .GetAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                var value = enabledFeatures.Contains(name, StringComparer.Ordinal) ? "true" : "false";

                return new FeatureValue(name, value, Provider: null);
            });
        builder.Services.AddSingleton(featureManager);

        // A substituted IFeatureManager stands in for AddHeadlessFeatures, keeping this host free of the storage and
        // caching prerequisites the full feature registration needs.
        builder.Services.AddHeadlessHttpFeatures();
        builder.Services.AddControllers().AddApplicationPart(typeof(RequiresFeatureEnforcementTests).Assembly);

        var app = builder.Build();
        var controllers = app.MapControllers();

        if (gateControllersOn is not null)
        {
            controllers.RequireFeatures(gateControllersOn);
        }

        app.MapGet("/minimal", () => "minimal").RequireFeatures(_Reports);
        app.MapGet("/minimal/all", () => "all").RequireFeatures(requiresAll: true, _Reports, _Exports);

        var group = app.MapGroup("/group").RequireFeatures(_Reports);
        group.MapGet("/gated", () => "gated");
        group.MapGet("/open", [DisableFeatureCheck] () => "open");

        await app.StartAsync(AbortToken);

        return app;
    }

    #endregion
}

[ApiController]
[Route("reports")]
[RequiresFeature("Reports")]
public sealed class ReportsTestController : ControllerBase
{
    [HttpGet]
    public string Get() => "reports";

    [HttpGet("export")]
    [RequiresFeature("Exports")]
    public string Export() => "export";

    [HttpGet("status")]
    [DisableFeatureCheck]
    public string Status() => "status";
}
