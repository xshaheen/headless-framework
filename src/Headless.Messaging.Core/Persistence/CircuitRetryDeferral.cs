// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Persistence;

/// <summary>
/// Identifies one exact received-retry lease generation and how long until the circuit lets it be claimed again,
/// a delay the store adds to its own clock.
/// </summary>
internal readonly record struct CircuitRetryDeferral(MessageLeaseIdentity Identity, TimeSpan Delay);
