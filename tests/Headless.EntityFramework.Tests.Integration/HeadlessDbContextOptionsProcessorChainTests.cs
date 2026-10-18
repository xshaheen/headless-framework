// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework;
using Headless.Testing.Tests;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// The default processor chain and the built-in check that lets a save skip the audit recapture come from one list;
/// these tests fail if they drift apart.
/// </summary>
public sealed class HeadlessDbContextOptionsProcessorChainTests : TestBase
{
    [Fact]
    public async Task should_treat_every_processor_of_the_default_chain_as_built_in()
    {
        // given
        var options = new HeadlessDbContextOptions();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessDbContextServices();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        // when
        var chain = options.ResolveSaveEntryProcessors(scope.ServiceProvider).Select(p => p.GetType());

        // then
        options.HasOnlyBuiltInSaveEntryProcessors.Should().BeTrue();
        chain
            .Should()
            .Equal(
                typeof(HeadlessEntitySaveEntryProcessor),
                typeof(HeadlessAuditSaveEntryProcessor),
                typeof(HeadlessLocalEventSaveEntryProcessor),
                typeof(HeadlessMessageCollectorSaveEntryProcessor)
            );
    }

    [Fact]
    public void should_not_treat_a_consumer_processor_as_built_in()
    {
        // given
        var options = new HeadlessDbContextOptions();

        // when
        options.AddSaveEntryProcessor<ConsumerSaveEntryProcessor>(ServiceLifetime.Singleton);

        // then
        options.HasOnlyBuiltInSaveEntryProcessors.Should().BeFalse();
    }

    [Fact]
    public void should_stay_built_in_when_a_default_processor_is_removed()
    {
        // given
        var options = new HeadlessDbContextOptions();

        // when
        options.RemoveSaveEntryProcessor<HeadlessAuditSaveEntryProcessor>();

        // then
        options.HasOnlyBuiltInSaveEntryProcessors.Should().BeTrue();
    }

    private sealed class ConsumerSaveEntryProcessor : IHeadlessSaveEntryProcessor
    {
        public void Process(EntityEntry entry, HeadlessSaveEntryContext context) { }
    }
}
