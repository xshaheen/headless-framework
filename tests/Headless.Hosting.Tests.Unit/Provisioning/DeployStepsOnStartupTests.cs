// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Provisioning;

public sealed class DeployStepsOnStartupTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_provision_resources_before_a_lifecycle_service_registered_earlier_starts()
    {
        // given an initializer registered before the startup action, as a feature's setup usually is
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new LoggingLifecycleService(_log));
        builder.Services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
        builder.Services.AddDeployStepsOnStartup();
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then: the readiness check verifies the resource only once the host has started
        _log.Should().Equal("provision orders", "starting initializer", "verify orders");
    }

    [Fact]
    public async Task should_purge_after_provisioning_on_provision_and_purge_and_keep_the_last_choice_when_called_twice()
    {
        // given
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Microsoft.Extensions.Hosting.Environments.Development,
            }
        );
        builder.Services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders", _log));
        builder.Services.AddDeployStepsOnStartup();
        builder.Services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.ProvisionAndPurge);
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().Equal("provision orders", "purge orders", "verify orders");
    }

    [Fact]
    public async Task should_fail_the_host_start_and_touch_no_resource_on_provision_and_purge_outside_the_allowed_environments()
    {
        // given
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Microsoft.Extensions.Hosting.Environments.Production,
            }
        );
        builder.Services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders", _log));
        builder.Services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.ProvisionAndPurge);
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage(
                "*ProvisionAndPurge*'Production' environment*Use ResourceStartupAction.Provision*DestructiveEnvironments*"
            );
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_the_host_start_naming_the_resource_when_provisioning_fails()
    {
        // given
        var queue = new RecordingProvisioner("RabbitMQ", "orders", _log) { FailureMessage = "ACCESS_REFUSED" };
        queue.Failing.Add("provision");
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ => queue);
        builder.Services.AddDeployStepsOnStartup();
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*RabbitMQ orders: ACCESS_REFUSED*");
    }

    [Fact]
    public async Task should_seed_without_provisioning_when_the_resource_step_is_none()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
        builder.Services.AddSeeder(_ => new LoggingSeeder(_log));
        builder.Services.AddDeployStepsOnStartup(o => o.Resources = ResourceStartupAction.None);
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().Equal("seed", "verify orders");
    }

    [Fact]
    public async Task should_provision_without_seeding_when_the_seed_step_is_off()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
        builder.Services.AddSeeder(_ => new LoggingSeeder(_log));
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().Equal("provision orders", "verify orders");
    }

    [Fact]
    public void should_keep_the_registered_steps_when_try_add_runs_after_add()
    {
        // given
        var services = new ServiceCollection();
        services.AddDeployStepsOnStartup(o => o.Seed = false);

        // when
        services.TryAddDeployStepsOnStartup(o => o.Seed = true);
        using var provider = services.BuildServiceProvider();

        // then
        provider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<DeployStepsOnStartupOptions>>()
            .Value.Seed.Should()
            .BeFalse();
        services.Count(d => d.ServiceType == typeof(IHostedService)).Should().Be(2);
    }

    private sealed class LoggingSeeder(List<string> log) : ISeeder
    {
        public Task SeedAsync(CancellationToken cancellationToken)
        {
            log.Add("seed");

            return Task.CompletedTask;
        }
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
