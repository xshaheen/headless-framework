// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Redis;
using Headless.Testing.Tests;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Tests;

public sealed class RedisPubSubConnectionProviderTests : TestBase
{
    private static readonly IOptions<RedisPubSubOptions> _Options = Options.Create(
        new RedisPubSubOptions { Configuration = ConfigurationOptions.Parse("localhost:6379") }
    );

    [Fact]
    public async Task should_allow_multiple_dispose_async_calls()
    {
        // given
        await using var provider = new RedisPubSubConnectionProvider(_Options);

        // when
        var action = async () =>
        {
            await provider.DisposeAsync();
            await provider.DisposeAsync();
            await provider.DisposeAsync();
        };

        // then
        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task should_throw_when_connect_called_after_dispose()
    {
        // given
        await using var provider = new RedisPubSubConnectionProvider(_Options);
        await provider.DisposeAsync();

        // when
        var action = async () => await provider.ConnectAsync(AbortToken);

        // then
        await action.Should().ThrowAsync<ObjectDisposedException>();
    }
}
