// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Coordination;
using Headless.Jobs.Internal;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.Registration;

/// <summary>
/// <c>DisableStartupInitialization()</c> drops the startup work that touches the store, keeps the post-commit worker
/// able to drain, and is refused while background services would dispatch without the activation drain.
/// </summary>
public sealed class JobsStartupInitializationTests : TestBase
{
    [Fact]
    public void should_register_the_initializer_by_default()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs => jobs.DisableBackgroundServices());

        // then
        _HostedServiceTypes(services).Should().Contain(typeof(JobsInitializationHostedService));
    }

    [Fact]
    public async Task should_skip_the_initializer_and_open_the_activation_barrier_when_disabled()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
            jobs.DisableBackgroundServices().DisableStartupInitialization()
        );

        // then
        _HostedServiceTypes(services).Should().NotContain(typeof(JobsInitializationHostedService));
        await using var provider = services.BuildServiceProvider();
        var activation = provider.GetRequiredService<JobsActivationBarrier>().WaitAsync(AbortToken);
        activation.IsCompletedSuccessfully.Should().BeTrue();
        (await activation).Should().BeNull();
        provider
            .GetServices<IHostedService>()
            .Should()
            .ContainSingle(x => x is JobsPostCommitSignalService, "the commit callback still hands it signals");
    }

    [Fact]
    public void should_reject_disabling_startup_initialization_while_background_services_run()
    {
        // given
        var services = new ServiceCollection();

        // when
        var act = () =>
            services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs => jobs.DisableStartupInitialization());

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*requires DisableBackgroundServices()*");
    }

    [Fact]
    public void should_register_the_coordination_startup_gate_on_the_durable_path_by_default()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<INodeMembership>());

        // when
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
            jobs.DisableBackgroundServices().UseEntityFramework()
        );

        // then
        var hosted = _HostedServiceTypes(services);
        hosted.Should().Contain(typeof(JobsCoordinationStartupGate));
        hosted.Should().Contain(x => _IsRecoveryBridge(x));
    }

    [Fact]
    public void should_skip_the_coordination_startup_gate_on_the_durable_path_when_disabled()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<INodeMembership>());

        // when
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
            jobs.DisableBackgroundServices().DisableStartupInitialization().UseEntityFramework()
        );

        // then
        var hosted = _HostedServiceTypes(services);
        hosted.Should().NotContain(typeof(JobsCoordinationStartupGate));
        hosted.Should().NotContain(typeof(JobsInitializationHostedService));
        hosted.Should().Contain(x => _IsRecoveryBridge(x), "dead-owner recovery is not startup work");
    }

    [Fact]
    public async Task should_fail_startup_on_a_duplicate_job_registration_when_initialization_is_disabled()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
            jobs.DisableBackgroundServices()
                .DisableStartupInitialization()
                .AddModule<BillingJobsModule>()
                .AddModule<RivalCloseDayJobsModule>()
        );
        await using var provider = services.BuildServiceProvider();
        var validator = provider
            .GetServices<IHeadlessStartupValidator>()
            .OfType<JobsCatalogStartupValidator>()
            .Single();

        // when
        var act = () => validator.ValidateAsync(AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*'{TestJobs.BillingCloseDay}'*");
    }

    [Fact]
    public void should_reject_a_disabled_membership_heartbeat_while_background_services_claim()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessCoordination(setup => setup.DisableMembershipHeartbeat().UsePostgreSql(_ConnectionString));

        // when
        var act = () => services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs => jobs.UseEntityFramework());

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*DisableMembershipHeartbeat()*DisableBackgroundServices()*");
    }

    [Fact]
    public void should_accept_a_disabled_membership_heartbeat_on_a_host_that_never_claims()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessCoordination(setup => setup.DisableMembershipHeartbeat().UsePostgreSql(_ConnectionString));

        // when
        var act = () =>
            services.AddHeadlessJobs<TimeJobEntity, CronJobEntity>(jobs =>
                jobs.DisableBackgroundServices().UseEntityFramework()
            );

        // then
        act.Should().NotThrow();
    }

    [Fact]
    public void should_reach_the_coordination_setup_builder_through_the_application_context_overload()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationContext>(db => db.UseNpgsql(_ConnectionString));

        // when
        services.AddHeadlessJobs(jobs =>
            jobs.DisableBackgroundServices()
                .UsePostgreSql<ApplicationContext>(coordination =>
                    coordination.Configure(options => options.ClusterName = "tests").DisableMembershipHeartbeat()
                )
        );

        // then
        // Coordination's types are internal to it, so they are matched by name.
        services.Should().NotContain(x => x.ServiceType.Name == "MembershipHeartbeatBackgroundService");
        services.Should().Contain(x => x.ServiceType.Name == "MembershipService");
    }

    [Fact]
    public void should_reject_a_disabled_membership_heartbeat_through_the_application_context_overload_when_claiming()
    {
        // given
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationContext>(db => db.UseNpgsql(_ConnectionString));

        // when
        var act = () =>
            services.AddHeadlessJobs(jobs =>
                jobs.UsePostgreSql<ApplicationContext>(coordination => coordination.DisableMembershipHeartbeat())
            );

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*DisableMembershipHeartbeat()*");
    }

    private const string _ConnectionString = "Host=localhost;Database=jobs;Username=jobs;Password=jobs";

    private sealed class ApplicationContext(DbContextOptions<ApplicationContext> options) : DbContext(options);

    // The bridge type is internal to Headless.Coordination, so it is matched by name.
    private static bool _IsRecoveryBridge(Type? type)
    {
        return type is { IsGenericType: true }
            && type.GetGenericTypeDefinition().Name.StartsWith("DeadOwnerRecoveryBridge", StringComparison.Ordinal)
            && type.GetGenericArguments()[0] == typeof(JobsDeadOwnerReclaimer);
    }

    private static List<Type?> _HostedServiceTypes(IServiceCollection services)
    {
        return [.. services.Where(x => x.ServiceType == typeof(IHostedService)).Select(x => x.ImplementationType)];
    }
}
