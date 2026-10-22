// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Hosting;

/// <summary>What <c>AddDeployStepsOnStartup</c> does to the provisioned resources when the host starts.</summary>
[PublicAPI]
public enum ResourceStartupAction
{
    /// <summary>Leave the resources alone; the deployment provisions them.</summary>
    None = 0,

    /// <summary>Provision every resource, creating what is missing.</summary>
    Provision = 1,

    /// <summary>Provision every resource, then purge its data. For a test or demo host that starts from empty state.</summary>
    ProvisionAndPurge = 2,
}
