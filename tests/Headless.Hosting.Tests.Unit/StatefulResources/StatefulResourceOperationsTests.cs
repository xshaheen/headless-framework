// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.StatefulResources;

public sealed class StatefulResourceOperationsTests : TestBase
{
    private readonly List<string> _log = [];

    [Fact]
    public async Task should_set_up_direct_registrations_then_source_resources_in_registration_order()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "orders", _log));
        services.AddSingleton<IStatefulResourceSource>(
            new FixedResourceSource([
                new RecordingResource("RabbitMQ", "a", _log),
                new RecordingResource("RabbitMQ", "b", _log),
            ])
        );
        services.AddHeadlessStatefulResource(_ => new RecordingResource("Blob container", "files", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.SetupResourcesAsync(AbortToken);

        // then
        _log.Should().Equal("setup orders", "setup files", "setup a", "setup b");
    }

    [Fact]
    public async Task should_set_up_a_dependency_before_the_resource_that_needs_it_and_tear_it_down_after()
    {
        // given queues stored in a database registered after them
        var services = new ServiceCollection();
        services.AddHeadlessStatefulResource(_ => new DependentResource("Queue", "jobs", _log, "db"));
        services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "db", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.SetupResourcesAsync(AbortToken);
        await provider.TeardownResourcesAsync(AbortToken);

        // then
        _log.Should().Equal("setup db", "setup jobs", "teardown jobs", "teardown db");
    }

    [Fact]
    public async Task should_name_the_cycle_when_two_resources_depend_on_each_other()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessStatefulResource(_ => new DependentResource("Queue", "a", _log, "b"));
        services.AddHeadlessStatefulResource(_ => new DependentResource("Queue", "b", _log, "a"));
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.SetupResourcesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Queue a -> Queue b -> Queue a*");
        _log.Should().BeEmpty();
    }

    [Fact]
    public async Task should_run_every_resource_then_throw_one_exception_naming_each_failure()
    {
        // given
        var first = new RecordingResource("RabbitMQ", "orders", _log) { FailureMessage = "ACCESS_REFUSED" };
        first.Failing.Add("setup");
        var last = new RecordingResource("Schema", "billing", _log) { FailureMessage = "connection refused" };
        last.Failing.Add("setup");
        var services = new ServiceCollection();
        services.AddHeadlessStatefulResource(_ => first);
        services.AddHeadlessStatefulResource(_ => new RecordingResource("Schema", "ok", _log));
        services.AddHeadlessStatefulResource(_ => last);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.SetupResourcesAsync(AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<StatefulResourceException>();
        thrown.WithMessage("*2 resource(s)*RabbitMQ orders: ACCESS_REFUSED*Schema billing: connection refused*");
        thrown.Which.Failures.Should().HaveCount(2);
        _log.Should().Equal("setup orders", "setup ok", "setup billing");
    }

    [Fact]
    public async Task should_skip_a_resource_that_cannot_clear_its_data_without_failing()
    {
        // given
        var schema = new RecordingResource("Schema", "orders", _log);
        schema.Unsupported.Add("clear");
        var services = new ServiceCollection();
        services.AddHeadlessStatefulResource(_ => schema);
        services.AddHeadlessStatefulResource(_ => new RecordingResource("RabbitMQ", "orders.queue", _log));
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.ResetResourceStateAsync(AbortToken);

        // then
        _log.Should().Equal("clear orders", "clear orders.queue");
    }

    [Fact]
    public async Task should_register_a_source_type_once_however_often_a_feature_adds_it()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessStatefulResourceSource<SingleQueueSource>();
        services.AddHeadlessStatefulResourceSource<SingleQueueSource>();
        await using var provider = services.BuildServiceProvider();

        // when
        await provider.SetupResourcesAsync(AbortToken);

        // then
        provider.GetServices<IStatefulResourceSource>().Should().ContainSingle();
    }

    [Fact]
    public async Task should_do_nothing_when_no_resource_is_registered()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        var act = () => provider.SetupResourcesAsync(AbortToken);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_fail_a_resource_whose_driver_throws_not_supported_instead_of_skipping_it()
    {
        // given — only StatefulResourceUnsupportedException marks an operation as unsupported on purpose
        var services = new ServiceCollection();
        var queues = new RecordingResource("Broker", "queues", _log);
        queues.DriverNotSupported.Add("setup");
        services.AddHeadlessStatefulResource(_ => queues);
        await using var provider = services.BuildServiceProvider();

        // when
        var act = () => provider.SetupResourcesAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<StatefulResourceException>().WithMessage("*Specified method is not supported*");
    }

    [Fact]
    public async Task should_report_the_latest_outcome_after_a_host_restart()
    {
        // given — a resource that fails the first start and succeeds the second
        var flaky = new RecordingResource("Schema", "orders", _log);
        flaky.Failing.Add("setup");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessStatefulResource(_ => flaky);
        services.AddHeadlessResourceSetupOnStartup();
        await using var provider = services.BuildServiceProvider();
        var startup = provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>().Single();
        var initializer = (IInitializer)startup;

        // when
        await FluentActions
            .Awaiting(() => startup.StartingAsync(AbortToken))
            .Should()
            .ThrowAsync<StatefulResourceException>();
        flaky.Failing.Clear();
        await startup.StartingAsync(AbortToken);

        // then
        initializer.IsInitialized.Should().BeTrue();
        await initializer.WaitForInitializationAsync(AbortToken);
    }

    private sealed class SingleQueueSource : IStatefulResourceSource
    {
        public Task<IReadOnlyList<IStatefulResource>> GetResourcesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IStatefulResource>>([new RecordingResource("RabbitMQ", "q", [])]);
    }
}
