namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IRequestTime
{
    public DateTimeOffset Now { get; }
}

public sealed class RequestTime(IClock clock) : IRequestTime
{
    public DateTimeOffset Now { get; } = clock.Now;
}
