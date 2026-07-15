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
    public JobFunctionProviderTests() => JobFunctionProvider.ResetForTests(discoveryComplete: false);

    public void Dispose() => JobFunctionProvider.ResetForTests();

    [Fact]
    public void should_reject_build_before_discovery_is_complete()
    {
        var build = JobFunctionProvider.Build;

        build
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Jobs discovery must complete before JobFunctionProvider.Build() can freeze the catalog.");
    }

    [Fact]
    public void should_freeze_registered_metadata_after_discovery_completes()
    {
        var descriptor = _Descriptor("registered", typeof(FirstRequest), "%Jobs:Registered:Cron");
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                [descriptor.FunctionName] = _Function(descriptor.FunctionName, descriptor.CronExpression).Value,
            }
        );
        JobFunctionProvider.RegisterRequestType(
            new Dictionary<string, (string, Type)>(StringComparer.Ordinal)
            {
                [descriptor.FunctionName] = (typeof(FirstRequest).FullName!, typeof(FirstRequest)),
            }
        );
        JobFunctionProvider.RegisterDescriptors(
            new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
            {
                [descriptor.FunctionName] = descriptor,
            }
        );

        JobFunctionProvider.MarkDiscoveryComplete();
        JobFunctionProvider.Build();

        JobFunctionProvider.JobFunctions.Should().ContainKey(descriptor.FunctionName);
        JobFunctionProvider.JobFunctionRequestTypes.Should().ContainKey(descriptor.FunctionName);
        JobFunctionProvider.JobFunctionDescriptors[descriptor.FunctionName].Should().BeSameAs(descriptor);
        JobFunctionProvider.JobFunctionDescriptorsByRequestType[typeof(FirstRequest)].Should().BeSameAs(descriptor);
        JobFunctionProvider
            .JobFunctionDescriptors[descriptor.FunctionName]
            .CronExpression.Should()
            .Be(descriptor.CronExpression);
    }

    [Fact]
    public void should_build_one_catalog_when_called_repeatedly_and_concurrently()
    {
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                ["registered"] = _Function("registered").Value,
            }
        );
        JobFunctionProvider.MarkDiscoveryComplete();

        var catalogs = new System.Collections.Concurrent.ConcurrentBag<
            FrozenDictionary<string, JobFunctionRegistration>
        >();
        Parallel.For(
            0,
            32,
            _ =>
            {
                JobFunctionProvider.Build();
                catalogs.Add(JobFunctionProvider.JobFunctions);
            }
        );

        var catalog = catalogs.First();
        catalogs.Should().OnlyContain(candidate => ReferenceEquals(candidate, catalog));
        catalog.Should().ContainSingle().Which.Key.Should().Be("registered");
    }

    [Fact]
    public async Task should_wait_for_overlapping_host_discovery_callbacks_before_freezing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var firstCanReturn = new ManualResetEventSlim();
        using var secondCanReturn = new ManualResetEventSlim();
        using var firstEntered = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();

        var firstHost = Task.Run(() =>
            new ServiceCollection().AddHeadlessJobs(options =>
            {
                JobFunctionProvider.RegisterFunctions(
                    new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                    {
                        ["first-host"] = _Function("first-host").Value,
                    }
                );
                firstEntered.Set();
                firstCanReturn.Wait(cancellationToken);
            })
        );
        firstEntered.Wait(cancellationToken);

        var secondHost = Task.Run(() =>
            new ServiceCollection().AddHeadlessJobs(options =>
            {
                JobFunctionProvider.RegisterFunctions(
                    new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                    {
                        ["second-host"] = _Function("second-host").Value,
                    }
                );
                secondEntered.Set();
                secondCanReturn.Wait(cancellationToken);
            })
        );
        secondEntered.Wait(cancellationToken);

        firstCanReturn.Set();
        await Task.Delay(50, cancellationToken);
        firstHost.IsCompleted.Should().BeFalse("the other configured discovery callback is still active");

        secondCanReturn.Set();
        await Task.WhenAll(firstHost, secondHost).WaitAsync(cancellationToken);

        JobFunctionProvider.JobFunctions.Keys.Should().BeEquivalentTo("first-host", "second-host");
    }

    [Fact]
    public void should_allow_nested_discovery_on_the_same_synchronous_callback()
    {
        var services = new ServiceCollection();

        services.AddHeadlessJobs(_ => services.AddHeadlessJobs());

        JobFunctionProvider.JobFunctions.Should().BeEmpty();
    }

    [Fact]
    public async Task should_freeze_generated_discovery_metadata_and_middleware_loaded_by_the_options_callback()
    {
        const string assemblyName = "Headless.Jobs.GeneratedDiscoveryFixture.dll";
        const string functionName = "tests.discovery.generated";
        const string requestTypeName = "Headless.Jobs.GeneratedDiscoveryFixture.DiscoveryRequest";
        const string middlewareTypeName = "Headless.Jobs.GeneratedDiscoveryFixture.DiscoveryScheduleMiddleware";
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
        });

        JobFunctionProvider.JobFunctions.Should().ContainKey(functionName);
        JobFunctionProvider.JobFunctionDescriptors.Should().ContainKey(functionName);
        JobFunctionProvider.JobFunctionRequestTypes[functionName].Item2.FullName.Should().Be(requestTypeName);

        var middlewareType = discoveredAssembly!.GetType(middlewareTypeName, throwOnError: true)!;
        var services = new ServiceCollection().AddSingleton(middlewareType).BuildServiceProvider();
        var descriptor = JobFunctionProvider.JobFunctionDescriptors[functionName];
        var nextCalled = false;
        await JobMiddlewareRegistry.DispatchScheduleAsync(
            new JobScheduleContext(descriptor, new TimeJobEntity(), services),
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

    [Fact]
    public void should_reject_registration_after_discovery_completes()
    {
        JobFunctionProvider.MarkDiscoveryComplete();

        var register = () =>
            JobFunctionProvider.RegisterFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            );

        register
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Jobs generated registration is closed after discovery completes.");
    }

    [Fact]
    public void should_reject_function_and_middleware_registration_after_freeze()
    {
        JobFunctionProvider.MarkDiscoveryComplete();
        JobFunctionProvider.Build();

        var registerFunction = () =>
            JobFunctionProvider.RegisterFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            );
        var registerMiddleware = () =>
            JobMiddlewareRegistry.RegisterSchedule(
                "Tests:Late",
                null,
                0,
                static (_, next, cancellationToken) => next(cancellationToken)
            );

        registerFunction
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Jobs generated registration is frozen after JobFunctionProvider.Build().");
        registerMiddleware
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Jobs generated registration is frozen after JobFunctionProvider.Build().");
    }

    [Fact]
    public void should_keep_duplicate_diagnostics_stable_across_failed_builds()
    {
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                ["zeta"] = _Function("zeta").Value,
                ["alpha"] = _Function("alpha").Value,
            }
        );
        JobFunctionProvider.RegisterFunctions(
            new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
            {
                ["alpha"] = _Function("alpha").Value,
                ["zeta"] = _Function("zeta").Value,
            }
        );
        JobFunctionProvider.MarkDiscoveryComplete();

        var build = JobFunctionProvider.Build;
        var first = build.Should().Throw<InvalidOperationException>().Which;
        var second = build.Should().Throw<InvalidOperationException>().Which;

        second.Message.Should().Be(first.Message);
        first
            .Message.IndexOf("'alpha'", StringComparison.Ordinal)
            .Should()
            .BeLessThan(first.Message.IndexOf("'zeta'", StringComparison.Ordinal));
    }

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
