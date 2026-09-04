// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>Internal bounded-shutdown contract for built-in messaging processors.</summary>
internal interface IProcessingServerShutdown
{
    /// <summary>
    /// Prevents new work from being accepted or picked up. Implementations must return without
    /// waiting for in-flight work so every processor can be quiesced before draining begins.
    /// </summary>
    void Quiesce();

    /// <summary>Drains the processor using only the supplied remaining end-to-end shutdown budget.</summary>
    ValueTask StopAsync(TimeSpan timeout);
}
