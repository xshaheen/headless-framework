// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>Internal bounded-shutdown contract for built-in messaging processors.</summary>
internal interface IProcessingServerShutdown
{
    /// <summary>Stops the processor using only the supplied remaining end-to-end shutdown budget.</summary>
    ValueTask StopAsync(TimeSpan timeout);
}
