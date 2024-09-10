using System.Diagnostics;

namespace Framework.Kernel.BuildingBlocks.Abstractions;

public interface IClock
{
    long Ticks { get; }

    DateTimeOffset Now { get; }

    long GetTimestamp();

    DateTimeOffset Normalize(DateTimeOffset v);

    DateTime Normalize(DateTime v);
}

public sealed class Clock : IClock
{
    private static DateTimeKind NormalizeKind => DateTimeKind.Utc;

    public long Ticks => Environment.TickCount64;

    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public DateTimeOffset Normalize(DateTimeOffset v) => v.ToUniversalTime();

    public DateTime Normalize(DateTime v)
    {
        if (NormalizeKind == v.Kind)
        {
            return v;
        }

        return NormalizeKind switch
        {
            DateTimeKind.Unspecified => v,
            DateTimeKind.Local when v.Kind is DateTimeKind.Utc => v.ToLocalTime(),
            DateTimeKind.Utc when v.Kind is DateTimeKind.Local => v.ToUniversalTime(),
            _ => DateTime.SpecifyKind(v, NormalizeKind)
        };
    }
}
