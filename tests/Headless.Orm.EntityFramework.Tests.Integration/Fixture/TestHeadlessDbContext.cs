using Headless.EntityFramework;
using Headless.EntityFramework.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Tests.Fixture;

public sealed class TestHeadlessDbContext(
    HeadlessDbContextServices services,
    RecordingHeadlessMessageDispatcher messageDispatcher,
    DbContextOptions options
) : HeadlessDbContext(services, options)
{
    public required DbSet<TestEntity> Tests { get; set; }

    public required DbSet<BasicEntity> Basics { get; set; }

    public List<EmitterDistributedMessages> EmittedDistributedMessages => messageDispatcher.EmittedDistributedMessages;

    public List<EmitterLocalMessages> EmittedLocalMessages => messageDispatcher.EmittedLocalMessages;

    public override string DefaultSchema => "";
}
