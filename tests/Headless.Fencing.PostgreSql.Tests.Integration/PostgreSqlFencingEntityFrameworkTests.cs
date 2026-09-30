// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Fencing;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// An EF-begun unit (<c>RunAsync(db, …)</c>) reaches <c>unit.Leases</c> through <c>db.UnitOfWork()</c> on the same
/// relational resource as a raw-ADO unit: a fenced settlement commits together with the entity it guards, and a
/// failed block rolls both back.
/// </summary>
[Collection<PostgreSqlFencingFixture>]
public sealed class PostgreSqlFencingEntityFrameworkTests(PostgreSqlFencingFixture fixture) : TestBase
{
    [Fact]
    public async Task should_commit_a_fenced_settlement_with_the_units_writes_and_roll_both_back_on_failure()
    {
        var resource = $"order-{Guid.NewGuid():N}";
        await fixture.ExecuteAsync(
            "CREATE TABLE IF NOT EXISTS shipment_probe (id bigserial PRIMARY KEY, resource text NOT NULL, generation bigint NOT NULL)",
            AbortToken
        );
        await using var provider = await _BuildProviderAsync();
        var leases = provider.GetRequiredService<IFencedLeases>();

        var failed = (await leases.GrantAsync("shipment", resource, TimeSpan.FromMinutes(5), AbortToken)).Lease!;

        var failing = async () =>
            await _RunAsync(
                provider,
                async (db, ct) =>
                {
                    var unit = db.UnitOfWork()!;
                    await unit.Leases.FenceAsync(failed, ct);
                    db.Shipments.Add(new ShipmentProbe { Resource = resource, Generation = failed.Generation });
                    await db.SaveChangesAsync(ct);
                    (await unit.Leases.SettleAsync(failed, ct)).Should().Be(LeaseSettlementStatus.Settled);

                    throw new InvalidOperationException("business rule failed after settling");
                }
            );

        await failing.Should().ThrowAsync<InvalidOperationException>().WithMessage("business rule failed*");
        (await _CountShipmentsAsync(resource)).Should().Be(0);
        (await fixture.ReadLeaseAsync(new LeaseKey("", "shipment", resource), AbortToken))!
            .State.Should()
            .Be(StoredLeaseState.Active, "the settlement rolled back with the block");

        await _RunAsync(
            provider,
            async (db, ct) =>
            {
                var unit = db.UnitOfWork()!;
                await unit.Leases.FenceAsync(failed, ct);
                db.Shipments.Add(new ShipmentProbe { Resource = resource, Generation = failed.Generation });
                await db.SaveChangesAsync(ct);
                (await unit.Leases.SettleAsync(failed, ct)).Should().Be(LeaseSettlementStatus.Settled);

                unit.IsRetryPrevented.Should().BeFalse("the owned block replays by re-running the settlement");
            }
        );

        (await _CountShipmentsAsync(resource)).Should().Be(1);
        (await fixture.ReadLeaseAsync(new LeaseKey("", "shipment", resource), AbortToken))!
            .State.Should()
            .Be(StoredLeaseState.Settled);
    }

    private async Task<ServiceProvider> _BuildProviderAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkUnitOfWork();
        services.AddDbContext<ShipmentDbContext>(options => options.UseNpgsql(fixture.ConnectionString));
        services.AddHeadlessFencing(fixture.ConfigureProvider);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(AbortToken);
        }

        return provider;
    }

    private async Task _RunAsync(ServiceProvider provider, Func<ShipmentDbContext, CancellationToken, Task> operation)
    {
        // A fresh scope, and so a fresh context, per unit: a failed block leaves its entity tracked.
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShipmentDbContext>();
        var factory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();

        await factory.RunAsync(db, (_, ct) => operation(db, ct), cancellationToken: AbortToken);
    }

    private Task<int> _CountShipmentsAsync(string resource)
    {
        return fixture.ScalarAsync(
            "SELECT count(*) FROM shipment_probe WHERE resource = @resource",
            AbortToken,
            ("resource", resource)
        );
    }

    public sealed class ShipmentDbContext(DbContextOptions<ShipmentDbContext> options) : DbContext(options)
    {
        public DbSet<ShipmentProbe> Shipments => Set<ShipmentProbe>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ShipmentProbe>(entity =>
            {
                entity.ToTable("shipment_probe");
                entity.HasKey(row => row.Id);
                entity.Property(row => row.Id).HasColumnName("id");
                entity.Property(row => row.Resource).HasColumnName("resource");
                entity.Property(row => row.Generation).HasColumnName("generation");
            });
        }
    }

    public sealed class ShipmentProbe
    {
        public long Id { get; init; }

        public required string Resource { get; init; }

        public long Generation { get; init; }
    }
}
