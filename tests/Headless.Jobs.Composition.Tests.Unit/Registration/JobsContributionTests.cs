// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Interfaces;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GeneratedFixture = Headless.Jobs.GeneratedDiscoveryFixture;
using MiddlewareFixture = Headless.Jobs.DiscoveryFixture;

namespace Tests.Registration;

public sealed class JobsContributionTests : TestBase
{
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
    public async Task should_register_a_module_contributed_before_add_headless_jobs()
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
    public async Task should_register_a_module_contributed_after_add_headless_jobs()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessJobs();
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());
        await using var provider = services.BuildServiceProvider();

        // when
        var registry = provider.GetRequiredService<JobFunctionRegistry>();

        // then
        registry
            .Functions.Keys.Should()
            .BeEquivalentTo(
                GeneratedFixture.DiscoveryJobs.FunctionName,
                GeneratedFixture.DiscoveryCloseDay.FunctionName
            );
    }

    [Fact]
    public async Task should_merge_identical_contributions_for_one_module()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());
        services.ConfigureJobs(jobs => jobs.AddModule<GeneratedFixture.JobsModule>());
        services.AddHeadlessJobs(options => options.AddModule<GeneratedFixture.JobsModule>());
        await using var provider = services.BuildServiceProvider();

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve
            .Should()
            .NotThrow()
            .Which.Functions.Keys.Should()
            .BeEquivalentTo(
                GeneratedFixture.DiscoveryJobs.FunctionName,
                GeneratedFixture.DiscoveryCloseDay.FunctionName
            );
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

        // then
        host.Services.GetService<IJobScheduler>().Should().BeNull();
        host.Services.GetService<JobFunctionRegistry>().Should().BeNull();
    }

    [Fact]
    public async Task should_fail_startup_naming_both_modules_when_two_modules_declare_one_identity()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessJobs(options => options.AddModule<BillingJobsModule>());
        services.ConfigureJobs(jobs => jobs.AddModule<RivalCloseDayJobsModule>());
        await using var provider = services.BuildServiceProvider();

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*'{TestJobs.BillingCloseDay}'*'{typeof(BillingJobsModule).FullName}'*'{typeof(RivalCloseDayJobsModule).FullName}'*"
            );
    }

    [Fact]
    public async Task should_fail_startup_naming_both_modules_when_two_modules_take_one_argument_type()
    {
        // given
        var services = new ServiceCollection();
        services.ConfigureJobs(jobs => jobs.AddModule<BillingJobsModule>().AddModule<RivalInvoiceArgsJobsModule>());
        services.AddHeadlessJobs();
        await using var provider = services.BuildServiceProvider();

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"*'{typeof(InvoiceArgs).FullName}'*'{TestJobs.BillingSendInvoice}'*'{TestJobs.RivalSendInvoice}'*'{typeof(BillingJobsModule).FullName}'*'{typeof(RivalInvoiceArgsJobsModule).FullName}'*"
            );
    }

    [Fact]
    public void should_reject_a_null_contribution()
    {
        var services = new ServiceCollection();

        var configure = () => services.ConfigureJobs(null!);

        configure.Should().Throw<ArgumentNullException>();
    }
}
