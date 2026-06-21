// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Enums;

namespace Headless.Jobs.Models;

public class InternalManagerContext(Guid id)
{
    public Guid Id { get; set; } = id;
    public required string FunctionName { get; set; }
    public required string Expression { get; set; }
    public int Retries { get; set; }
    public int[]? RetryIntervals { get; set; }

    /// <summary>Node-death policy carried from the cron definition to stamp onto generated occurrences.</summary>
    public NodeDeathPolicy OnNodeDeath { get; set; } = NodeDeathPolicy.Retry;
    public NextCronOccurrence? NextCronOccurrence { get; set; }
}

public class NextCronOccurrence(Guid id, DateTime createdAt)
{
    public Guid Id { get; set; } = id;
    public DateTime CreatedAt { get; set; } = createdAt;
}
