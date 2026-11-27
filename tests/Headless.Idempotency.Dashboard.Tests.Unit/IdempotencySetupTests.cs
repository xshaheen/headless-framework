// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.Idempotency;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Tests;

public sealed class IdempotencySetupTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 27, 14, 20, 0, TimeSpan.Zero));

    public static TheoryData<string> Overloads => ["none", "action", "action-with-provider", "configuration"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_serve_the_store_under_the_module_id_from_every_registration_overload(string overload)
    {
        // given
        Action<HeadlessDashboardSetupBuilder> addModule = overload switch
        {
            "none" => static dashboard => dashboard.AddIdempotency(),
            "action" => static dashboard => dashboard.AddIdempotency(options => options.OperatePolicy = "Operators"),
            "action-with-provider" => static dashboard =>
                dashboard.AddIdempotency((options, _) => options.OperatePolicy = "Operators"),
            _ => static dashboard =>
                dashboard.AddIdempotency(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["OperatePolicy"] = "Operators" })
                        .Build()
                ),
        };
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken, addModule: addModule);
        using var client = IdempotencyHost.Client(app);
        var cookie = await IdempotencyHost.SignInAsync(client, "viewer", AbortToken);

        // when
        using var response = await IdempotencyHost.GetAsync(client, IdempotencyHost.Api + "/store", cookie, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var module = app.Services.GetRequiredService<IdempotencyModule>();
        module.Id.Should().Be("idempotency");
        module.DisplayName.Should().Be("Idempotency");
    }

    [Fact]
    public async Task should_fail_at_startup_naming_add_headless_idempotency_when_no_store_is_registered()
    {
        // when
        var start = () => IdempotencyHost.StartAsync(_time, AbortToken, addIdempotency: false);

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("AddHeadlessIdempotency")
            .And.Contain("Idempotency dashboard module");
    }

    [Theory]
    [InlineData("view")]
    [InlineData("operate")]
    public async Task should_refuse_a_white_space_policy_name(string policy)
    {
        // given
        var start = () =>
            IdempotencyHost.StartAsync(
                _time,
                AbortToken,
                addModule: dashboard =>
                    dashboard.AddIdempotency(options =>
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

    [Fact]
    public async Task should_refuse_adding_the_module_twice_naming_its_id()
    {
        // given
        var start = () =>
            IdempotencyHost.StartAsync(
                _time,
                AbortToken,
                addModule: static dashboard => dashboard.AddIdempotency().AddIdempotency()
            );

        // then
        (await start.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
            .Contain("idempotency");
    }

    [Fact]
    public async Task should_declare_no_command_object_kind_or_count()
    {
        // given
        await using var app = await IdempotencyHost.StartAsync(_time, AbortToken);

        // when
        var module = app.Services.GetRequiredService<IdempotencyModule>();

        // then: releasing or deleting a live attempt's record could run its operation twice, and keys are not countable
        module.Should().NotBeAssignableTo<IDashboardCommandSource>();
        module.Should().NotBeAssignableTo<IDashboardCountSource>();
        module.Should().NotBeAssignableTo<IDashboardObjectResolver>();
    }
}
