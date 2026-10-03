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

internal sealed class PreparedPublishMessage
{
    public required string MessageName { get; init; }

    public required DateTimeOffset PublishAt { get; init; }

    public required Message Message { get; init; }

    public required MessageLane Lane { get; init; }

    public required Type DeclaredMessageType { get; init; }

    public required Type ConcreteMessageType { get; init; }
}
