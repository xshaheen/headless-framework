// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;

namespace Headless.Messaging.Processor;

/// <summary>One received message handed to a sequential consumer's sequencer.</summary>
/// <param name="Message">The admitted message.</param>
/// <param name="Descriptor">The consumer it runs on.</param>
/// <param name="RetryAttempt">The claim of a retry pickup, or <see langword="null"/> for a first dispatch.</param>
internal sealed record SequencedWork(
    MediumMessage Message,
    ConsumerExecutorDescriptor Descriptor,
    RetryDispatchAttempt? RetryAttempt
)
{
    /// <summary>Whether a pass already saw this work waiting; its row is first held on the pass after.</summary>
    internal bool SeenWaiting { get; set; }

    /// <summary>The passes left before this work's row is held again.</summary>
    internal int PassesUntilHold { get; set; }
}
