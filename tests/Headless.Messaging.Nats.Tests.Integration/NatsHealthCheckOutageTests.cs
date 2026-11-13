// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Tests;

/// <summary>
/// The <c>messaging-nats</c> check against a server that goes away: the ping waits for the client's reconnect, so the
/// check must end on its own timeout instead of holding the health request open for the whole outage.
/// </summary>
[Collection("NatsConnectivity")]
public sealed class NatsHealthCheckOutageTests : TestBase
{
    private readonly FixedPortNatsFixture _broker = new();

    [Fact]
    public async Task should_report_nats_unhealthy_within_its_timeout_while_the_server_is_down()
    {
        await _broker.StartAsync();

        try
        {
            // given - a host whose pooled connection has reached the server
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHeadlessMessaging(setup => setup.UseNats(_broker.ConnectionString));
            await using var provider = services.BuildServiceProvider();
            var health = provider.GetRequiredService<HealthCheckService>();
            (await _CheckNatsAsync(health)).Status.Should().Be(HealthStatus.Healthy);

            // when
            await _broker.Container.StopAsync(AbortToken);

            // then - the 5 second default ends the probe well before this bound; without it the probe never returns
            var entry = await _CheckNatsAsync(health).WaitAsync(TimeSpan.FromSeconds(30), AbortToken);
            entry.Status.Should().Be(HealthStatus.Unhealthy);
        }
        finally
        {
            await ((IAsyncDisposable)_broker).DisposeAsync();
        }
    }

    private static async Task<HealthReportEntry> _CheckNatsAsync(HealthCheckService health)
    {
        var report = await health.CheckHealthAsync(
            registration => string.Equals(registration.Name, "messaging-nats", StringComparison.Ordinal),
            AbortToken
        );

        return report.Entries["messaging-nats"];
    }
}
