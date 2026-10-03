// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.Abstractions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Registration;
using Headless.Messaging.RequestReply;
using Headless.MultiTenancy;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IMessagePublishRequestFactory
{
    /// <summary>
    /// Creates a request for an explicitly resolved not-before instant. <paramref name="delayTime"/> is nullable
    /// because an absolute schedule produces a <paramref name="publishAt"/> with no relative delay.
    /// </summary>
    PreparedPublishMessage Create(
        object? contentObj,
        Type declaredMessageType,
        MessageOptions? options,
        TimeSpan? delayTime,
        DateTimeOffset publishAt,
        MessageLane lane
    );

    PreparedPublishMessage Create(
        object? contentObj,
        Type declaredMessageType,
        MessageOptions? options = null,
        TimeSpan? delayTime = null,
        MessageLane lane = MessageLane.Bus
    );

    PreparedPublishMessage Create<T>(
        T? contentObj,
        MessageOptions? options = null,
        TimeSpan? delayTime = null,
        MessageLane lane = MessageLane.Bus
    );

    /// <summary>
    /// Creates a Queue request that carries <paramref name="request"/>'s protocol headers. They are stamped after the
    /// custom headers are validated, and the message id is always framework-generated, so neither the caller nor
    /// publish middleware can change the request id, the reply address, the deadline, or the message id.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="options"/> names a callback, or carries a reserved or invalid header.
    /// </exception>
    PreparedPublishMessage CreateRequest(
        object? contentObj,
        Type declaredMessageType,
        MessageOptions? options,
        RequestStamp request
    );

    /// <summary>
    /// Resolves the contract name and version a message of <paramref name="messageType"/> carries on
    /// <paramref name="lane"/>, by the same rules a publish uses: the registered name mapping or the naming convention,
    /// with the host's message-name prefix, and the declared contract version or the initial one.
    /// </summary>
    /// <exception cref="InvalidOperationException">No name mapping or convention names the type.</exception>
    (string MessageName, string ContractVersion) ResolveContract(Type messageType, MessageLane lane);
}
