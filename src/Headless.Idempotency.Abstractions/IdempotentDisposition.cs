// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Idempotency;

/// <summary>What an admission decided for a keyed operation.</summary>
[PublicAPI]
public enum IdempotentDisposition
{
    /// <summary>
    /// The caller now owns the operation under a fenced lease: run it, then complete or release the admission.
    /// </summary>
    Admitted = 0,

    /// <summary>A live attempt owns the operation; the caller got nothing and may retry later.</summary>
    InFlight = 1,

    /// <summary>The operation already completed within its retention window; its stored result is returned.</summary>
    Replay = 2,

    /// <summary>
    /// The key is in use with a different request fingerprint, or its stored result carries a different contract than
    /// the caller expects. Nothing was written.
    /// </summary>
    Conflict = 3,
}
