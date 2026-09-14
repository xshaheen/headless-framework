// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Testing.Internal;

/// <summary>Headers the recording transports stamp on outgoing messages for the recording consume pipeline.</summary>
internal static class RecordingHeaders
{
    /// <summary>
    /// The <see cref="MessageObservationStore.Generation"/> at send time. A message consumed under a later generation
    /// was sent before a reset cleared the round it belongs to; its Published record is gone and its observations
    /// belong to no current test.
    /// </summary>
    public const string ResetGeneration = "headless-testing-reset-generation";
}
