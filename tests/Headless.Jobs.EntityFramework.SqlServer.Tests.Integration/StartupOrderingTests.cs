// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Jobs;
using Headless.Jobs.DbContextFactory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// R5: the durable Jobs store must fail fast at registration when no real Coordination provider is present, and
/// must accept the actual SQL Server provider (not just the unit-test stub). No container needed — registration and
/// container build never open a database connection.
/// </summary>
public sealed class StartupOrderingTests
{
    private const string _DummyConnectionString = "Server=localhost;Database=unused;User Id=sa;Password=p;";

    [Fact]
    public void durable_store_without_a_coordination_provider_fails_fast()
    {
        var services = new ServiceCollection();

        var act = () =>
            services.AddHeadlessJobs(options =>
                options.UseEntityFramework(ef =>
                    ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(_DummyConnectionString), schema: "jobs")
                )
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddHeadlessCoordination*");
    }

    [Fact]
    public void durable_store_with_the_sqlserver_coordination_provider_registers()
    {
        var services = new ServiceCollection();

        services.AddHeadlessCoordination(setup => setup.UseSqlServer(_DummyConnectionString));

        var act = () =>
            services.AddHeadlessJobs(options =>
                options.UseEntityFramework(ef =>
                    ef.UseJobsDbContext<JobsDbContext>(db => db.UseSqlServer(_DummyConnectionString), schema: "jobs")
                )
            );

        act.Should().NotThrow();
    }
}
