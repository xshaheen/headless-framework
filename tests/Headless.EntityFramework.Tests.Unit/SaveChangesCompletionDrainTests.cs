// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A pipeline-owned save commits before it drains the unit's after-commit callbacks, so a callback fault cannot
/// undo the save: the save reports the committed count instead of a failure a caller would retry.
/// </summary>
public sealed class SaveChangesCompletionDrainTests : TestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_return_the_committed_count_when_an_after_commit_callback_faults(bool synchronous)
    {
        // given
        var connection = new SqliteConnection("Data Source=:memory:");
        await using var _ = connection;
        await connection.OpenAsync(AbortToken);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContext<ShipmentsDbContext>(options => options.UseSqlite(connection)).AddDomainEvents();
        services.AddScoped<IDomainEventHandler<ShipmentPlaced>, FaultingCallbackHandler>();
        await using var provider = services.BuildServiceProvider();

        await using (var setup = provider.CreateAsyncScope())
        {
            await setup
                .ServiceProvider.GetRequiredService<ShipmentsDbContext>()
                .Database.EnsureCreatedAsync(AbortToken);
        }

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentsDbContext>();
        var shipment = new Shipment { Id = Guid.CreateVersion7() };
        shipment.Place();
        db.Add(shipment);

        // when
        var saved = synchronous ? db.SaveChanges() : await db.SaveChangesAsync(AbortToken);

        // then
        saved.Should().Be(1);
        db.Entry(shipment).State.Should().Be(EntityState.Unchanged);
        await using var readScope = provider.CreateAsyncScope();
        (await readScope.ServiceProvider.GetRequiredService<ShipmentsDbContext>().Shipments.CountAsync(AbortToken))
            .Should()
            .Be(1);
    }

    public sealed record ShipmentPlaced;

    public sealed class Shipment : AggregateRoot<Guid>
    {
        public void Place() => AddDomainEvent(new ShipmentPlaced());
    }

    private sealed class FaultingCallbackHandler(ShipmentsDbContext db) : IDomainEventHandler<ShipmentPlaced>
    {
        public ValueTask HandleAsync(
            EventContext<ShipmentPlaced> context,
            CancellationToken cancellationToken = default
        )
        {
            db.UnitOfWork()!.OnCompleted(() => throw new InvalidOperationException("cache invalidation down"));

            return ValueTask.CompletedTask;
        }
    }

    public sealed class ShipmentsDbContext(DbContextOptions<ShipmentsDbContext> options) : HeadlessDbContext(options)
    {
        public DbSet<Shipment> Shipments => Set<Shipment>();

        public override string DefaultSchema => "";
    }
}
