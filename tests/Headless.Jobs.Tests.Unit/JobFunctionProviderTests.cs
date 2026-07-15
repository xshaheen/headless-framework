// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.Loader;
using Headless.Jobs;
using Headless.Jobs.Entities;
using Headless.Jobs.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[Collection<JobsHelperCollection>]
public sealed class JobFunctionProviderTests : IDisposable
{
    public JobFunctionProviderTests() => JobFunctionProvider.ResetForTests();

    public void Dispose() => JobFunctionProvider.ResetForTests();

    [Fact]
    public void should_build_name_and_request_type_descriptor_indexes()
    {
        var typed = _Descriptor("typed", typeof(FirstRequest), "%Jobs:Typed:Cron");
        var requestless = _Descriptor("requestless", null);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["Jobs:Typed:Cron"] = "0 */5 * * * *" }
            )
            .Build();

        var registry = JobFunctionRegistryBuilder.Build(
            [_Function("typed", "%Jobs:Typed:Cron"), _Function("requestless")],
            [new("typed", (typeof(FirstRequest).FullName!, typeof(FirstRequest)))],
            [new("typed", typed), new("requestless", requestless)],
            configuration
        );

        registry.DescriptorsByRequestType[typeof(FirstRequest)].Should().BeSameAs(registry.Descriptors["typed"]);
        registry.DescriptorsByRequestType.Should().NotContainKey(typeof(SecondRequest));
        registry.Descriptors["requestless"].RequestType.Should().BeNull();
        registry.Descriptors["typed"].CronExpression.Should().Be("0 */5 * * * *");
        registry.Functions["typed"].CronExpression.Should().Be(registry.Descriptors["typed"].CronExpression);
    }

    [Fact]
    public void should_derive_descriptors_for_assemblies_generated_by_older_versions()
    {
        var registry = JobFunctionRegistryBuilder.Build(
            [_Function("typed", "%Jobs:Typed:Cron"), _Function("requestless")],
            [new("typed", (typeof(FirstRequest).FullName!, typeof(FirstRequest)))],
            [],
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.Ordinal) { ["Jobs:Typed:Cron"] = "" }
                )
                .Build()
        );

        registry.Descriptors["typed"].RequestType.Should().Be<FirstRequest>();
        registry.Descriptors["typed"].CronExpression.Should().Be("%Jobs:Typed:Cron");
        registry.Descriptors["requestless"].RequestType.Should().BeNull();
        registry.DescriptorsByRequestType[typeof(FirstRequest)].Should().BeSameAs(registry.Descriptors["typed"]);
    }

    [Fact]
    public void should_report_all_collisions_in_stable_ordinal_order()
    {
        var functions = new[] { _Function("zeta"), _Function("alpha"), _Function("zeta"), _Function("alpha") };
        var requestTypes = new[]
        {
            new KeyValuePair<string, (string, Type)>("zeta", (typeof(SecondRequest).FullName!, typeof(SecondRequest))),
            new KeyValuePair<string, (string, Type)>("alpha", (typeof(FirstRequest).FullName!, typeof(FirstRequest))),
            new KeyValuePair<string, (string, Type)>(
                "other-zeta",
                (typeof(SecondRequest).FullName!, typeof(SecondRequest))
            ),
            new KeyValuePair<string, (string, Type)>(
                "other-alpha",
                (typeof(FirstRequest).FullName!, typeof(FirstRequest))
            ),
        };

        var buildForward = () => JobFunctionRegistryBuilder.Build(functions, requestTypes, []);
        var buildReversed = () =>
            JobFunctionRegistryBuilder.Build(
                functions.AsEnumerable().Reverse().ToArray(),
                requestTypes.AsEnumerable().Reverse().ToArray(),
                []
            );

        var forward = buildForward.Should().Throw<InvalidOperationException>().Which;
        var reversed = buildReversed.Should().Throw<InvalidOperationException>().Which;

        reversed.Message.Should().Be(forward.Message);
        forward
            .Message.IndexOf("'alpha'", StringComparison.Ordinal)
            .Should()
            .BeLessThan(forward.Message.IndexOf("'zeta'", StringComparison.Ordinal));
        forward.Message.Should().Contain(typeof(FirstRequest).FullName!);
        forward.Message.Should().Contain(typeof(SecondRequest).FullName!);
    }

    [Fact]
    public void should_reject_descriptor_only_collisions()
    {
        var descriptors = new[]
        {
            new KeyValuePair<string, JobFunctionDescriptor>("first", _Descriptor("first", typeof(FirstRequest))),
            new KeyValuePair<string, JobFunctionDescriptor>("second", _Descriptor("second", typeof(FirstRequest))),
        };

        var build = () => JobFunctionRegistryBuilder.Build([], [], descriptors);

        build
            .Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain(typeof(FirstRequest).FullName!);
    }

    [Fact]
    public void should_reject_duplicate_descriptor_function_names()
    {
        var descriptors = new[]
        {
            new KeyValuePair<string, JobFunctionDescriptor>("duplicate", _Descriptor("duplicate", null)),
            new KeyValuePair<string, JobFunctionDescriptor>("duplicate", _Descriptor("duplicate", null)),
        };

        var build = () => JobFunctionRegistryBuilder.Build([], [], descriptors);

        build.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("'duplicate'");
    }

    [Fact]
    public async Task should_load_middleware_only_discovery_once_before_registry_freeze()
    {
        const string assemblyName = "Headless.Jobs.DiscoveryFixture.dll";
        const string middlewareTypeName = "Headless.Jobs.DiscoveryFixture.DiscoveryScheduleMiddleware";
        Assembly? discoveredAssembly = null;
        AssemblyLoadContext
            .Default.Assemblies.Should()
            .NotContain(assembly =>
                string.Equals(
                    assembly.GetName().Name,
                    Path.GetFileNameWithoutExtension(assemblyName),
                    StringComparison.Ordinal
                )
            );

        new ServiceCollection().AddHeadlessJobs(options =>
        {
            discoveredAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(
                Path.Combine(AppContext.BaseDirectory, "fixtures", assemblyName)
            );
            options.AddJobsDiscovery([discoveredAssembly]);
            options.AddJobsDiscovery([discoveredAssembly]);
        });
        JobFunctionProvider.Build();

        var middlewareType = discoveredAssembly!.GetType(middlewareTypeName, throwOnError: true)!;
        await using var services = new ServiceCollection().AddSingleton(middlewareType).BuildServiceProvider();
        var nextCalled = false;
        await JobMiddlewareRegistry.DispatchScheduleAsync(
            new JobScheduleContext(_Descriptor("fixture", null), new TimeJobEntity(), services),
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        nextCalled.Should().BeTrue();
        middlewareType.GetProperty("InvocationCount")!.GetValue(null).Should().Be(1);
    }

    private static KeyValuePair<string, JobFunctionRegistration> _Function(string name, string cronExpression = "") =>
        new(
            name,
            new JobFunctionRegistration
            {
                CronExpression = cronExpression,
                Priority = JobPriority.Normal,
                Delegate = new JobFunctionDelegate((_, _, _) => Task.CompletedTask),
                MaxConcurrency = 0,
            }
        );

    private static JobFunctionDescriptor _Descriptor(string name, Type? requestType, string cronExpression = "") =>
        new(name, requestType, cronExpression, JobPriority.Normal, 0);

    private sealed record FirstRequest;

    private sealed record SecondRequest;
}
