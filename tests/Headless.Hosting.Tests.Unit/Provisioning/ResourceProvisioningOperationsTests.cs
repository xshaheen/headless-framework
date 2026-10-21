// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Provisioning;

public sealed class ResourceProvisioningOperationsTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_provision_direct_registrations_then_source_resources_in_registration_order()
    {
        // given
        var services = new ServiceCollection();
        services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
        services.AddSingleton<IResourceProvisionerSource>(
            new FixedProvisionerSource([
                new RecordingProvisioner("RabbitMQ", "a", _log),
                new RecordingProvisioner("RabbitMQ", "b", _log),
            ])
        );
        services.AddResourceProvisioner(_ => new RecordingProvisioner("Blob container", "files", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        _log.Should().Equal("provision orders", "provision files", "provision a", "provision b");
    }

    [Fact]
    public async Task should_provision_a_dependency_before_the_resource_that_needs_it_and_tear_it_down_after()
    {
        // given queues stored in a database registered after them
        var services = new ServiceCollection();
        services.AddResourceProvisioner(_ => new DependentProvisioner("Queue", "jobs", _log, "db"));
        services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "db", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.ProvisionResourcesAsync(AbortToken);
        await provider.DeleteResourcesAsync(AbortToken);

        // then
        _log.Should().Equal("provision db", "provision jobs", "delete jobs", "delete db");
    }

    [Fact]
    public async Task should_purge_a_resource_before_the_resource_it_depends_on()
    {
        // given rows in 'jobs' that reference rows in 'db', as a cross-feature foreign key would
        var services = new ServiceCollection();
        services.AddResourceProvisioner(_ => new DependentProvisioner("Queue", "jobs", _log, "db"));
        services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "db", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.PurgeResourcesAsync(AbortToken);

        // then
        _log.Should().Equal("purge jobs", "purge db");
    }

    [Fact]
    public async Task should_name_the_cycle_when_two_resources_depend_on_each_other()
    {
        // given
        var services = new ServiceCollection();
        services.AddResourceProvisioner(_ => new DependentProvisioner("Queue", "a", _log, "b"));
        services.AddResourceProvisioner(_ => new DependentProvisioner("Queue", "b", _log, "a"));
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.ProvisionResourcesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Queue a -> Queue b -> Queue a*");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_every_resource_then_throw_one_exception_naming_each_failure()
    {
        // given
        var first = new RecordingProvisioner("RabbitMQ", "orders", _log) { FailureMessage = "ACCESS_REFUSED" };
        first.Failing.Add("provision");
        var last = new RecordingProvisioner("Schema", "billing", _log) { FailureMessage = "connection refused" };
        last.Failing.Add("provision");
        var services = new ServiceCollection();
        services.AddResourceProvisioner(_ => first);
        services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "ok", _log));
        services.AddResourceProvisioner(_ => last);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.ProvisionResourcesAsync(AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<ResourceProvisioningException>();
        thrown.WithMessage("*2 resource(s)*RabbitMQ orders: ACCESS_REFUSED*Schema billing: connection refused*");
        thrown.Which.Failures.Should().HaveCount(2);
        _log.Should().Equal("provision orders", "provision ok", "provision billing");
    }

    [Theory]
    [InlineData("purge")]
    [InlineData("delete")]
    public async Task should_refuse_to_delete_data_and_touch_no_resource_outside_the_allowed_environments(
        string operation
    )
    {
        // given
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Live");
        var services = new ServiceCollection();
        services.AddSingleton(environment);
        services.AddResourceProvisioner(_ => new RecordingProvisioner("Schema", "orders", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        Func<Task> act = string.Equals(operation, "purge", StringComparison.Ordinal)
            ? () => provider.PurgeResourcesAsync(AbortToken)
            : () => provider.DeleteResourcesAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*'Live' environment*DestructiveEnvironments*");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_skip_a_provisioner_that_cannot_purge_without_calling_it_or_failing()
    {
        // given
        var schema = new RecordingProvisioner("Schema", "orders", _log);
        schema.Unsupported.Add("purge");
        var services = new ServiceCollection();
        services.AddResourceProvisioner(_ => schema);
        services.AddResourceProvisioner(_ => new RecordingProvisioner("RabbitMQ", "orders.queue", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.PurgeResourcesAsync(AbortToken);

        // then
        _log.Should().Equal("purge orders.queue");
    }

    [Fact]
    public async Task should_purge_and_delete_a_provisioner_that_keeps_the_default_capabilities()
    {
        // given — a provisioner that overrides neither CanPurge, CanDelete, nor DependsOn
        var provisioner = new MinimalProvisioner();
        var services = new ServiceCollection();
        services.AddResourceProvisioner(_ => provisioner);
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.PurgeResourcesAsync(AbortToken);
        await provider.DeleteResourcesAsync(AbortToken);

        // then
        provisioner.Operations.Should().Equal("purge", "delete");
    }

    [Fact]
    public async Task should_register_a_source_type_once_however_often_a_feature_adds_it()
    {
        // given
        var services = new ServiceCollection();
        services.AddResourceProvisionerSource<SingleQueueSource>();
        services.AddResourceProvisionerSource<SingleQueueSource>();
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.ProvisionResourcesAsync(AbortToken);

        // then
        provider.GetServices<IResourceProvisionerSource>().Should().ContainSingle();
    }

    [Fact]
    public async Task should_do_nothing_when_no_resource_is_registered()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => provider.ProvisionResourcesAsync(AbortToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_fail_a_resource_whose_driver_throws_not_supported_instead_of_skipping_it()
    {
        // given — only CanPurge and CanDelete mark an operation as unsupported on purpose
        var services = new ServiceCollection();
        var queues = new RecordingProvisioner("Broker", "queues", _log);
        queues.DriverNotSupported.Add("provision");
        services.AddResourceProvisioner(_ => queues);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.ProvisionResourcesAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<ResourceProvisioningException>()
            .WithMessage("*Specified method is not supported*");
    }

    [Fact]
    public async Task should_report_the_latest_outcome_after_a_host_restart()
    {
        // given — a resource that fails the first start and succeeds the second
        var flaky = new RecordingProvisioner("Schema", "orders", _log);
        flaky.Failing.Add("provision");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddResourceProvisioner(_ => flaky);
        services.AddDeployStepsOnStartup();
        await using var provider = services.BuildServiceProvider();
        var startup = provider.GetServices<IHostedService>().OfType<ResourceProvisioningStartupService>().Single();
        var initializer = (IInitializer)startup;

        // when
        await FluentActions
            .Awaiting(() => startup.StartingAsync(AbortToken))
            .Should()
            .ThrowAsync<ResourceProvisioningException>();
        flaky.Failing.Clear();
        await startup.StartingAsync(AbortToken);

        // then
        initializer.IsInitialized.Should().BeTrue();
        await initializer.WaitForInitializationAsync(AbortToken);
    }

    private sealed class SingleQueueSource : IResourceProvisionerSource
    {
        public Task<IReadOnlyList<IResourceProvisioner>> GetProvisionersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IResourceProvisioner>>([new RecordingProvisioner("RabbitMQ", "q", [])]);
    }

    private sealed class MinimalProvisioner : IResourceProvisioner
    {
        public List<string> Operations { get; } = [];

        public string Kind => "Minimal";

        public string Name => "only";

        public Task ProvisionAsync(CancellationToken cancellationToken) => _Record("provision");

        public Task VerifyAsync(CancellationToken cancellationToken) => _Record("verify");

        public Task PurgeAsync(CancellationToken cancellationToken) => _Record("purge");

        public Task DeleteAsync(CancellationToken cancellationToken) => _Record("delete");

        public Task<IReadOnlyList<KeyValuePair<string, string>>> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KeyValuePair<string, string>>>([]);

        private Task _Record(string operation)
        {
            Operations.Add(operation);

            return Task.CompletedTask;
        }
    }
}
