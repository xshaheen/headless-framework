// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

[PublicAPI]
public enum OversizeBehavior
{
    Reject = 0,
    PassThrough = 1,
}
