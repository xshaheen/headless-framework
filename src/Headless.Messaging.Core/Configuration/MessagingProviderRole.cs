// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Configuration;

/// <summary>Identifies the messaging subsystem role supplied by a provider contribution.</summary>
[PublicAPI]
public enum MessagingProviderRole
{
    Transport = 0,
    Storage = 1,
    Coordination = 2,
}
