// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Context;
using Headless.Coordination;
using Headless.Jobs;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Provider conformance for the EF Jobs store's readiness check: it reports the real database's reachability through
/// the store's own context, Healthy while connections open and Unhealthy when the database cannot be opened.
/// </summary>
public abstract class JobsStorageHealthCheckConformanceTests<TFixture>(TFixture fixture) : TestBase
    where TFixture : class, IJobsCoordinationFixture
{
    private static readonly string _CheckName = "jobs-storage-" + typeof(JobsDbContext).FullName;

    public virtual async Task storage_check_reports_healthy_when_the_store_can_connect()
    {
        using var host = _BuildHost(fixture.ConnectionString);

        var entry = await _CheckAsync(host);

        entry.Status.Should().Be(HealthStatus.Healthy);
    }

    public virtual async Task storage_check_reports_unhealthy_when_the_database_cannot_be_opened()
    {
        // The server is up but the database does not exist, so every connection the store opens is refused.
        var missing = new DbConnectionStringBuilder { ConnectionString = fixture.ConnectionString };
        missing["Database"] = "headless_missing_" + Guid.NewGuid().ToString("N");
        using var host = _BuildHost(missing.ConnectionString);

        var entry = await _CheckAsync(host);

        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Description.Should().Be($"The '{_CheckName}' dependency probe failed.");
    }

    private IHost _BuildHost(string storeConnectionString)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.AddHeadlessHostIdentity(options => options.HostName = "storage-health");
        builder.Services.AddHeadlessCoordination(fixture.ConfigureCoordination);
        builder.Services.AddHeadlessJobs(jobs =>
            jobs.DisableBackgroundServices()
                .UseEntityFramework(ef =>
                    ef.UseJobsDbContext<JobsDbContext>(db =>
                    {
                        fixture.ConfigureStore(db);
                        // Keeps the fixture's provider and settings, and swaps only where the store connects.
                        var relational = db.Options.Extensions.OfType<RelationalOptionsExtension>().Single();
                        ((IDbContextOptionsBuilderInfrastructure)db).AddOrUpdateExtension(
                            relational.WithConnectionString(storeConnectionString)
                        );
                    })
                )
        );

        return builder.Build();
    }

    private static async Task<HealthReportEntry> _CheckAsync(IHost host)
    {
        var report = await host
            .Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(
                registration => string.Equals(registration.Name, _CheckName, StringComparison.Ordinal),
                AbortToken
            );

        return report.Entries[_CheckName];
    }
}
