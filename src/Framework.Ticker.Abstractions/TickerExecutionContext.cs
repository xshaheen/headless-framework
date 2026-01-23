using System.Runtime.InteropServices;
using System.Threading;
using Framework.Ticker.Utilities.Enums;
using Framework.Ticker.Utilities.Models;

namespace Framework.Ticker.Utilities;

internal interface ITickerOptionsSeeding
{
    bool SeedDefinedCronTickers { get; }
    Func<IServiceProvider, Task>? TimeSeederAction { get; }
    Func<IServiceProvider, Task>? CronSeederAction { get; }
}

internal class TickerExecutionContext
{
    private long _nextOccurrenceTicks;
    internal Action<IServiceProvider>? ExternalProviderApplicationAction { get; set; }
    internal Action<object>? DashboardApplicationAction { get; set; }
    public Action<object?, CoreNotifyActionType>? NotifyCoreAction { get; set; }
    public string? LastHostExceptionMessage { get; set; }
    internal ITickerOptionsSeeding? OptionsSeeding { get; set; }

    private InternalFunctionContext[] _functions = [];
    internal InternalFunctionContext[] Functions => Volatile.Read(ref _functions);

    public void SetNextPlannedOccurrence(DateTime? dt) =>
        Interlocked.Exchange(ref _nextOccurrenceTicks, dt?.Ticks ?? -1);

    public DateTime? GetNextPlannedOccurrence()
    {
        var t = Interlocked.Read(ref _nextOccurrenceTicks);
        return t < 0 ? null : new DateTime(t, DateTimeKind.Utc);
    }

    public void SetFunctions(ReadOnlySpan<InternalFunctionContext> functions)
    {
        var copy = new InternalFunctionContext[functions.Length];
        functions.CopyTo(copy.AsSpan());

        _CacheFunctionReferences(copy.AsSpan());
        Volatile.Write(ref _functions, copy);
    }

    private static void _CacheFunctionReferences(Span<InternalFunctionContext> functions)
    {
        for (var i = 0; i < functions.Length; i++)
        {
            ref var context = ref functions[i];
            if (TickerFunctionProvider.TickerFunctions.TryGetValue(context.FunctionName, out var tickerItem))
            {
                context.CachedDelegate = tickerItem.Delegate;
                context.CachedPriority = tickerItem.Priority;
            }

            if (context.TimeTickerChildren is { Count: > 0 })
            {
                var childrenSpan = CollectionsMarshal.AsSpan(context.TimeTickerChildren);
                _CacheFunctionReferences(childrenSpan);
            }
        }
    }
}
