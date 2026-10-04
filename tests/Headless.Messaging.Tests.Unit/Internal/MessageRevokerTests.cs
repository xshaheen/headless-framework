// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Internal;

public sealed class MessageRevokerTests : TestBase
{
    [Theory]
    [InlineData(MessageRevocationResult.NotFound)]
    [InlineData(MessageRevocationResult.Revoked)]
    [InlineData(MessageRevocationResult.AttemptReserved)]
    public async Task should_resolve_revoker_and_forward_handle_token_and_result(MessageRevocationResult result)
    {
        var storage = Substitute.For<IDataStorage, IMessageRevocationStorage>();
        var capability = (IMessageRevocationStorage)storage;
        var handle = Guid.NewGuid();
        capability.RevokeAsync(handle, AbortToken).Returns(result);
        var services = new ServiceCollection();
        services.AddHeadlessMessaging(_ => { });
        services.AddSingleton(storage);
        await using var provider = services.BuildServiceProvider();

        var revoker = provider.GetRequiredService<IMessageRevoker>();
        (await revoker.RevokeAsync(handle, AbortToken)).Should().Be(result);
        await capability.Received(1).RevokeAsync(handle, AbortToken);
    }

    [Fact]
    public async Task should_reject_cancelled_request_before_calling_storage()
    {
        var storage = Substitute.For<IDataStorage, IMessageRevocationStorage>();
        var revoker = new MessageRevoker(storage);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        Func<Task> action = async () => await revoker.RevokeAsync(Guid.NewGuid(), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        storage.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_name_provider_when_revocation_is_unsupported()
    {
        var storage = Substitute.For<IDataStorage>();
        var revoker = new MessageRevoker(storage);

        Func<Task> action = async () => await revoker.RevokeAsync(Guid.NewGuid(), AbortToken);
        var error = await action.Should().ThrowAsync<NotSupportedException>();
        error.Which.Message.Should().Contain(storage.GetType().FullName!);
    }
}
