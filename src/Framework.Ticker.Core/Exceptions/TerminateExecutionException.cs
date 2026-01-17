using Framework.Ticker.Utilities.Enums;

namespace Framework.Ticker.Exceptions;

public class TerminateExecutionException : Exception
{
    internal TickerStatus Status { get; } = TickerStatus.Skipped;

    public TerminateExecutionException(string message)
        : base(message) { }

    public TerminateExecutionException(TickerStatus tickerType, string message)
        : base(message) => Status = tickerType;

    public TerminateExecutionException(string message, Exception innerException)
        : base(message, innerException) { }

    public TerminateExecutionException(TickerStatus tickerType, string message, Exception innerException)
        : base(message, innerException) => Status = tickerType;
}

internal class ExceptionDetailClassForSerialization
{
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
}
