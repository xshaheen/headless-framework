// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<JobsHelperCollection>]
public sealed class JobFunctionRegistryTests : IDisposable
{
    private const string _CronToken = "%Jobs:Registered:Cron%";

    public JobFunctionRegistryTests()
    {
        JobFunctionProvider.ResetForTests(discoveryComplete: false);
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                ["registered"] = new()
                {
                    CronExpression = _CronToken,
                    Priority = JobPriority.Normal,
                    Delegate = static (_, _, _) => Task.CompletedTask,
                    MaxConcurrency = 0,
                },
            }
        );
        JobFunctionProvider.RegisterDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                ["registered"] = new("registered", null, _CronToken, JobPriority.Normal, 0),
            }
        );
    }

    public void Dispose() => JobFunctionProvider.ResetForTests();

    [Fact]
    public void should_build_one_configuration_resolved_registry_per_host_without_clobbering()
    {
        using var hostA = _CreateHost("0 */5 * * * *");
        var registryA = hostA.GetRequiredService<JobFunctionRegistry>();

        using var hostB = _CreateHost("0 */10 * * * *");
        var registryB = hostB.GetRequiredService<JobFunctionRegistry>();

        registryA.Should().NotBeSameAs(registryB);
        registryA.Functions["registered"].CronExpression.Should().Be("0 */5 * * * *");
        registryA.Descriptors["registered"].CronExpression.Should().Be("0 */5 * * * *");
        registryB.Functions["registered"].CronExpression.Should().Be("0 */10 * * * *");
        registryB.Descriptors["registered"].CronExpression.Should().Be("0 */10 * * * *");
        JobFunctionProvider.JobFunctions["registered"].CronExpression.Should().Be(_CronToken);
        JobFunctionProvider.JobFunctionDescriptors["registered"].CronExpression.Should().Be(_CronToken);
    }

    private static ServiceProvider _CreateHost(string cronExpression)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Jobs:Registered:Cron"] = cronExpression }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHeadlessJobs(options => options.DisableBackgroundServices());
        return services.BuildServiceProvider();
    }
}
