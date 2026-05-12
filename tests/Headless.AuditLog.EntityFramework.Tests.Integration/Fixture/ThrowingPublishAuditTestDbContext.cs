using Headless.EntityFramework;
using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests.Fixture;

/// <summary>
/// Test DbContext whose distributed enqueue/local publish methods always throw. Combined with an
/// <c>IDistributedMessageEmitter</c> entity this forces the runtime down the path where
/// audit entries have already been persisted but the post-persist enqueue raises. The
/// catch block in the save pipeline must discard tracking for the persisted audit entries
/// so a retry on the same change tracker doesn't double-insert.
/// </summary>
public sealed class ThrowingPublishAuditTestDbContext(HeadlessDbContextServices services, DbContextOptions options)
    : AuditTestDbContext(services, options)
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
            // GetDistributedMessages is method-based; backing field is auto-excluded by EF.
        });
    }
}

public sealed class ThrowingHeadlessMessageDispatcher : IHeadlessMessageDispatcher
{
    public Task PublishLocalAsync(
        IReadOnlyList<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException(PublishFailureMessage);

    public void PublishLocal(IReadOnlyList<EmitterLocalMessages> emitters, IDbContextTransaction currentTransaction) =>
        throw new InvalidOperationException(PublishFailureMessage);

    public Task EnqueueDistributedAsync(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException(PublishFailureMessage);

    public void EnqueueDistributed(
        IReadOnlyList<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    ) => throw new InvalidOperationException(PublishFailureMessage);

    private const string PublishFailureMessage = ThrowingPublishAuditTestDbContext.PublishFailureMessage;
}
