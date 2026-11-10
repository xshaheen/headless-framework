// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json;
using Headless.Dashboard;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tests.Modules.Fixtures;

namespace Tests.Authentication;

/// <summary>
/// Per-module view and operate policies under host authentication. The shell's view policy is <c>Ops</c>; the
/// <c>diag</c> module sets its own view and operate policies, and the <c>audit</c> module inherits the shell's.
/// </summary>
public sealed class ModulePermissionTests : TestBase
{
    private readonly FakeTimeProvider _time = ShellTestHost.NewTime();

    [Fact]
    public async Task should_issue_a_session_and_report_view_for_that_module_only_when_only_a_module_view_policy_passes()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        var cookie = await ShellTestHost.SignInAsync(client, "Bearer diag-viewer", AbortToken);
        var info = await ShellTestHost.InfoAsync(client, cookie, AbortToken);

        info.GetProperty("authenticated").GetBoolean().Should().BeTrue();
        info.GetProperty("canOperate").GetBoolean().Should().BeFalse();
        var modules = info.GetProperty("modules");
        _Access(modules, "diag").Should().Be((true, false));
        _Access(modules, "audit").Should().Be((false, false));
    }

    [Fact]
    public async Task should_report_view_and_operate_for_every_module_when_the_user_passes_the_shell_policies()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        var cookie = await ShellTestHost.SignInAsync(client, "Bearer ops", AbortToken);
        var modules = (await ShellTestHost.InfoAsync(client, cookie, AbortToken)).GetProperty("modules");

        _Access(modules, "diag").Should().Be((true, true));
        _Access(modules, "audit").Should().Be((true, true));
    }

    [Fact]
    public async Task should_answer_403_naming_every_refusing_policy_when_the_user_passes_no_view_policy()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        using var exchange = await ShellTestHost.ExchangeAsync(client, "Bearer guest", AbortToken);

        exchange.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ShellTestHost.SetCookie(exchange, ShellTestHost.DefaultCookieName).Should().BeNull();
        using var problem = JsonDocument.Parse(await exchange.Content.ReadAsStringAsync(AbortToken));
        problem
            .RootElement.GetProperty("refusedPolicies")
            .EnumerateArray()
            .Select(static policy => policy.GetString())
            .Should()
            .Equal(ShellTestHost.OpsPolicy, ShellTestHost.DiagViewersPolicy);
        problem.RootElement.GetProperty("detail").GetString().Should().Contain(ShellTestHost.DiagViewersPolicy);
    }

    [Fact]
    public async Task should_open_only_the_modules_whose_view_policy_the_session_passes()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer diag-viewer", AbortToken);

        using var diag = await ShellTestHost.GetDiagAsync(client, cookie, AbortToken);
        using var audit = await ShellTestHost.SendAsync(
            client,
            HttpMethod.Get,
            "/headless/api/audit/whoami",
            AbortToken,
            cookie: cookie
        );

        diag.StatusCode.Should().Be(HttpStatusCode.OK);
        (await diag.Content.ReadAsStringAsync(AbortToken)).Should().Be("diag-viewer");
        audit.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Bearer diag-viewer", HttpStatusCode.Forbidden)]
    [InlineData("Bearer diag-operator", HttpStatusCode.OK)]
    public async Task should_require_the_module_operate_policy_for_an_operator_action(
        string hostCredential,
        HttpStatusCode expected
    )
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, hostCredential, AbortToken);

        using var response = await ShellTestHost.SendAsync(
            client,
            HttpMethod.Post,
            "/headless/api/diag/requeue",
            AbortToken,
            cookie: cookie
        );

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task should_answer_401_on_a_module_endpoint_without_a_session_even_when_the_host_user_is_signed_in()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);

        using var response = await ShellTestHost.GetDiagAsync(client, cookie: null, AbortToken, "Bearer ops");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_answer_400_when_an_operator_action_lacks_the_requested_with_header()
    {
        await using var app = await _StartAsync();
        using var client = ShellTestHost.Client(app);
        var cookie = await ShellTestHost.SignInAsync(client, "Bearer ops", AbortToken);

        using var response = await ShellTestHost.SendAsync(
            client,
            HttpMethod.Post,
            "/headless/api/diag/requeue",
            AbortToken,
            cookie: cookie,
            requestedWith: false
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private Task<WebApplication> _StartAsync()
    {
        return ShellTestHost.StartAsync(
            dashboard =>
                dashboard
                    .WithHostAuthentication(host => host.ViewPolicy = ShellTestHost.OpsPolicy)
                    .AddModule<DiagEndpointModule, DiagModuleOptions>("diag")
                    .AddModule<AuditEndpointModule, AuditModuleOptions>("audit"),
            new ShellTestHostOptions
            {
                Time = _time,
                ConfigureServices = services =>
                    services.Configure<DiagModuleOptions>(options =>
                    {
                        options.ViewPolicy = ShellTestHost.DiagViewersPolicy;
                        options.OperatePolicy = ShellTestHost.DiagOperatorsPolicy;
                    }),
            },
            AbortToken
        );
    }

    private static (bool CanView, bool CanOperate) _Access(JsonElement modules, string id)
    {
        var module = modules.GetProperty(id);
        return (module.GetProperty("canView").GetBoolean(), module.GetProperty("canOperate").GetBoolean());
    }
}
