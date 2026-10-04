// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using GeneratedFixture = Headless.Jobs.GeneratedDiscoveryFixture;

namespace Tests;

/// <summary>
/// Drives jobs compiled by the real source generator: scheduling finds a job from its argument type or its class, and
/// the generated invoker builds the class from the run's scope, passes the stored argument, and disposes the instance.
/// </summary>
[Collection<JobsHelperCollection>]
public sealed class GeneratedJobInvocationTests : TestBase
{
    [Fact]
    public async Task should_enqueue_a_job_by_its_argument_type_and_run_it_with_the_stored_argument()
    {
        // given
        await using var provider = _CreateProvider();
        var probe = provider.GetRequiredService<GeneratedFixture.DiscoveryProbe>();
        var request = new GeneratedFixture.DiscoveryRequest("invoice-42");

        // when
        var id = await _Scheduler(provider).EnqueueAsync(request, AbortToken);
        await _RunAsync(provider, GeneratedFixture.DiscoveryJobs.FunctionName, id);

        // then
        var stored = await _Persistence(provider).GetTimeJobByIdAsync(id, AbortToken);
        stored!.Function.Should().Be(GeneratedFixture.DiscoveryJobs.FunctionName);
        probe.Executions.Should().ContainSingle().Which.Should().Be(request);
    }

    [Fact]
    public async Task should_enqueue_a_job_without_arguments_by_its_class_and_dispose_it_after_the_run()
    {
        // given
        await using var provider = _CreateProvider();
        var probe = provider.GetRequiredService<GeneratedFixture.DiscoveryProbe>();

        // when
        var id = await _Scheduler(provider).EnqueueAsync<GeneratedFixture.DiscoveryCloseDay>(AbortToken);
        await _RunAsync(provider, GeneratedFixture.DiscoveryCloseDay.FunctionName, id);

        // then
        var stored = await _Persistence(provider).GetTimeJobByIdAsync(id, AbortToken);
        stored!.Function.Should().Be(GeneratedFixture.DiscoveryCloseDay.FunctionName);
        stored.Request.Should().BeNull();
        probe.Executions.Should().ContainSingle().Which.Should().BeOfType<GeneratedFixture.DiscoveryCloseDay>();
        probe.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task should_record_the_job_class_and_attribute_knobs_on_the_registration()
    {
        // given
        await using var provider = _CreateProvider();

        // when
        var registry = provider.GetRequiredService<JobFunctionRegistry>();

        // then
        var typed = registry.Functions[GeneratedFixture.DiscoveryJobs.FunctionName];
        typed.JobType.Should().Be<GeneratedFixture.DiscoveryJobs>();
        typed.Priority.Should().Be(JobPriority.High);
        typed.MaxConcurrency.Should().Be(2);
        registry
            .DescriptorsByJobType[typeof(GeneratedFixture.DiscoveryCloseDay)]
            .FunctionName.Should()
            .Be(GeneratedFixture.DiscoveryCloseDay.FunctionName);
        registry.DescriptorsByJobType.Should().ContainKey(typeof(GeneratedFixture.DiscoveryJobs));
    }

    private static ServiceProvider _CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<GeneratedFixture.DiscoveryProbe>();
        services.AddSingleton<GeneratedFixture.DiscoveryScheduleMiddleware>();
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            options.AddModule<GeneratedFixture.JobsModule>();
        });
        return services.BuildServiceProvider();
    }

    private static IJobScheduler _Scheduler(IServiceProvider provider) => provider.GetRequiredService<IJobScheduler>();

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Persistence(IServiceProvider provider) =>
        provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

    private static async Task _RunAsync(ServiceProvider provider, string function, Guid id)
    {
        var registry = provider.GetRequiredService<JobFunctionRegistry>();
        await using var scope = provider.CreateAsyncScope();
        var context = new JobContext
        {
            Id = id,
            Type = JobType.TimeJob,
            FunctionName = function,
        };
        context.SetServiceScope(scope);

        await registry.Functions[function].Delegate(scope.ServiceProvider, context, AbortToken);
    }
}
