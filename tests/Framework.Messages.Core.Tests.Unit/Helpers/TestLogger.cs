using Microsoft.Extensions.Logging;

namespace Tests.Helpers;

public class TestLogger(ITestOutputHelper outputHelper, string categoryName) : ILogger
{
    public string CategoryName { get; } = categoryName;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception, string> formatter
    )
    {
        outputHelper.WriteLine($"[{logLevel}] {formatter.Invoke(state, exception)}");
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return new DisposableAction(state);
    }

    private sealed class DisposableAction(object state) : IDisposable
    {
        private readonly object _state = state;

        public void Dispose() { }
    }
}
