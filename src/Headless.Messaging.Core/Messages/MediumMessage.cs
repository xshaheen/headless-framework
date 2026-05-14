// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Messages;

[PublicAPI]
public class MediumMessage
{
    public required long StorageId { get; set; }

    public required Message Origin { get; set; }

    public required string Content { get; set; }

    public DateTime Added { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime? NextRetryAt { get; set; }

    public int Retries { get; set; }

    public string? ExceptionInfo { get; set; }
}
