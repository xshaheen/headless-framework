namespace Headless.Ticker.Models;

public class InternalManagerContext(Guid id)
{
    public Guid Id { get; set; } = id;
    public required string FunctionName { get; set; }
    public required string Expression { get; set; }
    public int Retries { get; set; }
    public int[]? RetryIntervals { get; set; }
    public NextCronOccurrence? NextCronOccurrence { get; set; }
}

public class NextCronOccurrence(Guid id, DateTime createdAt)
{
    public Guid Id { get; set; } = id;
    public DateTime CreatedAt { get; set; } = createdAt;
}
