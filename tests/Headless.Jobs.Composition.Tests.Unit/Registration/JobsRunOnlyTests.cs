// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.BackgroundServices;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Registration;

/// <summary>
/// A host with <c>RunOnly</c> keeps every job registered and schedulable but never leases a row whose job it filtered
/// out, on any claim path.
/// </summary>
public sealed class JobsRunOnlyTests : TestBase
{
    [Fact]
    public async Task should_schedule_a_filtered_job_but_never_claim_it()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var provider = _Provider(clock, options => options.RunOnly("orders.*"));
        var scheduler = provider.GetRequiredService<IJobScheduler>();
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        // when
        var billingId = await scheduler.EnqueueAsync<BillingCloseDay>(AbortToken);
        var ordersId = await scheduler.EnqueueAsync<OrdersShip>(AbortToken);
        var peeked = await store.GetEarliestTimeJobsAsync(AbortToken);
        var acquiredBilling = await store.AcquireImmediateTimeJobsAsync([billingId], AbortToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        var swept = await _CollectAsync(store.QueueTimedOutTimeJobsAsync(AbortToken));

        // then
        peeked.Jobs.Select(x => x.Id).Should().Equal(ordersId);
        acquiredBilling.Should().BeEmpty();
        swept.Select(x => x.Id).Should().Equal(ordersId);
        var billing = await store.GetTimeJobByIdAsync(billingId, AbortToken);
        billing!.Status.Should().Be(JobStatus.Idle);
        billing.OwnerId.Should().BeNull();
    }

    [Fact]
    public async Task should_claim_every_job_on_a_host_without_a_filter()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var provider = _Provider(clock);
        var scheduler = provider.GetRequiredService<IJobScheduler>();
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();

        // when
        var billingId = await scheduler.EnqueueAsync<BillingCloseDay>(AbortToken);
        var ordersId = await scheduler.EnqueueAsync<OrdersShip>(AbortToken);
        clock.Advance(TimeSpan.FromSeconds(5));
        var swept = await _CollectAsync(store.QueueTimedOutTimeJobsAsync(AbortToken));

        // then
        swept.Select(x => x.Id).Should().BeEquivalentTo([billingId, ordersId]);
    }

    [Fact]
    public async Task should_run_an_exactly_named_job_and_keep_its_module_siblings_filtered()
    {
        // given
        await using var provider = _Provider(options: options => options.RunOnly(TestJobs.BillingSendInvoice));

        // when
        var filter = provider.GetRequiredService<JobFunctionRegistry>().RunFilter;

        // then
        filter.Allows(TestJobs.BillingSendInvoice).Should().BeTrue();
        filter.Allows(TestJobs.BillingCloseDay).Should().BeFalse();
        filter.Allows(TestJobs.OrdersShip).Should().BeFalse();
        filter.RunnableFunctions.Should().Equal(TestJobs.BillingSendInvoice);
    }

    [Fact]
    public async Task should_give_two_hosts_in_one_process_their_own_filters()
    {
        // given
        await using var ordersHost = _Provider(options: options => options.RunOnly("orders.*"));
        await using var billingHost = _Provider(options: options => options.RunOnly("billing.*"));

        // when
        var orders = ordersHost.GetRequiredService<JobFunctionRegistry>();
        var billing = billingHost.GetRequiredService<JobFunctionRegistry>();

        // then
        orders.Should().NotBeSameAs(billing);
        orders.Functions.Keys.Should().BeEquivalentTo(billing.Functions.Keys);
        orders.RunFilter.RunnableFunctions.Should().Equal(TestJobs.OrdersShip);
        billing.RunFilter.RunnableFunctions.Should().Equal(TestJobs.BillingCloseDay, TestJobs.BillingSendInvoice);
    }

    [Fact]
    public async Task should_fail_startup_when_a_filter_entry_matches_no_registered_job()
    {
        // given
        await using var provider = _Provider(options: options => options.RunOnly("orders.*", "shipping.*"));

        // when
        var resolve = () => provider.GetRequiredService<JobFunctionRegistry>();

        // then
        resolve.Should().Throw<InvalidOperationException>().WithMessage("*'shipping.*'*");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" orders.*")]
    [InlineData("orders*")]
    [InlineData("*.ship")]
    [InlineData(".*")]
    [InlineData("orders.a.*")]
    [InlineData("orders.*.x")]
    public void should_reject_a_malformed_filter_entry_when_it_is_authored(string entry)
    {
        var services = new ServiceCollection();

        var add = () => services.AddHeadlessJobs(options => options.RunOnly(entry));

        add.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task should_seed_the_cron_definition_of_a_job_the_host_does_not_run()
    {
        // given
        await using var provider = _Provider(options: options =>
            options.AddModule<BillingNightlyJobsModule>().RunOnly("orders.*")
        );
        var initializer = provider.GetServices<IHostedService>().OfType<JobsInitializationHostedService>().Single();

        // when
        await initializer.StartAsync(AbortToken);

        // then
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var definitions = await store.GetCronJobsAsync(predicate: null, AbortToken);
        definitions.Select(x => x.Function).Should().Contain(TestJobs.BillingNightly);
        provider.GetRequiredService<JobFunctionRegistry>().RunFilter.Allows(TestJobs.BillingNightly).Should().BeFalse();
    }

    [Fact]
    public async Task should_never_claim_or_dispatch_a_filtered_cron_job()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var provider = _Provider(clock, options => options.RunOnly("orders.*"));
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var runnableDefinition = _CronDefinition(TestJobs.OrdersShip);
        var filteredDefinition = _CronDefinition(TestJobs.BillingCloseDay);
        await store.InsertCronJobsAsync([runnableDefinition, filteredDefinition], AbortToken);
        var overdue = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        var runnableOccurrence = _Occurrence(runnableDefinition.Id, overdue);
        var filteredOccurrence = _Occurrence(filteredDefinition.Id, overdue);
        await store.InsertCronJobOccurrencesAsync([runnableOccurrence, filteredOccurrence], AbortToken);

        // when
        var swept = await store.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken);
        var instant = clock.GetUtcNow().UtcDateTime.AddMinutes(30);
        var claimed = await store
            .QueueCronJobOccurrencesAsync(
                (instant, [_Dispatch(runnableDefinition), _Dispatch(filteredDefinition)]),
                AbortToken
            )
            .ToArrayAsync(AbortToken);
        var candidates = await store.GetEarliestCronDispatchCandidatesAsync(10, cancellationToken: AbortToken);

        // then
        swept.Select(x => x.Id).Should().Equal(runnableOccurrence.Id);
        claimed.Should().ContainSingle().Which.CronJobId.Should().Be(runnableDefinition.Id);
        candidates.Should().NotBeNull();
        candidates.Candidates.Should().OnlyContain(x => x.FunctionName == TestJobs.OrdersShip);
        var stored = (
            await store.GetAllCronJobOccurrencesAsync(x => x.Id == filteredOccurrence.Id, AbortToken)
        ).Single();
        stored.Status.Should().Be(JobStatus.Idle);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task should_never_peek_or_acquire_a_filtered_cron_occurrence_directly()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var provider = _Provider(clock, options => options.RunOnly("orders.*"));
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var runnableDefinition = _CronDefinition(TestJobs.OrdersShip);
        var filteredDefinition = _CronDefinition(TestJobs.BillingCloseDay);
        await store.InsertCronJobsAsync([runnableDefinition, filteredDefinition], AbortToken);
        var upcoming = clock.GetUtcNow().UtcDateTime.AddSeconds(30);
        var runnableOccurrence = _Occurrence(runnableDefinition.Id, upcoming);
        var filteredOccurrence = _Occurrence(filteredDefinition.Id, upcoming);
        await store.InsertCronJobOccurrencesAsync([runnableOccurrence, filteredOccurrence], AbortToken);

        // when
        var peekedFiltered = await store.GetEarliestAvailableCronOccurrenceAsync([filteredDefinition.Id], AbortToken);
        var peekedRunnable = await store.GetEarliestAvailableCronOccurrenceAsync([runnableDefinition.Id], AbortToken);
        var acquired = await store.AcquireImmediateCronOccurrencesAsync(
            [runnableOccurrence.Id, filteredOccurrence.Id],
            AbortToken
        );

        // then
        peekedFiltered.Should().BeNull();
        peekedRunnable.Should().NotBeNull();
        peekedRunnable.Id.Should().Be(runnableOccurrence.Id);
        acquired.Select(x => x.Id).Should().Equal(runnableOccurrence.Id);
        var stored = (
            await store.GetAllCronJobOccurrencesAsync(x => x.Id == filteredOccurrence.Id, AbortToken)
        ).Single();
        stored.Status.Should().Be(JobStatus.Idle);
        stored.OwnerId.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task should_claim_every_cron_job_on_a_host_without_a_filter()
    {
        // given
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        await using var provider = _Provider(clock);
        var store = provider.GetRequiredService<IJobPersistenceProvider<TimeJobEntity, CronJobEntity>>();
        var ordersDefinition = _CronDefinition(TestJobs.OrdersShip);
        var billingDefinition = _CronDefinition(TestJobs.BillingCloseDay);
        await store.InsertCronJobsAsync([ordersDefinition, billingDefinition], AbortToken);
        var overdue = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        var ordersOccurrence = _Occurrence(ordersDefinition.Id, overdue);
        var billingOccurrence = _Occurrence(billingDefinition.Id, overdue);
        await store.InsertCronJobOccurrencesAsync([ordersOccurrence, billingOccurrence], AbortToken);

        // when
        var swept = await store.QueueTimedOutCronJobOccurrencesAsync(AbortToken).ToArrayAsync(AbortToken);

        // then
        swept.Select(x => x.Id).Should().BeEquivalentTo([ordersOccurrence.Id, billingOccurrence.Id]);
    }

    private static CronJobEntity _CronDefinition(string function) =>
        new()
        {
            Id = Guid.NewGuid(),
            Function = function,
            Expression = "0 0 * * * *",
        };

    private static CronJobOccurrenceEntity<CronJobEntity> _Occurrence(Guid cronJobId, DateTime executionTime) =>
        new()
        {
            Id = Guid.NewGuid(),
            CronJobId = cronJobId,
            ExecutionTime = executionTime,
        };

    private static JobManagerDispatchContext _Dispatch(CronJobEntity definition) =>
        new(definition.Id)
        {
            FunctionName = definition.Function,
            Expression = definition.Expression,
            ScheduleRevision = definition.ScheduleRevision,
            OnNodeDeath = NodeDeathPolicy.Retry,
        };

    private static ServiceProvider _Provider(
        TimeProvider? clock = null,
        Action<JobsOptionsBuilder<TimeJobEntity, CronJobEntity>>? options = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        services.AddHeadlessJobs(jobs =>
        {
            jobs.DisableBackgroundServices();
            jobs.AddModule<BillingJobsModule>().AddModule<OrdersJobsModule>();
            options?.Invoke(jobs);
        });
        return services.BuildServiceProvider();
    }

    private static async Task<List<TimeJobEntity>> _CollectAsync(IAsyncEnumerable<TimeJobEntity> source)
    {
        var items = new List<TimeJobEntity>();
        await foreach (var item in source.WithCancellation(AbortToken))
        {
            items.Add(item);
        }

        return items;
    }
}
