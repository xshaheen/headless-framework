using Headless.Jobs.Enums;

namespace Headless.Jobs.Exceptions;

public class TerminateExecutionException : Exception
{
    internal JobStatus Status { get; } = JobStatus.Skipped;

    public TerminateExecutionException(string message)
        : base(message) { }

    public TerminateExecutionException(JobStatus tickerType, string message)
        : base(message) => Status = tickerType;

    public TerminateExecutionException(string message, Exception innerException)
        : base(message, innerException) { }

    public TerminateExecutionException(JobStatus tickerType, string message, Exception innerException)
        : base(message, innerException) => Status = tickerType;
}

internal class ExceptionDetailClassForSerialization
{
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
}
