namespace Framework.BuildingBlocks.Abstractions;

public interface IClock
{
    long Ticks { get; }

    DateTimeOffset Now { get; }
}

public sealed class Clock : IClock
{
    public long Ticks => Environment.TickCount64;

    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}
