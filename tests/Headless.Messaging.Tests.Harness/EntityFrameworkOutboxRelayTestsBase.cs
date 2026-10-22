// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;
using Headless.EntityFramework;
using Headless.Messaging;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Guarantees;
using Tests.Helpers;

namespace Tests;

/// <summary>
/// The EF Core outbox bridge end to end: an integration event an aggregate raises is written to the outbox in the save's
/// transaction, and the relay sends it through the real broker to its consumer only once that save commits.
/// </summary>
[PublicAPI]
public abstract class EntityFrameworkOutboxRelayTestsBase : TestBase
{
    private static readonly TimeSpan _DeliveryTimeout = TimeSpan.FromSeconds(30);

    // How long a test watches for a copy that should not arrive after the expected one did.
    private static readonly TimeSpan _NoDuplicateWindow = TimeSpan.FromSeconds(3);

    private readonly string _prefix = $"relay-{Guid.NewGuid():N}"[..18];

    /// <summary>Configures the broker the relay sends to.</summary>
    protected abstract void ConfigureTransport(MessagingSetupBuilder setup);

    /// <summary>Configures the messaging storage on the database <see cref="UseDatabase"/> points at.</summary>
    protected abstract void ConfigureStorage(MessagingSetupBuilder setup);

    /// <summary>Points the application's context at the messaging storage's database.</summary>
    protected abstract void UseDatabase(DbContextOptionsBuilder options);

    /// <summary>Registers the EF Core save pipeline together with the bridge that writes integration events to the outbox.</summary>
    protected abstract void AddOutboxBridge(IServiceCollection services);

    [Fact]
    [Guarantee("MSG-PUB-09")]
    public async Task should_relay_an_integration_event_committed_by_a_save_to_its_consumer_once()
    {
        // given
        await using var host = await _StartHostAsync();
        var collector = host.GetRequiredService<MessageCollector<RelayedOrderPlaced>>();
        var marker = Guid.NewGuid().ToString("N");

        // when
        await using (var scope = host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
            db.Orders.Add(RelayOrder.Place(marker));
            await db.SaveChangesAsync(AbortToken);
        }

        // then
        (await collector.WaitForCountAsync(1, _DeliveryTimeout, AbortToken))
            .Should()
            .BeTrue("the committed event is relayed through the broker");
        (await collector.WaitForCountAsync(2, _NoDuplicateWindow, AbortToken)).Should().BeFalse();
        collector.ReceivedMessages.Should().ContainSingle().Which.Marker.Should().Be(marker);
    }

    [Fact]
    [Guarantee("MSG-PUB-09")]
    public async Task should_never_relay_an_integration_event_whose_save_rolled_back()
    {
        // given
        await using var host = await _StartHostAsync();
        var collector = host.GetRequiredService<MessageCollector<RelayedOrderPlaced>>();
        var rolledBack = Guid.NewGuid().ToString("N");
        var committed = Guid.NewGuid().ToString("N");

        // when — the save writes its event row inside a unit that then rolls back
        await using (var scope = host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
            await using var unit = await scope
                .ServiceProvider.GetRequiredService<IUnitOfWorkFactory>()
                .BeginAsync(db, AbortToken);
            db.Orders.Add(RelayOrder.Place(rolledBack));
            await db.SaveChangesAsync(AbortToken);
            await unit.RollbackAsync();
        }

        // A committed save after the rollback marks the point by which a leaked row would have been relayed too.
        await using (var scope = host.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
            db.Orders.Add(RelayOrder.Place(committed));
            await db.SaveChangesAsync(AbortToken);
        }

        // then
        (await collector.WaitForCountAsync(1, _DeliveryTimeout, AbortToken))
            .Should()
            .BeTrue();
        (await collector.WaitForCountAsync(2, _NoDuplicateWindow, AbortToken)).Should().BeFalse();
        collector.ReceivedMessages.Should().ContainSingle().Which.Marker.Should().Be(committed);
    }

    private async Task<ServiceProvider> _StartHostAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(LoggerProvider).SetMinimumLevel(LogLevel.Debug));
        AddOutboxBridge(services);
        services.AddHeadlessMessaging(setup =>
        {
            ConfigureTransport(setup);
            ConfigureStorage(setup);
            setup.Options.MinimumInboxGuarantee = InboxGuarantee.Durable;
            setup.Options.MessageNamePrefix = _prefix;
        });
        services.ConfigureMessaging(messaging =>
        {
            messaging.Message<RelayedOrderPlaced>("orders.placed");
            messaging.AddModule<EntityFrameworkOutboxRelayModule>();
        });
        services.AddSingleton<MessageCollector<RelayedOrderPlaced>>();
        services.AddDbContext<RelayDbContext>(UseDatabase);

        var provider = services.BuildServiceProvider();

        try
        {
            await provider.ApplyMessagingSchemaAsync(AbortToken);

            await using (var scope = provider.CreateAsyncScope())
            {
                // Every test builds its own provider over the collection's database; recreate only this suite's table.
                var db = scope.ServiceProvider.GetRequiredService<RelayDbContext>();
                await db.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS "RelayOrders";""", AbortToken);
                await db.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(AbortToken);
            }

            await provider.GetRequiredService<IBootstrapper>().BootstrapAsync(AbortToken);

            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }
}

/// <summary>The integration event the relay suite's aggregate raises.</summary>
[PublicAPI]
public sealed record RelayedOrderPlaced(string Marker);

/// <summary>An aggregate that raises <see cref="RelayedOrderPlaced"/> when it is placed.</summary>
[PublicAPI]
public sealed class RelayOrder : AggregateRoot, IEntity<Guid>
{
    public Guid Id { get; private init; } = Guid.NewGuid();

    public required string Marker { get; init; }

    public static RelayOrder Place(string marker)
    {
        var order = new RelayOrder { Marker = marker };
        order.AddIntegrationEvent(new RelayedOrderPlaced(marker));

        return order;
    }

    public override IReadOnlyList<object> GetKeys() => [Id];
}

/// <summary>The application context whose saves the outbox bridge enlists in.</summary>
[PublicAPI]
public sealed class RelayDbContext(DbContextOptions<RelayDbContext> options) : HeadlessDbContext(options)
{
    public DbSet<RelayOrder> Orders => Set<RelayOrder>();

    public override string DefaultSchema => "";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<RelayOrder>().ToTable("RelayOrders").Property(order => order.Id).ValueGeneratedNever();
    }
}

/// <summary>The consumer the relay suite's host registers.</summary>
public sealed class EntityFrameworkOutboxRelayModule : IMessagingModule
{
    public static void Register(MessagingCatalogBuilder catalog)
    {
        catalog.AddBusConsumer<MessageCollector<RelayedOrderPlaced>, RelayedOrderPlaced>(
            "tests.integration.relayed-order-placed",
            everyInstance: false,
            TestConsumerDispatch.FromServices<MessageCollector<RelayedOrderPlaced>, RelayedOrderPlaced>()
        );
    }
}
