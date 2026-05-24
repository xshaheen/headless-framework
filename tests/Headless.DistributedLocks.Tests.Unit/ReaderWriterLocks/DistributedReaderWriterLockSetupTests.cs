// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tests.Fakes;

namespace Tests.ReaderWriterLocks;

public sealed class DistributedReaderWriterLockSetupTests : TestBase
{
    [Fact]
    public void should_register_reader_writer_lock_provider_as_singleton()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedReaderWriterLock<FakeReaderWriterLockStorage>(_ => { });
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IDistributedReaderWriterLockProvider>().Should().NotBeNull();
        provider
            .GetRequiredService<IDistributedReaderWriterLockProvider>()
            .Should()
            .BeSameAs(provider.GetRequiredService<IDistributedReaderWriterLockProvider>());
    }

    [Fact]
    public void should_be_idempotent_for_repeated_reader_writer_setup_calls()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddDistributedReaderWriterLock<FakeReaderWriterLockStorage>(_ => { });
        services.AddDistributedReaderWriterLock<FakeReaderWriterLockStorage>(_ => { });

        // then
        services.Count(x => x.ServiceType == typeof(IDistributedReaderWriterLockProvider)).Should().Be(1);
        services.Count(x => x.ServiceType == typeof(DistributedReaderWriterLockProvider)).Should().Be(1);
    }
}
