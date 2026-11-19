// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Internal;

/// <summary>One <c>setup.AddTransport(name)</c> call, recorded so the runtime can find every named transport.</summary>
/// <param name="Name">The transport's name, as the application spelled it.</param>
internal sealed record MessagingTransportRegistration(string Name);
