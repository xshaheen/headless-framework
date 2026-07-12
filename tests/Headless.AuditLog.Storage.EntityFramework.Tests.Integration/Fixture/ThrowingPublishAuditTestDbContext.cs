using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Tests.Fixture;

/// <summary>
/// Test DbContext whose distributed enqueue/local publish methods always throw. Combined with an
/// <c>IIntegrationEventEmitter</c> entity this forces the runtime down the path where
/// audit entries have already been persisted but the post-persist enqueue raises. The
/// catch block in the save pipeline must discard tracking for the persisted audit entries
/// so a retry on the same change tracker doesn't double-insert.
/// </summary>
public sealed class ThrowingPublishAuditTestDbContext(
    HeadlessDbContextServices services,
    DbContextOptions options,
    IOptions<AuditLogStorageOptions> auditLogStorage
) : AuditTestDbContext(services, options, auditLogStorage)
{
    public const string PublishFailureMessage = "Simulated publish failure.";

    public DbSet<EmittingOrder> EmittingOrders => Set<EmittingOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<EmittingOrder>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            // GetIntegrationEvents is method-based; backing field is auto-excluded by EF.
        });
    }
}

public sealed class ThrowingHeadlessMessageDispatcher : IHeadlessOutboxDispatcher
{
    public Task DispatchAsync(
        IReadOnlyList<IIntegrationEvent> integrationEvents,
        CancellationToken cancellationToken = default
    ) => throw new InvalidOperationException(_PublishFailureMessage);

    public void Dispatch(IReadOnlyList<IIntegrationEvent> integrationEvents) =>
        throw new InvalidOperationException(_PublishFailureMessage);

    private const string _PublishFailureMessage = ThrowingPublishAuditTestDbContext.PublishFailureMessage;
}
