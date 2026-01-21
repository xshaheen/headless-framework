// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Monitoring;

public class MessageView
{
    public required string Id { get; set; }

    public required string Version { get; set; }

    public string? Group { get; set; }

    public required string Name { get; set; }

    public string? Content { get; set; }

    public DateTime Added { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int Retries { get; set; }

    public required string StatusName { get; set; }
}
