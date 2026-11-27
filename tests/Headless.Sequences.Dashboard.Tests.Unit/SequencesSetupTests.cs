// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SequencesSetupTests : TestBase
{
    public static TheoryData<string> Overloads => ["none", "action", "action-with-provider", "configuration"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_serve_the_module_under_its_id_from_every_registration_overload(string overload)
    {
        // given
        Action<HeadlessDashboardSetupBuilder> addModule = overload switch
        {
            "none" => static dashboard => dashboard.AddSequences(),
            "action" => static dashboard =>
                dashboard.AddSequences(options => options.ViewPolicy = SequencesHost.OperatorsPolicy),
            "action-with-provider" => static dashboard =>
                dashboard.AddSequences((options, _) => options.ViewPolicy = SequencesHost.OperatorsPolicy),
            _ => static dashboard =>
                dashboard.AddSequences(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(
                            new Dictionary<string, string?>(StringComparer.Ordinal)
                            {
                                ["ViewPolicy"] = SequencesHost.OperatorsPolicy,
                            }
                        )
                        .Build()
                ),
        };

        // when
        await using var host = await SequencesHost.StartAsync(AbortToken, addModule: addModule);

        // then
        var module = host.App.Services.GetRequiredService<SequencesModule>();
        module.Id.Should().Be("sequences");
        module.DisplayName.Should().Be("Sequences");

        var (viewerStatus, _) = await host.GetAsync("/summary", AbortToken);

        if (string.Equals(overload, "none", StringComparison.Ordinal))
        {
            viewerStatus.Should().Be(HttpStatusCode.OK);
        }
        else
        {
            viewerStatus.Should().Be(HttpStatusCode.Forbidden);

            var opsCookie = await host.SignInAsync("ops", AbortToken);
            using var opsResponse = await host.SendAsync(
                HttpMethod.Get,
                SequencesHost.Api + "/summary",
                opsCookie,
                AbortToken
            );
            opsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task should_fail_at_startup_naming_add_headless_sequences_when_no_inspector_is_registered()
    {
        // when
        var start = () => SequencesHost.StartAsync(AbortToken, addSequences: false);

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("AddHeadlessSequences");
    }

    [Theory]
    [InlineData("view")]
    [InlineData("operate")]
    public async Task should_refuse_a_white_space_policy_name(string policy)
    {
        // given
        var start = () =>
            SequencesHost.StartAsync(
                AbortToken,
                addModule: dashboard =>
                    dashboard.AddSequences(options =>
                    {
                        if (string.Equals(policy, "view", StringComparison.Ordinal))
                        {
                            options.ViewPolicy = "  ";
                        }
                        else
                        {
                            options.OperatePolicy = "  ";
                        }
                    })
            );

        // then
        await start.Should().ThrowAsync<OptionsValidationException>();
    }
}
