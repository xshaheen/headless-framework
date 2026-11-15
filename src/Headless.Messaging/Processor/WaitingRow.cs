// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Processor;

/// <summary>One stored row whose delivery waits on this host, as <see cref="WaitingRows"/> tracks it.</summary>
/// <param name="message">The row's message; a hold renews its lease identity in place.</param>
internal sealed class WaitingRow(MediumMessage message)
{
    public MediumMessage Message { get; } = message;

    /// <summary>Whether a pass already saw this row waiting; it is first held on the pass after.</summary>
    public bool SeenWaiting { get; set; }

    /// <summary>The passes left before this row is held again.</summary>
    public int PassesUntilHold { get; set; }
}
