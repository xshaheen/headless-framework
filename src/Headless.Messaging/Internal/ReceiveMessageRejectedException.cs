// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>
/// Carries the caller-supplied reason of an explicit <see cref="ReceiveContext.Reject(string, Exception?)"/>
/// into the poison-on-arrival row and the exhausted callback when no cause exception was provided.
/// The type name is what operators see in the stored exception header, so it is distinct from
/// framework faults.
/// </summary>
#pragma warning disable CA1064 // Deliberately internal: framework fault taxonomy is not public contract; callers catch the base Exception via the outcome table.
internal sealed class ReceiveMessageRejectedException(string reason) : Exception(reason);
#pragma warning restore CA1064
