// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>
/// Selects a bounded page of rows whose persisted lane value is not recognized by this runtime.
/// </summary>
[PublicAPI]
public sealed class UnknownLaneMessageQuery
{
    /// <summary>Gets or sets whether to inspect the published or received message table.</summary>
    public MessageType MessageType { get; set; }

    /// <summary>Gets or sets the one-based page number. Values below one are normalized to one.</summary>
    public int CurrentPage { get; set; } = 1;

    /// <summary>Gets or sets the page size. The default is 50 and the maximum is 200.</summary>
    public int PageSize { get; set; } = 50;
}
