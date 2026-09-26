// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;

namespace Headless.Idempotency;

/// <summary>The lease a provider granted on a locked record when it admitted a new attempt.</summary>
/// <param name="Generation">The generation drawn for the attempt, after the record's row lock was held.</param>
/// <param name="LeaseExpiresAt">The attempt's lease expiry, from the database clock.</param>
[PublicAPI]
[StructLayout(LayoutKind.Auto)]
public readonly record struct IdempotencyRecordGrant(long Generation, DateTimeOffset LeaseExpiresAt);
