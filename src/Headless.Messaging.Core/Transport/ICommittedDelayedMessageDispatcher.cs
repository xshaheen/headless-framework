// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;

namespace Headless.Messaging.Transport;

/// <summary>
/// Internal queue-only path for messages whose durable delayed-state transition already committed.
/// Keeping this separate from <see cref="IDispatcher"/> prevents consumers from bypassing storage authority.
/// </summary>
internal interface ICommittedDelayedMessageDispatcher
{
    void EnqueueCommittedDelayedMessage(MediumMessage message);
}
