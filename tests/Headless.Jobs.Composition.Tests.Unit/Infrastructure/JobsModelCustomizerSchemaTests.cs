// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Customizer;
using Headless.Jobs.Entities;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The model-customizer path is the one a consumer-hosted DbContext takes. It used to apply the Jobs configurations
/// with their mapping defaults and map the reservation table to a constant schema, so a configured override reached
/// only the dedicated JobsDbContext. These cover that every table the customizer maps reads the one storage option.
/// </summary>
public sealed class JobsModelCustomizerSchemaTests : TestBase
{
    private const string _CustomSchema = "jobs_custom";

    [Fact]
    public void customizer_maps_the_reservation_table_into_the_configured_schema()
    {
        using var context = _CreateCustomizedContext(_CustomSchema);

        var reservation = context.Model.FindEntityType(typeof(JobIdempotencyReservationEntity));

        reservation.Should().NotBeNull("the customizer maps the Jobs-owned reservation table");
        reservation!.GetSchema().Should().Be(_CustomSchema);
    }

    [Fact]
    public void customizer_maps_every_jobs_table_into_the_configured_schema()
    {
        using var context = _CreateCustomizedContext(_CustomSchema);

        context.Model.FindEntityType(typeof(TimeJobEntity))!.GetSchema().Should().Be(_CustomSchema);
        context.Model.FindEntityType(typeof(CronJobEntity))!.GetSchema().Should().Be(_CustomSchema);
        context
            .Model.FindEntityType(typeof(CronJobOccurrenceEntity<CronJobEntity>))!
            .GetSchema()
            .Should()
            .Be(_CustomSchema);
    }

    [Fact]
    public void customizer_falls_back_to_the_default_schema_when_nothing_is_configured()
    {
        using var context = _CreateCustomizedContext(JobsStorageOptions.DefaultSchema);

        context
            .Model.FindEntityType(typeof(JobIdempotencyReservationEntity))!
            .GetSchema()
            .Should()
            .Be(JobsStorageOptions.DefaultSchema);
        context.Model.FindEntityType(typeof(TimeJobEntity))!.GetSchema().Should().Be(JobsStorageOptions.DefaultSchema);
    }

    /// <summary>
    /// Builds a consumer-shaped context (a plain DbContext, not a JobsDbContext) whose model is produced by the Jobs
    /// customizer, with the storage option reachable exactly as the real registration leaves it: through the
    /// application service provider the context is bound to.
    /// </summary>
    private static ConsumerDbContext _CreateCustomizedContext(string schema)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new JobsStorageOptions { Schema = schema });
        var provider = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ConsumerDbContext>()
            .UseSqlite("Data Source=:memory:")
            .ReplaceService<IModelCustomizer, JobsModelCustomizer<TimeJobEntity, CronJobEntity>>()
            .UseApplicationServiceProvider(provider)
            // EF keys its model cache on the context type, so without a private internal provider every case here
            // would assert against whichever schema the first-built model happened to use.
            .EnableServiceProviderCaching(false)
            .Options;

        return new ConsumerDbContext(options);
    }

    private sealed class ConsumerDbContext(DbContextOptions<ConsumerDbContext> options) : DbContext(options);
}
