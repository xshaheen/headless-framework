// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Interfaces;
using Headless.Jobs.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Provider conformance for the feature-owned storage schema: configuring <c>JobsStorageOptions.Schema</c> must move
/// EVERY Jobs table, the non-generic idempotency reservation table included, and must leave nothing behind in the
/// default schema. The reservation table is the interesting half — it was mapped to a constant schema, so an
/// override used to split the store across two schemas with no error anywhere.
/// </summary>
public abstract class JobsCustomSchemaConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private const string _Schema = JobsCoordinationFixtureExtensions.CustomSchemaName;
    private const string _DefaultSchema = JobsStorageOptions.DefaultSchema;
    private const string _TimeJobsTable = "TimeJobs";
    private const string _ReservationsTable = "TimeJobIdempotencyReservations";

    public virtual async Task configured_schema_holds_every_jobs_table_including_the_reservation_table()
    {
        var ct = AbortToken;
        await fixture.ResetDatabaseAsync(ct);
        using var host = fixture.BuildCustomSchemaEnqueueHost("custom-schema-a", _Schema);
        await JobsCoordinationFixtureExtensions.CreateJobsSchemaAsync<CustomSchemaJobsDbContext>(host, ct);
        await host.StartAsync(ct);

        try
        {
            var scheduler = host.Services.GetRequiredService<IJobScheduler>();

            // A plain enqueue exercises the time-job mapping; the idempotent one additionally forces a write through
            // the reservation table, so a reservation stranded in the default schema fails here rather than silently.
            await scheduler.EnqueueAsync(new CoordinatedFacadeRequest(Guid.NewGuid(), "plain"), ct);
            await scheduler.EnqueueAsync(
                new CoordinatedFacadeRequest(Guid.NewGuid(), "idempotent"),
                new JobOptions { IdempotencyKey = "custom-schema-key", IdempotencyTtl = TimeSpan.FromMinutes(10) },
                ct
            );

            (await fixture.TableExistsAsync(_Schema, _TimeJobsTable, ct))
                .Should()
                .BeTrue("the configured schema owns the time-job table");
            (await fixture.TableExistsAsync(_Schema, _ReservationsTable, ct))
                .Should()
                .BeTrue("the reservation table follows the same option as every other Jobs table");

            (await fixture.TableExistsAsync(_DefaultSchema, _TimeJobsTable, ct))
                .Should()
                .BeFalse("nothing may be left behind in the default schema once an override is configured");
            (await fixture.TableExistsAsync(_DefaultSchema, _ReservationsTable, ct))
                .Should()
                .BeFalse("a reservation table in the default schema is the exact split this option prevents");

            // Existence alone would also pass if the runtime wrote nowhere, so assert the rows landed in the moved
            // tables: two enqueues, one of which reserved its key.
            (await fixture.CountRowsAsync(_Schema, _TimeJobsTable, ct))
                .Should()
                .Be(2);
            (await fixture.CountRowsAsync(_Schema, _ReservationsTable, ct)).Should().Be(1);
        }
        finally
        {
            await host.StopAsync(ct);
        }
    }
}
