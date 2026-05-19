// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Idempotency;

[PublicAPI]
public enum InFlightStrategy
{
    Reject = 0,
    WaitAndReplay = 1,
}
