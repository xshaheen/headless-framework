// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Messaging;

/// <summary>Identifies which messaging span type an <see cref="IActivityTagEnricher"/> is being called for.</summary>
[PublicAPI]
public enum MessagingEventKind
{
    /// <summary>The outbox persist span (<c>message.persist</c>).</summary>
    Persist = 0,

    /// <summary>The broker publish span (<c>message.publish</c>).</summary>
    Publish = 1,

    /// <summary>The broker consume span (<c>message.consume</c>).</summary>
    Consume = 2,

    /// <summary>The subscriber handler invocation span (<c>subscriber.invoke</c>).</summary>
    SubscriberInvoke = 3,
}
