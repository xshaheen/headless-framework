// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tests.HealthChecks;

/// <summary>
/// A Jobs host's readiness reports whether it can run jobs: activation, shutdown, the scheduler loop, and the EF store.
/// </summary>
public sealed class JobsHealthCheckTests : TestBase
{
    private const string _JobsCheck = "jobs";
    private const string _SchedulerCheck = "jobs-scheduler";
    private static readonly string _StorageCheck = "jobs-storage-" + typeof(ProbeJobsDbContext).FullName;

    [Fact]
    public async Task should_pass_the_check_command_of_a_host_that_was_never_started()
    {
        // given - the lifecycle checks are unhealthy before start, so check passes only because it skips them
        var host = _BuildHost(_ => { });

        // when
        var exitCode = await host.RunHeadlessAsync(["check"], AbortToken);

        // then
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task should_report_jobs_unhealthy_before_activation_and_healthy_after()
    {
        // given
        using var host = _BuildHost(_ => { });

        // when
        var beforeStart = await _CheckAsync(host.Services, _JobsCheck);
        await host.StartAsync(AbortToken);
        var afterStart = await _CheckAsync(host.Services, _JobsCheck);

        // then
        beforeStart.Status.Should().Be(HealthStatus.Unhealthy);
        afterStart.Status.Should().Be(HealthStatus.Healthy);

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_jobs_unhealthy_once_the_host_starts_shutting_down()
    {
        // given - a hosted service registered after Jobs stops first, so it holds shutdown open before the Jobs drain
        var blocker = new BlockingStopService();
        using var host = _BuildHost(_ => { }, services => services.AddHostedService(_ => blocker));
        await host.StartAsync(AbortToken);
        (await _CheckAsync(host.Services, _JobsCheck)).Status.Should().Be(HealthStatus.Healthy);

        // when
        var stopTask = host.StopAsync(AbortToken);
        await blocker.StopEntered.Task.WaitAsync(AbortToken);
        var duringShutdown = await _CheckAsync(host.Services, _JobsCheck);
        blocker.ReleaseStop.TrySetResult();
        await stopTask;

        // then
        duringShutdown.Status.Should().Be(HealthStatus.Unhealthy);
        duringShutdown.Description.Should().Be("The 'jobs' dependency probe failed.");
    }

    [Fact]
    public async Task should_report_scheduler_degraded_while_stopped_and_healthy_after_start()
    {
        // given
        using var host = _BuildHost(_ => { });
        await host.StartAsync(AbortToken);
        var scheduler = host.Services.GetRequiredService<IJobsHostScheduler>();
        var running = await _CheckAsync(host.Services, _SchedulerCheck);

        // when
        await scheduler.StopAsync(AbortToken);
        var stopped = await _CheckAsync(host.Services, _SchedulerCheck);
        await scheduler.StartAsync(AbortToken);
        var restarted = await _CheckAsync(host.Services, _SchedulerCheck);

        // then
        running.Status.Should().Be(HealthStatus.Healthy);
        stopped.Status.Should().Be(HealthStatus.Degraded);
        restarted.Status.Should().Be(HealthStatus.Healthy);

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_report_scheduler_degraded_in_manual_start_mode_until_started()
    {
        // given
        using var host = _BuildHost(jobs =>
            jobs.ConfigureScheduler(scheduler => scheduler.StartMode = JobsStartMode.Manual)
        );
        await host.StartAsync(AbortToken);

        // when
        var beforeExplicitStart = await _CheckAsync(host.Services, _SchedulerCheck);
        await host.Services.GetRequiredService<IJobsHostScheduler>().StartAsync(AbortToken);
        var afterExplicitStart = await _CheckAsync(host.Services, _SchedulerCheck);

        // then
        beforeExplicitStart.Status.Should().Be(HealthStatus.Degraded);
        afterExplicitStart.Status.Should().Be(HealthStatus.Healthy);

        await host.StopAsync(AbortToken);
    }

    [Fact]
    public async Task should_register_no_jobs_check_when_background_services_are_disabled()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(jobs => jobs.DisableBackgroundServices());
        await using var provider = services.BuildServiceProvider();

        // when
        var names = _RegistrationNames(provider);

        // then
        names.Should().NotContain(name => name.StartsWith("jobs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_register_ready_checks_tagged_jobs_and_no_storage_check_for_the_in_memory_store()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs();
        await using var provider = services.BuildServiceProvider();

        // when
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        // then
        registrations
            .Where(r => r.Name.StartsWith("jobs", StringComparison.Ordinal))
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo([_JobsCheck, _SchedulerCheck]);

        foreach (var registration in registrations.Where(r => r.Name.StartsWith("jobs", StringComparison.Ordinal)))
        {
            registration
                .Tags.Should()
                .BeEquivalentTo([
                    HeadlessHealthCheckTags.Ready,
                    HeadlessHealthCheckTags.Headless,
                    HeadlessHealthCheckTags.Jobs,
                    HeadlessHealthCheckTags.HostState,
                ]);
        }
    }

    [Fact]
    public async Task should_drop_the_jobs_checks_through_remove_health_checks()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<INodeMembership>());
        services.RemoveHealthChecks(registration => registration.Tags.Contains(HeadlessHealthCheckTags.Jobs));
        services.AddHeadlessJobs(jobs =>
            jobs.UseEntityFramework(ef => ef.UseJobsDbContext<ProbeJobsDbContext>(db => db.UseSqlite(_InMemory)))
        );
        await using var provider = services.BuildServiceProvider();

        // when
        var names = _RegistrationNames(provider);

        // then
        names.Should().NotContain(name => name.StartsWith("jobs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task should_report_ef_storage_healthy_when_the_store_can_connect()
    {
        // given
        await using var provider = _EfProvider(_InMemory);

        // when
        var entry = await _CheckAsync(provider, _StorageCheck);

        // then
        entry.Status.Should().Be(HealthStatus.Healthy);
        _Registration(provider, _StorageCheck)
            .Tags.Should()
            .BeEquivalentTo([
                HeadlessHealthCheckTags.Ready,
                HeadlessHealthCheckTags.Headless,
                HeadlessHealthCheckTags.Jobs,
                HeadlessHealthCheckTags.Database,
            ]);
    }

    [Fact]
    public async Task should_report_ef_storage_unhealthy_when_the_store_cannot_connect()
    {
        // given - Mode=ReadOnly refuses to create the missing file, so the connection cannot open
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.db");
        await using var provider = _EfProvider($"Data Source={path};Mode=ReadOnly");

        // when
        var entry = await _CheckAsync(provider, _StorageCheck);

        // then
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Description.Should().Be($"The '{_StorageCheck}' dependency probe failed.");
        entry.Description.Should().NotContain(path);
    }

    private const string _InMemory = "Data Source=:memory:";

    private static ServiceProvider _EfProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<INodeMembership>());
        services.AddHeadlessJobs(jobs =>
            jobs.DisableBackgroundServices()
                .UseEntityFramework(ef => ef.UseJobsDbContext<ProbeJobsDbContext>(db => db.UseSqlite(connectionString)))
        );

        return services.BuildServiceProvider();
    }

    private static IHost _BuildHost(
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>> configureJobs,
        Action<IServiceCollection>? afterJobs = null
    )
    {
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                // Keeps the test from installing the process-wide Ctrl+C / ProcessExit handlers ConsoleLifetime adds.
                services.AddSingleton<IHostLifetime, NoopHostLifetime>();
                services.AddHeadlessJobs(configureJobs);
                afterJobs?.Invoke(services);
            })
            .Build();
    }

    private static async Task<HealthReportEntry> _CheckAsync(IServiceProvider services, string name)
    {
        var report = await services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => string.Equals(registration.Name, name, StringComparison.Ordinal),
                AbortToken
            );

        return report.Entries[name];
    }

    private static List<string> _RegistrationNames(IServiceProvider provider)
    {
        return
        [
            .. provider
                .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
                .Value.Registrations.Select(r => r.Name),
        ];
    }

    private static HealthCheckRegistration _Registration(IServiceProvider provider, string name)
    {
        return provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single(r => string.Equals(r.Name, name, StringComparison.Ordinal));
    }

    private sealed class ProbeJobsDbContext(DbContextOptions<ProbeJobsDbContext> options)
        : JobsDbContext<TimeJobEntity, CronJobEntity>(options);

    private sealed class BlockingStopService : IHostedService
    {
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            StopEntered.TrySetResult();
            await ReleaseStop.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class NoopHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
