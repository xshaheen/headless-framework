// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// The in-memory store reconciles one owner's seeded cron definitions the way the relational store does: matched by
/// id, deleting what the owner no longer declares, and never touching runtime rows or another owner's rows. The
/// host-level cases drive the real seeder over the recurring jobs an app declares on its Jobs options, and the startup
/// validation that rejects a declaration before any seed writes.
/// </summary>
public sealed class InMemorySeedReconcileTests : TestBase
{
    private sealed class FakeTimeJob : TimeJobEntity<FakeTimeJob>;

    private sealed class FakeCronJob : CronJobEntity;

    private const string _Owner = "billing-app";
    private const string _OtherOwner = "reports-app";
    private const string _Function = "seed.cleanup";
    private const string _SeededExpression = "0 */5 * * * *";
    private const string _RuntimeExpression = "0 0 * * * *";
    private static readonly DateTimeOffset _Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_delete_a_seeded_row_and_its_occurrences_when_the_next_seed_no_longer_declares_it()
    {
        var provider = _Create();
        await _SeedAsync(provider, _Owner, [_Function, "seed.kept"]);
        var removed = (await provider.GetCronJobsAsync(x => x.Function == _Function, AbortToken)).Single();
        await provider.InsertCronJobOccurrencesAsync(
            [
                new CronJobOccurrenceEntity<FakeCronJob>
                {
                    Id = Guid.NewGuid(),
                    CronJobId = removed.Id,
                    ExecutionTime = _Now.UtcDateTime.AddMinutes(5),
                    Status = JobStatus.Idle,
                    CreatedAt = _Now,
                    UpdatedAt = _Now,
                },
            ],
            AbortToken
        );

        await _SeedAsync(provider, _Owner, ["seed.kept"]);

        var rows = await provider.GetCronJobsAsync(predicate: null, AbortToken);
        rows.Select(x => x.Function).Should().Equal("seed.kept");
        (await provider.GetAllCronJobOccurrencesAsync(x => x.CronJobId == removed.Id, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_keep_another_owners_seeded_row_when_this_owner_removes_the_same_function()
    {
        var provider = _Create();
        await _SeedAsync(provider, _OtherOwner, [_Function]);
        await _SeedAsync(provider, _Owner, [_Function]);

        await _SeedAsync(provider, _Owner, []);

        var rows = await provider.GetCronJobsAsync(predicate: null, AbortToken);
        rows.Should().ContainSingle().Which.Function.Should().Be(_Function);
    }

    [Fact]
    public async Task should_leave_a_runtime_row_of_an_attributed_function_unchanged()
    {
        var provider = _Create();
        var runtime = new FakeCronJob
        {
            Id = Guid.NewGuid(),
            Function = _Function,
            Expression = _RuntimeExpression,
            CreatedAt = _Now,
            UpdatedAt = _Now,
            Request = [],
        };
        await provider.InsertCronJobsAsync([runtime], AbortToken);

        await _SeedAsync(provider, _Owner, [_Function]);
        await _SeedAsync(provider, _Owner, []);
        await _SeedAsync(provider, _Owner, [_Function]);

        var rows = await provider.GetCronJobsAsync(predicate: null, AbortToken);
        var stored = rows.Should().ContainSingle(x => x.Id == runtime.Id).Subject;
        stored.Expression.Should().Be(_RuntimeExpression);
        stored.InitIdentifier.Should().BeNull();
        rows.Should().ContainSingle(x => x.Id != runtime.Id).Which.Expression.Should().Be(_SeededExpression);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Declared recurring jobs, seeded through the host's seeder
    // ----------------------------------------------------------------------------------------------------------------

    private const string _ReportFunction = "seed.report";
    private const string _NightlyFunction = "seed.nightly";
    private const string _NightlyAttributeCron = "0 0 3 * * *";
    private const string _DailyCron = "0 0 6 * * *";

    // Reads a stored payload independently of the request serializer that wrote it; web defaults accept either casing.
    private static readonly JsonSerializerOptions _ReadJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task should_seed_one_row_per_declaration_of_one_job_with_its_own_zone_and_arguments()
    {
        await using var host = _CreateHost(options =>
            options
                .SeedRecurringJob(
                    "daily-eu",
                    new ReportArgs("eu"),
                    _DailyCron,
                    new RecurringJobOptions
                    {
                        TimeZoneId = "Europe/Berlin",
                        Description = "EU report",
                        Retries = 4,
                        RetryIntervals = [10, 20, 30, 40],
                        OnNodeDeath = NodeDeathPolicy.MarkFailed,
                        CorrelationId = "corr-eu",
                        CausationId = "cause-eu",
                    }
                )
                .SeedRecurringJob(
                    "daily-us",
                    new ReportArgs("us"),
                    _DailyCron,
                    new RecurringJobOptions { TimeZoneId = "America/New_York" }
                )
        );

        await _RunSeedAsync(host);
        await _RunSeedAsync(host);

        var rows = await _Store(host).GetCronJobsAsync(x => x.Function == _ReportFunction, AbortToken);
        rows.Should().HaveCount(2, "each declaration is its own definition, and a second seed adds none");
        var eu = rows.Should().ContainSingle(x => x.TimeZoneId == "Europe/Berlin").Subject;
        eu.InitIdentifier.Should().Be($"headless.jobs.seed:{_Owner}:decl:daily-eu");
        eu.Expression.Should().Be(_DailyCron);
        _Region(eu).Should().Be("eu");
        eu.ContractVersion.Should().Be("3", "the row is labelled with the registered contract version");
        eu.Description.Should().Be("EU report");
        eu.Retries.Should().Be(4);
        eu.RetryIntervals.Should().Equal(10, 20, 30, 40);
        eu.OnNodeDeath.Should().Be(NodeDeathPolicy.MarkFailed);
        eu.CorrelationId.Should().Be("corr-eu");
        eu.CausationId.Should().Be("cause-eu");
        var us = rows.Should().ContainSingle(x => x.TimeZoneId == "America/New_York").Subject;
        us.InitIdentifier.Should().Be($"headless.jobs.seed:{_Owner}:decl:daily-us");
        _Region(us).Should().Be("us");
        us.OnNodeDeath.Should().Be(NodeDeathPolicy.Retry);
    }

    [Fact]
    public async Task should_delete_a_removed_declaration_and_its_occurrences_but_keep_runtime_and_foreign_rows()
    {
        await using var first = _CreateHost(options =>
            options
                .SeedRecurringJob("daily-eu", new ReportArgs("eu"), _DailyCron)
                .SeedRecurringJob("daily-us", new ReportArgs("us"), _DailyCron)
        );
        var store = _Store(first);
        await _RunSeedAsync(first);
        var runtimeId = await first
            .GetRequiredService<IJobScheduler>()
            .ScheduleRecurringAsync(new ReportArgs("runtime"), _DailyCron, AbortToken);
        await using var otherApp = _CreateHost(
            options => options.SeedOwner(_OtherOwner).SeedRecurringJob("daily-eu", new ReportArgs("eu"), _DailyCron),
            store
        );
        await _RunSeedAsync(otherApp);
        var removed = (
            await store.GetCronJobsAsync(
                x => x.InitIdentifier == $"headless.jobs.seed:{_Owner}:decl:daily-eu",
                AbortToken
            )
        ).Single();
        await store.InsertCronJobOccurrencesAsync(
            [
                new CronJobOccurrenceEntity<CronJobEntity>
                {
                    Id = Guid.NewGuid(),
                    CronJobId = removed.Id,
                    ExecutionTime = _Now.UtcDateTime.AddMinutes(5),
                    Status = JobStatus.Idle,
                    CreatedAt = _Now,
                    UpdatedAt = _Now,
                },
            ],
            AbortToken
        );
        await using var second = _CreateHost(
            options => options.SeedRecurringJob("daily-us", new ReportArgs("us"), _DailyCron),
            store
        );

        await _RunSeedAsync(second);

        var rows = await store.GetCronJobsAsync(x => x.Function == _ReportFunction, AbortToken);
        rows.Select(x => x.InitIdentifier)
            .Should()
            .BeEquivalentTo(
                [null, $"headless.jobs.seed:{_OtherOwner}:decl:daily-eu", $"headless.jobs.seed:{_Owner}:decl:daily-us"],
                "only this owner's removed declaration goes"
            );
        _Region(rows.Single(x => x.Id == runtimeId)).Should().Be("runtime");
        (await store.GetAllCronJobOccurrencesAsync(x => x.CronJobId == removed.Id, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_resolve_a_percent_cron_of_a_declaration_from_configuration()
    {
        await using var host = _CreateHost(
            options => options.SeedRecurringJob("daily-eu", new ReportArgs("eu"), "%Jobs:Report:Cron%"),
            configuration: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Jobs:Report:Cron"] = "0 15 6 * * *",
            }
        );

        await _RunSeedAsync(host);

        var row = (await _Store(host).GetCronJobsAsync(x => x.Function == _ReportFunction, AbortToken)).Single();
        row.Expression.Should().Be("0 15 6 * * *");
    }

    [Fact]
    public async Task should_replace_the_row_when_a_declaration_key_is_renamed()
    {
        await using var first = _CreateHost(options =>
            options.SeedRecurringJob("daily-eu", new ReportArgs("eu"), _DailyCron)
        );
        var store = _Store(first);
        await _RunSeedAsync(first);
        await using var second = _CreateHost(
            options => options.SeedRecurringJob("daily-europe", new ReportArgs("eu"), _DailyCron),
            store
        );

        await _RunSeedAsync(second);

        var rows = await store.GetCronJobsAsync(x => x.Function == _ReportFunction, AbortToken);
        rows.Should()
            .ContainSingle()
            .Which.InitIdentifier.Should()
            .Be($"headless.jobs.seed:{_Owner}:decl:daily-europe");
    }

    [Fact]
    public async Task should_keep_both_rows_when_a_job_has_an_attribute_cron_and_a_declaration()
    {
        await using var host = _CreateHost(options => options.SeedRecurringJob<NightlyJob>("nightly-late", _DailyCron));

        await _RunSeedAsync(host);
        await _RunSeedAsync(host);

        var rows = await _Store(host).GetCronJobsAsync(x => x.Function == _NightlyFunction, AbortToken);
        rows.Select(x => (x.InitIdentifier, x.Expression))
            .Should()
            .BeEquivalentTo([
                ($"headless.jobs.seed:{_Owner}:cron:{_NightlyFunction}", _NightlyAttributeCron),
                ($"headless.jobs.seed:{_Owner}:decl:nightly-late", _DailyCron),
            ]);
    }

    [Fact]
    public async Task should_seed_declarations_and_delete_attribute_rows_when_attribute_seeding_is_ignored()
    {
        await using var first = _CreateHost(options =>
            options.SeedRecurringJob<NightlyJob>("nightly-late", _DailyCron)
        );
        var store = _Store(first);
        await _RunSeedAsync(first);
        await using var second = _CreateHost(
            options => options.IgnoreSeedDefinedCronJobs().SeedRecurringJob<NightlyJob>("nightly-late", _DailyCron),
            store
        );

        await _RunSeedAsync(second);

        var rows = await store.GetCronJobsAsync(predicate: null, AbortToken);
        rows.Should()
            .ContainSingle()
            .Which.InitIdentifier.Should()
            .Be($"headless.jobs.seed:{_Owner}:decl:nightly-late");
    }

    [Fact]
    public async Task should_delete_this_owners_rows_when_nothing_is_declared_any_more()
    {
        await using var first = _CreateHost(options =>
            options.SeedRecurringJob("daily-eu", new ReportArgs("eu"), _DailyCron)
        );
        var store = _Store(first);
        await _RunSeedAsync(first);
        var runtimeId = await first
            .GetRequiredService<IJobScheduler>()
            .ScheduleRecurringAsync<NightlyJob>(_DailyCron, AbortToken);
        await using var second = _CreateHost(options => options.IgnoreSeedDefinedCronJobs(), store);

        await _RunSeedAsync(second);

        var rows = await store.GetCronJobsAsync(predicate: null, AbortToken);
        rows.Should().ContainSingle("the runtime row is the only one left").Which.Id.Should().Be(runtimeId);
    }

    [Fact]
    public async Task should_pass_startup_validation_when_every_declaration_has_a_unique_key_and_a_registered_job()
    {
        await using var host = _CreateHost(options =>
            options
                .SeedRecurringJob("daily-eu", new ReportArgs("eu"), _DailyCron)
                .SeedRecurringJob<NightlyJob>("nightly-late", _DailyCron)
        );

        var act = async () => await _ValidateStartupAsync(host);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_fail_startup_validation_and_write_nothing_when_two_declarations_share_a_key()
    {
        await using var host = _CreateHost(options =>
            options
                .SeedRecurringJob("daily", new ReportArgs("eu"), _DailyCron)
                .SeedRecurringJob<NightlyJob>("daily", _DailyCron)
        );

        var validate = async () => await _ValidateStartupAsync(host);
        var seed = async () => await _RunSeedAsync(host);

        await validate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'daily'*more than once*");
        await seed.Should().ThrowAsync<ArgumentException>();
        (await _Store(host).GetCronJobsAsync(predicate: null, AbortToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task should_fail_startup_validation_and_write_nothing_when_a_declared_argument_type_has_no_job()
    {
        await using var host = _CreateHost(options =>
            options.SeedRecurringJob("orphan", new UnregisteredArgs(1), _DailyCron)
        );

        var validate = async () => await _ValidateStartupAsync(host);
        var seed = async () => await _RunSeedAsync(host);

        await validate.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'orphan'*");
        await seed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'orphan'*");
        (await _Store(host).GetCronJobsAsync(predicate: null, AbortToken))
            .Should()
            .BeEmpty("the attribute cron is not written either when one declaration cannot be built");
    }

    [Fact]
    public async Task should_fail_startup_validation_when_a_declared_job_type_is_not_registered()
    {
        await using var host = _CreateHost(options => options.SeedRecurringJob<UnregisteredJob>("orphan", _DailyCron));

        var act = async () => await _ValidateStartupAsync(host);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'orphan'*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_a_blank_declaration_key_when_it_is_declared(string key)
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessJobs(options => options.SeedRecurringJob(key, new ReportArgs("eu"), _DailyCron));

        act.Should().Throw<ArgumentException>();
    }

    private static string? _Region(CronJobEntity row)
    {
        return JsonSerializer.Deserialize<ReportArgs>(row.Request!, _ReadJson)?.Region;
    }

    private static IJobPersistenceProvider<TimeJobEntity, CronJobEntity> _Store(ServiceProvider host)
    {
        return host.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
    }

    private static Task _RunSeedAsync(ServiceProvider host)
    {
        return host.GetRequiredService<JobsSeeder>().SeedAsync(AbortToken);
    }

    private static async Task _ValidateStartupAsync(ServiceProvider host)
    {
        foreach (var validator in host.GetServices<IHeadlessStartupValidator>())
        {
            await validator.ValidateAsync(AbortToken);
        }
    }

    /// <summary>
    /// A host of the app under test. Passing <paramref name="store"/> shares another host's store, which is how a test
    /// changes what the app declares between two seeds, or seeds as a second app.
    /// </summary>
    private static ServiceProvider _CreateHost(
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>> configure,
        IJobPersistenceProvider<TimeJobEntity, CronJobEntity>? store = null,
        Dictionary<string, string?>? configuration = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build()
        );
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddHeadlessJobs(options =>
        {
            options.DisableBackgroundServices().DisableStartupInitialization().AddModule<SeedJobsModule>();
            options.SeedOwner(_Owner);
            configure(options);
        });

        if (store is not null)
        {
            services.AddSingleton(store);
        }

        return services.BuildServiceProvider();
    }

    public sealed record ReportArgs(string Region);

    public sealed record UnregisteredArgs(int Value);

    public sealed class ReportJob : IJob<ReportArgs>
    {
        public ValueTask ExecuteAsync(JobContext<ReportArgs> context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class NightlyJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class UnregisteredJob : IJob
    {
        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    public sealed class SeedJobsModule : IJobsModule
    {
        private SeedJobsModule() { }

        static void IJobsModule.Register(JobsCatalogBuilder catalog)
        {
            catalog.AddFunctions(
                new Dictionary<string, JobFunctionRegistration>(StringComparer.Ordinal)
                {
                    [_ReportFunction] = new()
                    {
                        CronExpression = "",
                        Priority = JobPriority.Normal,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                        JobType = typeof(ReportJob),
                    },
                    [_NightlyFunction] = new()
                    {
                        CronExpression = _NightlyAttributeCron,
                        Priority = JobPriority.Normal,
                        Delegate = static (_, _, _) => Task.CompletedTask,
                        MaxConcurrency = 0,
                        JobType = typeof(NightlyJob),
                    },
                }
            );
            catalog.AddRequestTypes(
                new Dictionary<string, (string, Type)>(StringComparer.Ordinal)
                {
                    [_ReportFunction] = (typeof(ReportArgs).FullName!, typeof(ReportArgs)),
                }
            );
            catalog.AddDescriptors(
                new Dictionary<string, JobFunctionDescriptor>(StringComparer.Ordinal)
                {
                    [_ReportFunction] = new(_ReportFunction, typeof(ReportArgs), "", JobPriority.Normal, 0, "3"),
                    [_NightlyFunction] = new(_NightlyFunction, null, _NightlyAttributeCron, JobPriority.Normal, 0),
                }
            );
        }
    }

    private static Task _SeedAsync(
        JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> provider,
        string owner,
        string[] functions
    )
    {
        return provider.MigrateDefinedCronJobsAsync(
            owner,
            [
                .. functions.Select(function =>
                    CronSeedDefinition.ForCronFunction(
                        owner,
                        function,
                        _SeededExpression,
                        MissedRunPolicy.Coalesce,
                        JobsRecoveryDefaults.MissedRunGraceSeconds,
                        CronOverlapPolicy.Allow
                    )
                ),
            ],
            AbortToken
        );
    }

    private static JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob> _Create()
    {
        var services = new ServiceCollection();
        services.AddHeadlessGuidGenerator();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(_Now));
        services.AddSingleton(new SchedulerOptionsBuilder { NodeId = "node-a@incarnation" });
        return new JobsInMemoryPersistenceProvider<FakeTimeJob, FakeCronJob>(services.BuildServiceProvider());
    }
}
