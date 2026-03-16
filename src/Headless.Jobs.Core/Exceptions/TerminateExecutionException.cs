using Headless.Jobs.Enums;

namespace Headless.Jobs.Exceptions;

public sealed class TerminateExecutionException : Exception
{
    internal JobStatus Status { get; } = JobStatus.Skipped;

    public TerminateExecutionException(string message)
        : base(message) { }

    public TerminateExecutionException(JobStatus jobType, string message)
        : base(message) => Status = jobType;

    public TerminateExecutionException(string message, Exception innerException)
        : base(message, innerException) { }

    public TerminateExecutionException(JobStatus jobType, string message, Exception innerException)
        : base(message, innerException) => Status = jobType;
}

internal sealed class ExceptionDetailClassForSerialization
{
    public required string Message { get; set; }
    public string? StackTrace { get; set; }
}
