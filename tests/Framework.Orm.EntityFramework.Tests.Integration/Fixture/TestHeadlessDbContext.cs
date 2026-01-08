using Framework.Orm.EntityFramework.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Tests.Fixture;

public sealed class TestHeadlessDbContext(IHeadlessEntityModelProcessor entityProcessor, DbContextOptions options)
    : HeadlessDbContext(entityProcessor, options)
{
    public DbSet<TestEntity> Tests { get; set; }

    public DbSet<BasicEntity> Basics { get; set; }

    public List<EmitterDistributedMessages> EmittedDistributedMessages { get; } = [];

    public List<EmitterLocalMessages> EmittedLocalMessages { get; } = [];

    public override string DefaultSchema => "";

    protected override Task PublishMessagesAsync(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedDistributedMessages.AddRange(emitters);

        return Task.CompletedTask;
    }

    protected override void PublishMessages(
        List<EmitterDistributedMessages> emitters,
        IDbContextTransaction currentTransaction
    )
    {
        EmittedDistributedMessages.AddRange(emitters);
    }

    protected override Task PublishMessagesAsync(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction,
        CancellationToken cancellationToken
    )
    {
        EmittedLocalMessages.AddRange(emitters);

        return Task.CompletedTask;
    }

    protected override void PublishMessages(
        List<EmitterLocalMessages> emitters,
        IDbContextTransaction currentTransaction
    )
    {
        EmittedLocalMessages.AddRange(emitters);
    }
}
