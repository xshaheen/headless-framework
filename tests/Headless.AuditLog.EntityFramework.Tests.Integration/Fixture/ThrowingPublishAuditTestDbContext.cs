using Headless.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests.Fixture;

/// <summary>
/// Test DbContext whose distributed/local publish methods always throw. Combined with an
/// <c>IDistributedMessageEmitter</c> entity this forces the runtime down the path where
/// audit entries have already been persisted but the post-persist publish raises. The
/// catch block in <c>HeadlessDbContextRuntime._SaveWithinTransactionAsync</c> must detach
/// the persisted audit entries so a retry on the same change tracker doesn't double-insert.
/// </summary>
public sealed class ThrowingPublishAuditTestDbContext(
    IHeadlessEntityModelProcessor entityProcessor,
    DbContextOptions options
) : AuditTestDbContext(entityProcessor, options)
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

    protected override Task PublishMessagesAsync(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException(PublishFailureMessage);

    protected override Task PublishMessagesAsync(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException(PublishFailureMessage);
}
