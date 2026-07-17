using Headless.Domain;
using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Tests.Fixtures;

namespace Tests.Fixture;

public sealed class TestHeadlessDbContext(
    HeadlessDbContextServices services,
    RecordingHeadlessMessageDispatcher messageDispatcher,
    DbContextOptions options
) : HeadlessDbContext(services, options)
{
    public required DbSet<TestEntity> Tests { get; set; }

    public required DbSet<BasicEntity> Basics { get; set; }

    public required DbSet<LongKeyedEntity> LongKeyed { get; set; }

    public List<IIntegrationEvent> EmittedDistributedMessages => messageDispatcher.EmittedDistributedMessages;

    public List<IDomainEvent> EmittedLocalMessages => messageDispatcher.EmittedLocalMessages;

    public override string DefaultSchema => "";
}
