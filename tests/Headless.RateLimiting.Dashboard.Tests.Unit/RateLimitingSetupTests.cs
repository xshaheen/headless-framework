// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.RateLimiting;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests;

public sealed class RateLimitingSetupTests : TestBase
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 26, 14, 20, 0, TimeSpan.Zero));
    private readonly RecordingOperatorLog _log = new();

    public static TheoryData<string> Overloads => ["none", "action", "action-with-provider", "configuration"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_serve_the_module_under_its_id_from_every_registration_overload(string overload)
    {
        // given
        Action<HeadlessDashboardSetupBuilder> addModule = overload switch
        {
            "none" => static dashboard => dashboard.AddRateLimiting(),
            "action" => static dashboard => dashboard.AddRateLimiting(options => options.OperatePolicy = "Operators"),
            "action-with-provider" => static dashboard =>
                dashboard.AddRateLimiting((options, _) => options.OperatePolicy = "Operators"),
            _ => static dashboard =>
                dashboard.AddRateLimiting(
                    new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?> { ["OperatePolicy"] = "Operators" })
                        .Build()
                ),
        };

        // when
        await using var app = await RateLimitingHost.StartAsync(_time, _log, AbortToken, addModule: addModule);

        // then
        var module = app.Services.GetRequiredService<RateLimitingModule>();
        module.Id.Should().Be("rate-limiting");
        module.DisplayName.Should().Be("Rate limits");
    }

    [Fact]
    public async Task should_fail_at_startup_naming_add_attempt_limiter_when_no_limiter_is_registered()
    {
        // when
        var start = () => RateLimitingHost.StartAsync(_time, _log, AbortToken, addLimiter: false);

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("AddAttemptLimiter");
    }

    [Fact]
    public async Task should_fail_at_startup_naming_add_attempt_limiter_when_only_a_custom_limiter_is_registered()
    {
        // given: a custom limiter without AddAttemptLimiter, so no fingerprinter is registered
        var start = () =>
            RateLimitingHost.StartAsync(
                _time,
                _log,
                AbortToken,
                addLimiter: false,
                configure: static services => services.AddSingleton(Substitute.For<IAttemptLimiter>())
            );

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.Message.Should()
            .Contain("AddAttemptLimiter");
    }

    [Theory]
    [InlineData("view")]
    [InlineData("operate")]
    public async Task should_refuse_a_white_space_policy_name(string policy)
    {
        // given
        var start = () =>
            RateLimitingHost.StartAsync(
                _time,
                _log,
                AbortToken,
                addModule: dashboard =>
                    dashboard.AddRateLimiting(options =>
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
