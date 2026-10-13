// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Text.Json;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class JobsHostDescriptionTests : TestBase
{
    private const string _Cleanup = "cleanup";
    private const string _Reports = "reports.daily";
    private const string _Notify = "notify";

    [Fact]
    public async Task should_print_the_cron_expression_resolved_from_configuration()
    {
        await using var provider = _Provider(("Jobs:Cleanup", "0 0 3 * * *"));

        var cleanup = _Function(_Describe(provider), _Cleanup);

        cleanup.GetProperty("kind").GetString().Should().Be("cron");
        cleanup.GetProperty("cronExpression").GetString().Should().Be("0 0 3 * * *");
    }

    [Fact]
    public async Task should_print_concurrency_tuned_from_configuration()
    {
        await using var provider = _Provider(
            ("Jobs:Cleanup", "0 0 3 * * *"),
            ($"Headless:Jobs:Jobs:{_Cleanup}:Concurrency", "3"),
            ($"Headless:Jobs:Jobs:{_Cleanup}:ClusterConcurrency", "2"),
            ($"Headless:Jobs:Jobs:{_Cleanup}:Priority", "LongRunning"),
            ($"Headless:Jobs:Jobs:{_Cleanup}:FailurePolicy:DelayedRetries", "4"),
            ($"Headless:Jobs:Jobs:{_Cleanup}:FailurePolicy:DelayedInitialDelay", "00:00:05"),
            ($"Headless:Jobs:Jobs:{_Cleanup}:FailurePolicy:DelayedMaxDelay", "00:01:00")
        );

        var cleanup = _Function(_Describe(provider), _Cleanup);

        cleanup.GetProperty("maxConcurrency").GetInt32().Should().Be(3);
        cleanup.GetProperty("clusterMaxConcurrency").GetInt32().Should().Be(2);
        cleanup.GetProperty("priority").GetString().Should().Be("long-running");
        var failurePolicy = cleanup.GetProperty("failurePolicy");
        failurePolicy.GetProperty("immediateRetries").GetInt32().Should().Be(0);
        failurePolicy.GetProperty("delayedRetries").GetInt32().Should().Be(4);
        failurePolicy.GetProperty("delayedInitialDelaySeconds").GetDouble().Should().Be(5);
        failurePolicy.GetProperty("delayedMaxDelaySeconds").GetDouble().Should().Be(60);
    }

    [Fact]
    public async Task should_print_registered_not_run_here_for_a_function_outside_run_only()
    {
        await using var provider = _Provider(options => options.RunOnly(_Cleanup), ("Jobs:Cleanup", "0 0 3 * * *"));

        var section = _Describe(provider);

        _Function(section, _Cleanup).GetProperty("runState").GetString().Should().Be("runs-here");
        _Function(section, _Notify).GetProperty("runState").GetString().Should().Be("registered-not-run-here");
        _Function(section, _Reports).GetProperty("runState").GetString().Should().Be("registered-not-run-here");
    }

    [Fact]
    public async Task should_print_registered_not_run_here_when_the_host_runs_no_background_services()
    {
        await using var provider = _Provider(
            options => options.DisableBackgroundServices(),
            ("Jobs:Cleanup", "0 0 3 * * *")
        );

        var section = _Describe(provider);

        _Function(section, _Cleanup).GetProperty("runState").GetString().Should().Be("registered-not-run-here");
    }

    [Fact]
    public async Task should_print_a_time_job_without_cron_settings()
    {
        await using var provider = _Provider(("Jobs:Cleanup", "0 0 3 * * *"));

        var notify = _Function(_Describe(provider), _Notify);

        notify.GetProperty("kind").GetString().Should().Be("time");
        notify.TryGetProperty("cronExpression", out _).Should().BeFalse();
        notify.TryGetProperty("missedRunPolicy", out _).Should().BeFalse();
        notify.TryGetProperty("overlapPolicy", out _).Should().BeFalse();
        notify.GetProperty("contractVersion").GetString().Should().Be("2");
        notify.GetProperty("priority").GetString().Should().Be("high");
        notify.GetProperty("runState").GetString().Should().Be("runs-here");
    }

    [Fact]
    public async Task should_print_declared_cron_policies_and_scheduler_defaults_for_undeclared_ones()
    {
        await using var provider = _Provider(
            options => options.ConfigureScheduler(scheduler => scheduler.DefaultOverlapPolicy = CronOverlapPolicy.Skip),
            ("Jobs:Cleanup", "0 0 3 * * *")
        );

        var section = _Describe(provider);

        var cleanup = _Function(section, _Cleanup);
        cleanup.GetProperty("missedRunPolicy").GetString().Should().Be("skip");
        cleanup.GetProperty("missedRunGraceSeconds").GetInt32().Should().Be(120);
        cleanup.GetProperty("overlapPolicy").GetString().Should().Be("skip");

        var reports = _Function(section, _Reports);
        reports.GetProperty("missedRunPolicy").GetString().Should().Be("coalesce");
        reports.GetProperty("missedRunGraceSeconds").GetInt32().Should().Be(60);
        reports.GetProperty("overlapPolicy").GetString().Should().Be("skip");
        reports.GetProperty("contractVersion").GetString().Should().Be("1");
    }

    [Fact]
    public async Task should_list_functions_in_ordinal_order_without_infrastructure_settings()
    {
        await using var provider = _Provider(
            ("Jobs:Cleanup", "0 0 3 * * *"),
            ("ConnectionStrings:Jobs", "Host=db;Password=secret")
        );

        var section = _Describe(provider);

        section
            .GetProperty("functions")
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .Should()
            .Equal(_Cleanup, _Notify, _Reports);
        section.GetRawText().Should().NotContain("secret");
    }

    private static JsonElement _Describe(IServiceProvider provider)
    {
        var contributor = provider
            .GetServices<IHostDescriptionContributor>()
            .Should()
            .ContainSingle(x => x.SectionName == "jobs")
            .Subject;

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            contributor.WriteSection(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static JsonElement _Function(JsonElement section, string name) =>
        section
            .GetProperty("functions")
            .EnumerateArray()
            .Should()
            .ContainSingle(x => x.GetProperty("name").GetString() == name)
            .Subject;

    private static ServiceProvider _Provider(params (string Key, string Value)[] settings) =>
        _Provider(options: null, settings);

    private static ServiceProvider _Provider(
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? options,
        params (string Key, string Value)[] settings
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHeadlessJobs(builder =>
        {
            builder.AddModule<DescribedModule>();
            options?.Invoke(builder);
        });

        return services.BuildServiceProvider();
    }

    private sealed class DescribedModule : IJobsModule
    {
        private DescribedModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [_Cleanup] = new()
                    {
                        CronExpression = "%Jobs:Cleanup",
                        Priority = JobPriority.Normal,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                        OnMissedRun = MissedRunPolicy.Skip,
                        MissedRunGraceSeconds = 120,
                    },
                    [_Reports] = new()
                    {
                        CronExpression = "0 0 6 * * *",
                        Priority = JobPriority.Low,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 1,
                    },
                    [_Notify] = new()
                    {
                        CronExpression = string.Empty,
                        Priority = JobPriority.High,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                    },
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_Notify] = new(_Notify, null, string.Empty, JobPriority.High, 0, contractVersion: "2"),
                }
            );
        }
    }
}
