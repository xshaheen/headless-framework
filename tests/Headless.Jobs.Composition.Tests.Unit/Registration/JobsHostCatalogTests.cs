// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GeneratedFixture = Headless.Jobs.GeneratedDiscoveryFixture;
using MiddlewareFixture = Headless.Jobs.DiscoveryFixture;

namespace Tests.Registration;

/// <summary>
/// Each host builds and freezes its own job catalog from the modules it adds, so generated registrations never reach
/// process-wide state.
/// </summary>
public sealed class JobsHostCatalogTests : TestBase
{
    [Fact]
    public async Task should_freeze_generated_functions_and_middleware_from_an_added_module()
    {
        // given
        const string functionName = GeneratedFixture.DiscoveryJobs.FunctionName;
        await using var host = _Host(options => options.AddModule<GeneratedFixture.JobsModule>());

        // when
        var registry = host.GetRequiredService<JobFunctionRegistry>();

        // then
        registry.Functions.Should().ContainKey(functionName);
        registry.Descriptors.Should().ContainKey(functionName);
        registry.RequestTypes[functionName].Item2.Should().Be<GeneratedFixture.DiscoveryRequest>();

        await using var services = new ServiceCollection()
            .AddSingleton<GeneratedFixture.DiscoveryScheduleMiddleware>()
            .BuildServiceProvider();
        var invocationsBefore = GeneratedFixture.DiscoveryScheduleMiddleware.InvocationCount;
        var nextCalled = false;
        await registry.Middleware.DispatchScheduleAsync(
            new JobScheduleContext(registry.Descriptors[functionName], new TimeJobEntity(), services),
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AbortToken
        );

        nextCalled.Should().BeTrue();
        GeneratedFixture.DiscoveryScheduleMiddleware.InvocationCount.Should().Be(invocationsBefore + 1);
    }

    [Fact]
    public async Task should_register_nothing_from_a_referenced_assembly_whose_module_is_not_added()
    {
        // given: the fixture assembly is referenced and loaded; loading alone must register nothing.
        _ = typeof(GeneratedFixture.DiscoveryJobs);
        await using var host = _Host();

        // when
        var registry = host.GetRequiredService<JobFunctionRegistry>();

        // then
        registry.Functions.Should().NotContainKey(GeneratedFixture.DiscoveryJobs.FunctionName);
    }

    [Fact]
    public async Task should_give_each_host_in_one_process_its_own_modules()
    {
        // given
        await using var billingHost = _Host(options => options.AddModule<BillingJobsModule>());
        await using var ordersHost = _Host(options => options.AddModule<OrdersJobsModule>());

        // when
        var billing = billingHost.GetRequiredService<JobFunctionRegistry>();
        var orders = ordersHost.GetRequiredService<JobFunctionRegistry>();

        // then
        billing.Functions.Keys.Should().BeEquivalentTo(TestJobs.BillingCloseDay, TestJobs.BillingSendInvoice);
        orders.Functions.Keys.Should().BeEquivalentTo(TestJobs.OrdersShip);
    }

    [Fact]
    public async Task should_register_a_middleware_only_module_once_when_it_is_added_repeatedly()
    {
        // given
        await using var host = _Host(options =>
        {
            options.AddModule<MiddlewareFixture.JobsModule>();
            options.AddModule<MiddlewareFixture.JobsModule>();
        });
        var registry = host.GetRequiredService<JobFunctionRegistry>();
        await using var services = new ServiceCollection()
            .AddSingleton<MiddlewareFixture.DiscoveryScheduleMiddleware>()
            .BuildServiceProvider();
        var invocationsBefore = MiddlewareFixture.DiscoveryScheduleMiddleware.InvocationCount;
        var nextCalled = false;

        // when
        await registry.Middleware.DispatchScheduleAsync(
            new JobScheduleContext(_Descriptor("fixture", null), new TimeJobEntity(), services),
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AbortToken
        );

        // then
        nextCalled.Should().BeTrue();
        MiddlewareFixture.DiscoveryScheduleMiddleware.InvocationCount.Should().Be(invocationsBefore + 1);
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

        build.Should().Throw<InvalidOperationException>().WithMessage($"*{typeof(FirstRequest).FullName}*");
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

        build.Should().Throw<InvalidOperationException>().WithMessage("*'duplicate'*");
    }

    private static ServiceProvider _Host(Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices();
            configure?.Invoke(options);
        });
        return services.BuildServiceProvider();
    }

    private static KeyValuePair<string, JobFunctionRegistration> _Function(string name, string cronExpression = "")
    {
        return new(
            name,
            new JobFunctionRegistration
            {
                CronExpression = cronExpression,
                Priority = JobPriority.Normal,
                Delegate = (_, _, _) => Task.CompletedTask,
                MaxConcurrency = 0,
            }
        );
    }

    private static JobFunctionDescriptor _Descriptor(string name, Type? requestType, string cronExpression = "")
    {
        return new(name, requestType, cronExpression, JobPriority.Normal, 0);
    }

    private sealed record FirstRequest;

    private sealed record SecondRequest;
}
