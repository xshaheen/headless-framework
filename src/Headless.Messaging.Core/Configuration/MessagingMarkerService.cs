// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Configuration;

/// <summary>
/// A marker service used internally to verify that the messaging service has been registered on a <see cref="IServiceCollection"/>.
/// This service is registered when <c>AddHeadlessMessaging()</c> is called during dependency injection setup.
/// </summary>
internal sealed class MessagingMarkerService
{
    /// <summary>
    /// Gets or sets the name identifier for the messaging service.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the version of the messaging assembly.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagingMarkerService"/> class with the specified name.
    /// Automatically retrieves and stores the messaging assembly version information.
    /// </summary>
    /// <param name="name">The name identifier for the messaging service.</param>
    public MessagingMarkerService(string name)
    {
        Name = name;

        try
        {
            Version = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location).ProductVersion!;
        }
#pragma warning disable ERP022 // Version is diagnostic; any failure, such as an empty Location in single-file apps, falls back to N/A.
        catch
        {
            Version = "N/A"; // Fallback in case of any error retrieving version info
        }
#pragma warning restore ERP022
    }
}
