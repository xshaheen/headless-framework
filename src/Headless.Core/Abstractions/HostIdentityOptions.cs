// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.Abstractions;

/// <summary>Overrides for <see cref="IHostIdentityAccessor"/>. Unset members are discovered.</summary>
[PublicAPI]
public sealed class HostIdentityOptions
{
    /// <summary>Application name to report instead of the entry assembly title.</summary>
    public string? ApplicationName { get; set; }

    /// <summary>
    /// Host name to report instead of the discovered one. Set it when the deployment has a stable identity the
    /// environment does not expose, such as a StatefulSet ordinal passed through configuration.
    /// </summary>
    public string? HostName { get; set; }
}
