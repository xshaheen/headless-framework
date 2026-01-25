// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Testing.Tests;
using Headless.Messaging.InMemoryStorage;

namespace Tests;

public sealed class InMemoryStorageInitializerTests : TestBase
{
    private readonly InMemoryStorageInitializer _sut = new();

    [Fact]
    public void should_return_published_messages_table_name()
    {
        // when
        var result = _sut.GetPublishedTableName();

        // then
        result.Should().Be("PublishedMessages");
    }

    [Fact]
    public void should_return_received_messages_table_name()
    {
        // when
        var result = _sut.GetReceivedTableName();

        // then
        result.Should().Be("ReceivedMessages");
    }

    [Fact]
    public void should_return_empty_lock_table_name()
    {
        // when
        var result = _sut.GetLockTableName();

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task should_complete_initialize_without_error()
    {
        // when
        var act = () => _sut.InitializeAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }
}
