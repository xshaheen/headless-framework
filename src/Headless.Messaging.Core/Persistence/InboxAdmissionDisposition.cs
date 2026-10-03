// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>The durable outcome of converging one transport delivery on an inbox generation.</summary>
[PublicAPI]
public enum InboxAdmissionDisposition
{
    Winner = 0,
    InFlightDuplicate = 1,
    SucceededDuplicate = 2,
    TerminalFailedDuplicate = 3,
}
