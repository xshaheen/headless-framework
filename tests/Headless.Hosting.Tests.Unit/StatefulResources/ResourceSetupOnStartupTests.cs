// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.StatefulResources;

public sealed class ResourceSetupOnStartupTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_set_up_resources_before_a_lifecycle_service_registered_earlier_starts()
    {
        // given an initializer registered before the startup action, as a feature's setup usually is
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new LoggingLifecycleService(_log));
        builder.Services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "orders", _log));
        builder.Services.AddHeadlessResourceSetupOnStartup();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().Equal("setup orders", "starting initializer");
    }

    [Fact]
    public async Task should_clear_after_setup_on_reset_state_and_keep_only_the_last_action_when_called_twice()
    {
        // given
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Microsoft.Extensions.Hosting.Environments.Development,
            }
        );
        builder.Services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders", _log));
        builder.Services.AddHeadlessResourceSetupOnStartup();
        builder.Services.AddHeadlessResourceSetupOnStartup(StatefulResourceStartupAction.ResetState);
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().Equal("setup orders", "clear orders");
    }

    [Fact]
    public async Task should_fail_the_host_start_and_touch_no_resource_on_reset_state_in_production()
    {
        // given
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Microsoft.Extensions.Hosting.Environments.Production,
            }
        );
        builder.Services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders", _log));
        builder.Services.AddHeadlessResourceSetupOnStartup(StatefulResourceStartupAction.ResetState);
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*ResetState*refuses to run in the Production environment*");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_the_host_start_naming_the_resource_when_setup_fails()
    {
        // given
        var queue = new RecordingResource("RabbitMQ", "orders", _log) { FailureMessage = "ACCESS_REFUSED" };
        queue.Failing.Add("setup");
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHeadlessStatefulResource(_ => queue);
        builder.Services.AddHeadlessResourceSetupOnStartup();
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<StatefulResourceException>().WithMessage("*RabbitMQ orders: ACCESS_REFUSED*");
    }

    private sealed class LoggingLifecycleService(List<string> log) : IHostedLifecycleService
    {
        public Task StartingAsync(CancellationToken cancellationToken)
        {
            log.Add("starting initializer");

            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
