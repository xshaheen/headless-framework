// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>Mutable runtime call-order flags tracked during application startup.</summary>
/// <remarks>
/// Kept separate from <see cref="HeadlessServiceDefaultsOptions"/> so that user-facing configuration
/// is not mixed with internal state that is written from multiple call sites at startup.
/// </remarks>
internal sealed class HeadlessStartupState
{
    public volatile bool UseHeadlessCalled;

    public volatile bool MapHeadlessEndpointsCalled;

    public volatile bool UseStatusCodesRewriterCalled;
}
