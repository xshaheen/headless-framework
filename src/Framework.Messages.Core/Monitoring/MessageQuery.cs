// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;

namespace Framework.Messages.Monitoring;

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
