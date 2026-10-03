// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Marker contract for response envelopes that carry a human-readable status message.</summary>
public interface IMessageEnvelope
{
    /// <summary>The human-readable status message descriptor.</summary>
    MessageDescriptor Message { get; }
}
