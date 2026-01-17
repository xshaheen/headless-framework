// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages.Messages;

public class MediumMessage
{
    public required string DbId { get; set; }

    public required Message Origin { get; set; }

    public required string Content { get; set; }

    public DateTime Added { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int Retries { get; set; }
}
