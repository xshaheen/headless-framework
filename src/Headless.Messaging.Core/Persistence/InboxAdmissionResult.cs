// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Persistence;

/// <summary>Result of the atomic admission decision.</summary>
[PublicAPI]
public sealed record InboxAdmissionResult(InboxAdmissionDisposition Disposition, MediumMessage Message)
{
    public bool ShouldDispatch => Disposition is InboxAdmissionDisposition.Winner;
}
