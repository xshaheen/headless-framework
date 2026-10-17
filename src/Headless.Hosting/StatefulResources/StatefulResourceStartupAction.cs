// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>What <c>AddHeadlessResourceSetupOnStartup</c> does to the stateful resources when the host starts.</summary>
[PublicAPI]
public enum StatefulResourceStartupAction
{
    /// <summary>Set up every resource, creating what is missing.</summary>
    SetupOnly = 0,

    /// <summary>Set up every resource, then clear its data. For a test or demo host that starts from empty state.</summary>
    ResetState = 1,
}
