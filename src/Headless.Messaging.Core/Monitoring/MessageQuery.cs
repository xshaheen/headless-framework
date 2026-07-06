// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

/// <summary>
/// Filter and pagination parameters passed to <see cref="IMonitoringApi.GetMessagesAsync"/>.
/// All filter properties are optional; omitting them returns all rows of the selected type.
/// </summary>
[PublicAPI]
public class MessageQuery
{
    /// <summary>Gets or sets whether to query the published or received message table.</summary>
    public MessageType MessageType { get; set; }

    /// <summary>Gets or sets an optional consumer group filter (case-sensitive, exact match).</summary>
    public string? Group { get; set; }

    /// <summary>Gets or sets an optional message name filter (case-sensitive, exact match).</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets an optional content substring filter applied to the serialized message body.</summary>
    public string? Content { get; set; }

    /// <summary>Gets or sets an optional status filter (e.g., <see cref="Monitoring.StatusName.Succeeded"/>, <see cref="Monitoring.StatusName.Failed"/>).</summary>
    public StatusName? StatusName { get; set; }

    /// <summary>Gets or sets an optional delivery intent filter (<see cref="Headless.Messaging.IntentType.Bus"/> or <see cref="Headless.Messaging.IntentType.Queue"/>).</summary>
    public IntentType? IntentType { get; set; }

    /// <summary>Gets or sets the one-based page index for paginated results.</summary>
    public int CurrentPage { get; set; }

    /// <summary>Gets or sets the maximum number of rows returned per page.</summary>
    public int PageSize { get; set; }
}
