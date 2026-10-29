// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Headless.Jobs;
using Headless.Jobs.Provider;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Provider;

/// <summary>
/// A dashboard "Run now" occurrence is due at the store's clock inside its insert, so a due time the caller built the
/// occurrence with is never what lands. The in-memory store's clock is its <see cref="TimeProvider"/>.
/// </summary>
public sealed class RunNowCronOccurrenceProviderTests : TestBase
{
    private static readonly DateTimeOffset _BuiltAt = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task should_stamp_the_occurrence_from_the_store_clock_when_the_caller_supplied_an_execution_time()
    {
        var clock = new FakeTimeProvider(_BuiltAt);
        await using var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(clock)
            .AddHeadlessGuidGenerator()
            .BuildServiceProvider();
        var provider = new JobsInMemoryPersistenceProvider<TimeJobEntity, CronJobEntity>(services);
        var cron = new CronJobEntity
        {
            Id = Guid.NewGuid(),
            Function = "run-now-cron",
            Expression = "0 0 * * * *",
        };
        await provider.InsertCronJobsAsync([cron], AbortToken);
        var occurrence = new CronJobOccurrenceEntity<CronJobEntity>
        {
            Id = Guid.NewGuid(),
            CronJobId = cron.Id,
            ExecutionTime = _BuiltAt.UtcDateTime,
        };

        // The store clock moves on between building the occurrence and the insert, as it does across a slow request.
        clock.Advance(TimeSpan.FromMinutes(5));
        var storeNow = clock.GetUtcNow().UtcDateTime;

        var returned = await provider.InsertRunNowCronJobOccurrenceAsync(occurrence, AbortToken);

        returned.Should().Be(storeNow);
        occurrence.ExecutionTime.Should().Be(storeNow);
        var persisted = await provider.GetAllCronJobOccurrencesAsync(x => x.Id == occurrence.Id, AbortToken);
        persisted.Should().ContainSingle().Which.ExecutionTime.Should().Be(storeNow);
    }
}
