// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;

namespace Headless.Messaging.Transport;

/// <summary>
/// Internal non-blocking acceleration path for an immediate message whose durable row already committed.
/// A rejected enqueue is safe because durable retry pickup remains the recovery authority.
/// </summary>
internal interface ICommittedMessageDispatcher
{
    void EnqueueCommittedMessage(MediumMessage message);
}
