// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Messaging.Internal;

namespace Headless.Messaging.Configuration;

/// <summary>Frozen provider capability authority used by bootstrap and direct publisher gates.</summary>
[PublicAPI]
public interface IMessagingCapabilityModel
{
    /// <summary>The inert declarations contributed before the service provider was built.</summary>
    IReadOnlyList<MessagingProviderCapabilities> DeclaredCapabilities { get; }

    /// <summary>Role/provider aggregates produced while freezing the model.</summary>
    IReadOnlyList<MessagingProviderCapabilities> Providers { get; }

    /// <summary>Always true for a composed model.</summary>
    bool IsFrozen { get; }

    /// <summary>The inbox tier declared by the configured storage provider, when one is present.</summary>
    MessagingInboxCapabilityTier? InboxCapability { get; }

    /// <summary>Returns whether a role supports a semantic lane.</summary>
    bool Supports(MessageLane lane, MessagingProviderRole role);
}
