// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Endpoint metadata that marks a <c>POST</c> of the module's API as a read: it changes nothing and needs no operator,
/// so a call another node forwards to it is admitted at view access, as a <c>GET</c> is.
/// </summary>
internal sealed class MessagingReadEndpoint
{
    private MessagingReadEndpoint() { }

    public static MessagingReadEndpoint Instance { get; } = new();
}
