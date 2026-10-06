// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class NatsHealthCheckRegistrationTests : TestBase
{
    [Fact]
    public async Task should_contribute_messaging_nats_readiness_check_without_connecting()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));
        await using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should()
            .ContainSingle(registration => registration.Name == "messaging-nats")
            .Which.Tags.Should()
            .Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Messaging]);
    }

    [Fact]
    public async Task should_apply_the_configured_options_to_the_nats_check()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHeadlessHealthCheck(
            "messaging-nats",
            options =>
            {
                options.FailureStatus = HealthStatus.Degraded;
                options.Timeout = TimeSpan.FromSeconds(2);
            }
        );

        // when
        services.AddHeadlessMessaging(setup => setup.UseNats("nats://localhost:4222"));
        await using var provider = services.BuildServiceProvider();

        // then
        var registration = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should()
            .ContainSingle(registration => registration.Name == "messaging-nats")
            .Subject;
        registration.FailureStatus.Should().Be(HealthStatus.Degraded);
        registration.Timeout.Should().Be(TimeSpan.FromSeconds(2));
    }
}
