// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class JobFunctionRegistryTests
{
    private const string _CronToken = "%Jobs:Registered:Cron%";

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
        services.AddHeadlessJobs(options => options.DisableBackgroundServices().AddModule<RegisteredModule>());
        return services.BuildServiceProvider();
    }

    private sealed class RegisteredModule : IJobsModule
    {
        private RegisteredModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
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
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    ["registered"] = new("registered", null, _CronToken, JobPriority.Normal, 0),
                }
            );
        }
    }
}
