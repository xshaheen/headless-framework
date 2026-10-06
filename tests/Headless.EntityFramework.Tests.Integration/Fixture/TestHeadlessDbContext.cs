using Headless.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Tests.Fixtures;

namespace Tests.Fixture;

public sealed class TestHeadlessDbContext(
    RecordingHeadlessMessageDispatcher messageDispatcher,
    DbContextOptions options
) : HeadlessDbContext(options)
{
    public required DbSet<TestEntity> Tests { get; set; }

    public required DbSet<BasicEntity> Basics { get; set; }

    public required DbSet<LongKeyedEntity> LongKeyed { get; set; }

    public List<object> EmittedDistributedMessages => messageDispatcher.EmittedDistributedMessages;

    public List<object> EmittedLocalMessages => messageDispatcher.EmittedLocalMessages;

    public override string DefaultSchema => "";
}
