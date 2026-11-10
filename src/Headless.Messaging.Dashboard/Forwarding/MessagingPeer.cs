// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Dashboard;

/// <summary>A Messaging node discovery lists, other than this one.</summary>
/// <param name="Name">The node's name, which is also the first part of every id it serves.</param>
/// <param name="Addresses">The base address of each discovered instance of the node, in discovery order.</param>
internal sealed record MessagingPeer(string Name, IReadOnlyList<Uri> Addresses);
