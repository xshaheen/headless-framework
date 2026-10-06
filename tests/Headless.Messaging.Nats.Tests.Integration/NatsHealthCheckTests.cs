// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

[Collection("Nats")]
public sealed class NatsHealthCheckTests(NatsFixture fixture) : TestBase
{
    [Fact]
    public async Task should_report_nats_healthy_when_the_server_answers_ping()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup => setup.UseNats(fixture.ConnectionString));
        await using var provider = services.BuildServiceProvider();

        // when
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync(AbortToken);

        // then
        var entry = report.Entries["messaging-nats"];
        entry.Status.Should().Be(HealthStatus.Healthy);
        entry.Tags.Should().Contain([HeadlessHealthCheckTags.Ready, HeadlessHealthCheckTags.Messaging]);
    }
}
