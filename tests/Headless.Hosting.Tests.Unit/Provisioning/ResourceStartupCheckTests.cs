// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Tests.Provisioning;

public sealed class ResourceStartupCheckTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_fail_the_start_before_any_hosted_service_starts_when_a_fail_start_resource_is_missing()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new StartLogger(_log));
        builder.Services.AddResourceProvisioner(_ => _Missing("Schema", "orders", ResourceStartupCheck.FailStart));
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        (
            await act.Should()
                .ThrowAsync<ResourceProvisioningException>()
                .WithMessage("*Schema orders: orders is missing*")
        )
            .WithInnerException<InvalidOperationException>()
            .WithMessage("orders is missing");
        _log.Should().Equal("verify orders");
    }

    [Fact]
    public async Task should_name_every_missing_resource_and_keep_each_error_when_several_fail_the_start()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ => _Missing("Schema", "orders", ResourceStartupCheck.FailStart));
        builder.Services.AddResourceProvisioner(_ => _Missing("Schema", "invoices", ResourceStartupCheck.FailStart));
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        var thrown = await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*2 resource(s)*Schema orders: orders is missing*Schema invoices: invoices is missing*");
        thrown.Which.Failures.Select(static e => e.Message).Should().Equal("orders is missing", "invoices is missing");
    }

    [Fact]
    public async Task should_verify_a_fail_start_resource_after_the_startup_provisioning_creates_it()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log)
        {
            StartupCheck = ResourceStartupCheck.FailStart,
        });
        builder.Services.AddDeployStepsOnStartup(o => o.Seed = false);
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().Equal("provision orders", "verify orders");
    }

    [Fact]
    public async Task should_start_and_hold_readiness_naming_the_resource_while_a_gate_readiness_resource_is_missing()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddHostedService(_ => new StartLogger(_log));
        builder.Services.AddResourceProvisioner(_ =>
            _Missing("RabbitMQ", "orders", ResourceStartupCheck.GateReadiness)
        );
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(AbortToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(2));
        var wait = () => host.Services.WaitForWarmupAsync(bounded.Token);

        // then
        await wait.Should()
            .ThrowAsync<OperationCanceledException>()
            .WithMessage("*Resources (last error: *RabbitMQ orders: orders is missing*");
        var ready = await _CheckWarmupAsync(host.Services);
        ready.Status.Should().Be(HealthStatus.Unhealthy);
        ready.Exception!.Message.Should().Contain("RabbitMQ orders: orders is missing");

        // Read once the host stopped: the warm-up retries on its own thread until then.
        await host.StopAsync(AbortToken);
        _log.Should().StartWith("start", "the host started instead of failing").And.Contain("verify orders");
    }

    [Fact]
    public async Task should_never_verify_a_resource_whose_startup_check_is_none()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ => _Missing("Blobs", "avatars", ResourceStartupCheck.None));
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_skip_a_fail_start_resource_when_the_application_sets_its_kind_to_none()
    {
        // given: the kind is matched case-insensitively
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ => _Missing("Schema", "orders", ResourceStartupCheck.FailStart));
        builder.Services.ConfigureResourceProvisioning(o => o.StartupChecks["schema"] = ResourceStartupCheck.None);
        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);
        await host.Services.WaitForWarmupAsync(AbortToken);
        await host.StopAsync(AbortToken);

        // then
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_the_start_for_a_gate_readiness_resource_when_the_application_sets_its_kind_to_fail_start()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddResourceProvisioner(_ =>
            _Missing("RabbitMQ", "orders", ResourceStartupCheck.GateReadiness)
        );
        builder.Services.ConfigureResourceProvisioning(o =>
            o.StartupChecks["RabbitMQ"] = ResourceStartupCheck.FailStart
        );
        using var host = builder.Build();

        // when
        var act = () => host.StartAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<ResourceProvisioningException>().WithMessage("*RabbitMQ orders*");
    }

    [Fact]
    public void should_register_the_startup_validator_and_the_warmup_once_however_many_provisioners_are_added()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddResourceProvisioner(_ => _Missing("Schema", "orders", ResourceStartupCheck.FailStart));
        services.AddResourceProvisioner(_ => _Missing("Schema", "invoices", ResourceStartupCheck.FailStart));
        services.AddResourceProvisionerSource<EmptySource>();

        // then
        services.Count(static d => d.ImplementationType == typeof(ResourceStartupValidator)).Should().Be(1);
        services.Count(static d => d.ImplementationType == typeof(ResourceReadinessWarmup)).Should().Be(1);
    }

    private RecordingProvisioner _Missing(string kind, string name, ResourceStartupCheck check)
    {
        var provisioner = new RecordingProvisioner(kind, name, _log)
        {
            StartupCheck = check,
            FailureMessage = $"{name} is missing",
        };
        provisioner.Failing.Add("verify");

        return provisioner;
    }

    private async Task<HealthReportEntry> _CheckWarmupAsync(IServiceProvider services)
    {
        var report = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                static r => string.Equals(r.Name, HeadlessHealthCheckNames.Warmup, StringComparison.Ordinal),
                AbortToken
            );

        return report.Entries[HeadlessHealthCheckNames.Warmup];
    }

    private sealed class StartLogger(List<string> log) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            log.Add("start");

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptySource : IResourceProvisionerSource
    {
        public Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IResourceProvisioner>>([]);
    }
}
