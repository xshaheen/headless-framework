// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Monitoring;

public class MessageQuery
{
    public MessageType MessageType { get; set; }

    public string? Group { get; set; }

    public string? Name { get; set; }

    public string? Content { get; set; }

    public string? StatusName { get; set; }

    public int CurrentPage { get; set; }

    public int PageSize { get; set; }
}
