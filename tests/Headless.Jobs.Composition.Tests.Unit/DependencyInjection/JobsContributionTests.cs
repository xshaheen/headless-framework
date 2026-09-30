// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GeneratedFixture = Headless.Jobs.GeneratedDiscoveryFixture;
using MiddlewareFixture = Headless.Jobs.DiscoveryFixture;

namespace Tests.DependencyInjection;

[Collection<JobsHelperCollection>]
public sealed class JobsContributionTests : TestBase
{
    public JobsContributionTests() => JobFunctionProvider.ResetForTests(discoveryComplete: false);

    protected override ValueTask DisposeAsyncCore()
    {
        JobFunctionProvider.ResetForTests();
        return base.DisposeAsyncCore();
    }

    [Fact]
    public void should_record_each_contribution_as_an_immutable_module_descriptor()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.ConfigureJobs(jobs =>
            jobs.AddModule<GeneratedFixture.JobsModule>().AddModule<MiddlewareFixture.JobsModule>()
        );

        // then
        services
            .Where(descriptor => descriptor.ServiceType == typeof(JobsModuleContribution))
            .Select(descriptor => ((JobsModuleContribution)descriptor.ImplementationInstance!).ModuleType)
            .Should()
            .Equal(typeof(GeneratedFixture.JobsModule), typeof(MiddlewareFixture.JobsModule));
    }

    [Fact]
    public void should_register_a_module_contributed_before_add_headless_jobs()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());

        // when
        services.AddHeadlessJobs();

        // then
        JobFunctionProvider.JobFunctions.Should().ContainKey(GeneratedFixture.DiscoveryJobs.FunctionName);
    }

    [Fact]
    public async Task should_resolve_the_host_registry_when_every_contributed_module_joined_the_catalog()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());
        services.AddHeadlessJobs();
        await using var provider = services.BuildServiceProvider();

        // when
        var registry = provider.GetRequiredService<JobFunctionRegistry>();

        // then
        registry.Functions.Should().ContainKey(GeneratedFixture.DiscoveryJobs.FunctionName);
    }

    [Fact]
    public void should_merge_identical_contributions_for_one_module()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());

        // when
        var add = () => services.AddHeadlessJobs(options => options.AddModule<GeneratedFixture.JobsModule>());

        // then
        add.Should().NotThrow();
        JobFunctionProvider.JobFunctions.Keys.Should().Equal(GeneratedFixture.DiscoveryJobs.FunctionName);
    }

    [Fact]
    public async Task should_keep_contributions_inert_when_the_host_never_adds_jobs()
    {
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());

        // when
        using var host = builder.Build();
        await host.StartAsync(AbortToken);
        await host.StopAsync(AbortToken);
        new ServiceCollection().AddHeadlessJobs();

        // then
        host.Services.GetService<IJobScheduler>().Should().BeNull();
        JobFunctionProvider.JobFunctions.Should().NotContainKey(GeneratedFixture.DiscoveryJobs.FunctionName);
    }

    [Fact]
    public async Task should_fail_startup_instead_of_dropping_a_module_contributed_after_the_catalog_closed()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessJobs();
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());
        await using var provider = services.BuildServiceProvider();

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*'{typeof(GeneratedFixture.JobsModule).FullName}'*ConfigureJobs*");
    }

    [Fact]
    public void should_reject_a_null_contribution()
    {
        var services = new ServiceCollection();

        var configure = () => services.ConfigureJobs(null!);

        configure.Should().Throw<ArgumentNullException>();
    }
}
