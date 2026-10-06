// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Testing.Tests;
using Microsoft.CodeAnalysis;

namespace Tests;

/// <summary>HF2002: scheduling a job through an injected scheduler or manager while a unit of work is in scope.</summary>
public sealed class HF2002JobsTests : TestBase
{
    private const string _Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using Headless.Jobs;
        using Headless.UnitOfWork;
        using Microsoft.EntityFrameworkCore;

        public sealed record ReindexOrder(int Id);

        public sealed class ReportJob : TimeJobEntity<ReportJob>;

        public sealed class NightlyJob : CronJobEntity;

        public sealed class NoDefaultConstructorJob(int priority) : TimeJobEntity<NoDefaultConstructorJob>
        {
            public int Priority { get; } = priority;
        }

        public sealed class RequiredMemberJob : TimeJobEntity<RequiredMemberJob>
        {
            public required string Tag { get; set; }
        }

        """;

    [Fact]
    public async Task should_report_scheduler_calls_and_fix_them_onto_the_unit_jobs()
    {
        const string body = """
            public sealed class Handler(IJobScheduler scheduler, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        await scheduler.EnqueueAsync(new ReindexOrder(1), token);
                    }, cancellationToken: ct);
            }
            """;

        var diagnostics = await _AnalyzeAsync(body);

        _ShouldReport(diagnostics, "unit.Jobs");
        (await AnalyzerHarness.FixAsync(_Prelude + body, AbortToken))
            .Should()
            .Contain("await unit.Jobs.EnqueueAsync(new ReindexOrder(1), token);");
    }

    [Fact]
    public async Task should_fix_a_scheduler_builder_overload()
    {
        const string body = """
            public sealed class Handler(IJobScheduler scheduler, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        await scheduler.ScheduleAfterAsync(new ReindexOrder(1), TimeSpan.FromMinutes(5), options => { }, token);
                    }, cancellationToken: ct);
            }
            """;

        (await AnalyzerHarness.FixAsync(_Prelude + body, AbortToken))
            .Should()
            .Contain(
                "await unit.Jobs.ScheduleAfterAsync(new ReindexOrder(1), TimeSpan.FromMinutes(5), options => { }, token);"
            );
    }

    [Fact]
    public async Task should_report_manager_calls_and_keep_the_job_type_in_the_fix()
    {
        const string body = """
            public sealed class Handler(
                ITimeJobManager<ReportJob> timeJobs,
                ICronJobManager<NightlyJob> cronJobs,
                IUnitOfWorkFactory factory
            )
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        await timeJobs.AddAsync(new ReportJob(), token);
                        await cronJobs.AddAsync(new NightlyJob(), token);
                    }, cancellationToken: ct);
            }
            """;

        var diagnostics = await _AnalyzeAsync(body);

        diagnostics.Should().HaveCount(2);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).Should().Contain("'unit.TimeJobs<ReportJob>()'");
        diagnostics[1].GetMessage(CultureInfo.InvariantCulture).Should().Contain("'unit.CronJobs<NightlyJob>()'");

        var fixedSource = await AnalyzerHarness.FixAllAsync(_Prelude + body, AbortToken);

        fixedSource.Should().Contain("await unit.TimeJobs<ReportJob>().AddAsync(new ReportJob(), token);");
        fixedSource.Should().Contain("await unit.CronJobs<NightlyJob>().AddAsync(new NightlyJob(), token);");
    }

    [Theory]
    [InlineData("NoDefaultConstructorJob", "new NoDefaultConstructorJob(1)")]
    [InlineData("RequiredMemberJob", "new RequiredMemberJob { Tag = \"x\" }")]
    public async Task should_offer_no_fix_when_the_job_type_cannot_satisfy_the_accessor(string jobType, string job)
    {
        var fixes = await AnalyzerHarness.GetFixesAsync(
            _Prelude
                + $$"""
                public sealed class Handler(ITimeJobManager<{{jobType}}> timeJobs, IUnitOfWorkFactory factory)
                {
                    public Task Handle(DbContext db, CancellationToken ct) =>
                        factory.RunAsync(db, async (unit, token) =>
                        {
                            await timeJobs.AddAsync({{job}}, token);
                        }, cancellationToken: ct);
                }
                """,
            AbortToken
        );

        fixes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_report_calls_through_the_unit_jobs_accessors()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        await unit.Jobs.EnqueueAsync(new ReindexOrder(1), token);
                        await unit.TimeJobs<ReportJob>().AddAsync(new ReportJob(), token);
                        await unit.CronJobs<NightlyJob>().AddAsync(new NightlyJob(), token);

                        var jobs = unit.Jobs;
                        await jobs.EnqueueAsync(new ReindexOrder(2), token);

                        await (unit.Jobs?.EnqueueAsync(new ReindexOrder(4), token) ?? Task.FromResult(Guid.Empty));
                    }, cancellationToken: ct);

                public async Task Guarded(DbContext db)
                {
                    var unit = db.UnitOfWork();
                    await (unit?.Jobs.EnqueueAsync(new ReindexOrder(3)) ?? Task.FromResult(Guid.Empty));
                }
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_report_a_local_reassigned_from_an_injected_scheduler()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IJobScheduler scheduler, IUnitOfWorkFactory factory)
            {
                public Task Handle(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        var jobs = unit.Jobs;
                        jobs = scheduler;
                        await jobs.EnqueueAsync(new ReindexOrder(1), token);
                    }, cancellationToken: ct);
            }
            """
        );

        _ShouldReport(diagnostics, "unit.Jobs");
    }

    [Fact]
    public async Task should_report_a_local_passed_by_ref_or_out_after_its_accessor_initializer()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IJobScheduler scheduler, IUnitOfWorkFactory factory)
            {
                public Task ByRef(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        var jobs = unit.Jobs;
                        Swap(ref jobs);
                        await jobs.EnqueueAsync(new ReindexOrder(1), token);
                    }, cancellationToken: ct);

                public Task ByOut(DbContext db, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        var jobs = unit.Jobs;
                        Replace(out jobs);
                        await jobs.EnqueueAsync(new ReindexOrder(2), token);
                    }, cancellationToken: ct);

                private void Swap(ref IJobScheduler jobs) => jobs = scheduler;

                private void Replace(out IJobScheduler jobs) => jobs = scheduler;
            }
            """
        );

        diagnostics.Should().HaveCount(2);
        diagnostics.Should().AllSatisfy(diagnostic => diagnostic.Id.Should().Be("HF2002"));
    }

    [Fact]
    public async Task should_report_a_top_level_local_reassigned_in_a_later_statement()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(
            """
            using System.Threading.Tasks;
            using Headless.Jobs;
            using Headless.UnitOfWork;

            IJobScheduler scheduler = null!;
            IUnitOfWorkFactory factory = null!;

            await using var unit = await factory.BeginAsync();
            var jobs = unit.Jobs;
            jobs = scheduler;
            await jobs.EnqueueAsync(new ReindexOrder(1));

            public sealed record ReindexOrder(int Id);
            """,
            AbortToken,
            outputKind: Microsoft.CodeAnalysis.OutputKind.ConsoleApplication
        );

        _ShouldReport(diagnostics, "unit.Jobs");
    }

    [Fact]
    public async Task should_not_report_members_that_never_enlist()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(
                IJobScheduler scheduler,
                ITimeJobManager<ReportJob> timeJobs,
                ICronJobManager<NightlyJob> cronJobs,
                IUnitOfWorkFactory factory
            )
            {
                public Task Handle(DbContext db, Guid id, CancellationToken ct) =>
                    factory.RunAsync(db, async (unit, token) =>
                    {
                        await scheduler.CancelAsync(id, token);
                        await scheduler.PauseCronAsync(id, token);
                        await scheduler.ResumeCronAsync(id, token);
                        await scheduler.RequeueAsync(id, token);
                        await timeJobs.UpdateAsync(new ReportJob(), token);
                        await timeJobs.DeleteAsync(id, token);
                        await timeJobs.UpdateBatchAsync([], token);
                        await timeJobs.DeleteBatchAsync([], token);
                        await cronJobs.UpdateAsync(new NightlyJob(), token);
                        await cronJobs.DeleteAsync(id, token);
                        await cronJobs.UpdateBatchAsync([], token);
                        await cronJobs.DeleteBatchAsync([], token);
                    }, cancellationToken: ct);
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_report_without_a_unit_in_scope()
    {
        var diagnostics = await _AnalyzeAsync(
            """
            public sealed class Handler(IJobScheduler scheduler)
            {
                public Task Handle(CancellationToken ct) => scheduler.EnqueueAsync(new ReindexOrder(1), ct);
            }
            """
        );

        diagnostics.Should().BeEmpty();
    }

    private static Task<ImmutableArray<Diagnostic>> _AnalyzeAsync(string body) =>
        AnalyzerHarness.AnalyzeAsync(_Prelude + body, AbortToken);

    private static void _ShouldReport(ImmutableArray<Diagnostic> diagnostics, string receiver)
    {
        diagnostics.Should().ContainSingle();
        diagnostics[0].Id.Should().Be("HF2002");
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).Should().Contain($"'{receiver}'");
    }
}
