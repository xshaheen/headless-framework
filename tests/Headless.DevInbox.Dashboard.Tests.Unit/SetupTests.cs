// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard;
using Headless.DevInbox.Dashboard.Internal;
using Headless.Emails;
using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// <c>AddDevInbox</c> on a real host: it starts only in Development with at least one capture store, and serves the
/// channels the host registered.
/// </summary>
public sealed class SetupTests : TestBase
{
    [Fact]
    public async Task should_list_every_channel_when_the_host_registers_all_three_stores()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        using var client = DevInboxTestHost.Client(app);

        // when
        var channels = await DevInboxTestHost.GetJsonAsync(client, DevInboxTestHost.Api + "/channels", AbortToken);

        // then
        channels
            .GetProperty("channels")
            .EnumerateArray()
            .Select(static channel =>
                (channel.GetProperty("kind").GetString(), channel.GetProperty("capacity").GetInt32())
            )
            .Should()
            .Equal(("email", 500), ("sms", 500), ("push", 500));
    }

    [Fact]
    public async Task should_list_only_the_sms_channel_when_the_host_registers_only_the_sms_store()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken, emails: false, push: false);
        using var client = DevInboxTestHost.Client(app);

        // when
        var channels = await DevInboxTestHost.GetJsonAsync(client, DevInboxTestHost.Api + "/channels", AbortToken);

        // then
        channels
            .GetProperty("channels")
            .EnumerateArray()
            .Select(static channel => channel.GetProperty("kind").GetString())
            .Should()
            .Equal("sms");
    }

    [Fact]
    public async Task should_fail_to_start_naming_add_dev_inbox_and_development_when_the_host_runs_in_production()
    {
        // when
        var start = () => DevInboxTestHost.StartAsync(AbortToken, environment: "Production");

        // then
        var failure = await start.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("AddDevInbox").And.Contain("Development").And.Contain("'Production'");
    }

    [Fact]
    public async Task should_fail_validation_naming_development_when_the_host_has_no_environment()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessEmails(setup => setup.UseDevelopment());
        services.AddHeadlessDashboard(dashboard => dashboard.WithNoAuth().AddDevInbox());
        await using var provider = services.BuildServiceProvider();
        var validator = provider
            .GetServices<IHeadlessStartupValidator>()
            .Single(static validator => validator is DevInboxStartupValidator);

        // when
        var validate = () => validator.ValidateAsync(AbortToken);

        // then
        var failure = await validate.Should().ThrowAsync<InvalidOperationException>();
        failure
            .Which.Message.Should()
            .Contain("AddDevInbox")
            .And.Contain("Development")
            .And.Contain("no IHostEnvironment");
    }

    [Fact]
    public async Task should_fail_to_start_naming_use_development_for_each_channel_when_no_capture_store_is_registered()
    {
        // when
        var start = () => DevInboxTestHost.StartAsync(AbortToken, emails: false, sms: false, push: false);

        // then
        var failure = await start.Should().ThrowAsync<InvalidOperationException>();
        failure
            .Which.Message.Should()
            .Contain("UseDevelopment()")
            .And.Contain("AddHeadlessEmails")
            .And.Contain("AddHeadlessSms")
            .And.Contain("AddHeadlessPushNotifications");
    }

    public static TheoryData<string> Overloads => ["none", "action", "action-with-services", "configuration"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_register_the_module_through_each_overload(string overload)
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["ViewPolicy"] = "" })
            .Build();
        await using var app = await DevInboxTestHost.StartAsync(
            AbortToken,
            dashboard: setup =>
            {
                _ = overload switch
                {
                    "none" => setup.AddDevInbox(),
                    "action" => setup.AddDevInbox(static _ => { }),
                    "action-with-services" => setup.AddDevInbox(static (_, _) => { }),
                    _ => setup.AddDevInbox(configuration),
                };
            }
        );
        using var client = DevInboxTestHost.Client(app);

        // when
        using var response = await DevInboxTestHost.GetAsync(client, DevInboxTestHost.Api + "/channels", AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task should_refuse_an_operate_policy_of_white_space()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessDashboard(dashboard =>
            dashboard.WithNoAuth().AddDevInbox(static options => options.OperatePolicy = "  ")
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var read = () => provider.GetRequiredService<IOptions<DevInboxDashboardOptions>>().Value;

        // then
        read.Should().Throw<OptionsValidationException>().WithMessage("*OperatePolicy*");
    }
}
