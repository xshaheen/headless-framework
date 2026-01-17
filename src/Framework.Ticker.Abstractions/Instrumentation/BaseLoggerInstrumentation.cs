using System.Diagnostics;
using Framework.Ticker.Utilities.Enums;
using Framework.Ticker.Utilities.Models;
using Microsoft.Extensions.Logging;

namespace Framework.Ticker.Utilities.Instrumentation;

public abstract class TickerQBaseLoggerInstrumentation(ILogger logger, string instanceIdentifier)
{
    protected string InstanceIdentifier { get; } = instanceIdentifier;

    public abstract Activity? StartJobActivity(string activityName, InternalFunctionContext context);

    public virtual void LogJobEnqueued(string jobType, string functionName, Guid jobId, string? enqueuedFrom = null)
    {
        logger.LogInformation(
            "TickerQ Job enqueued: {JobType} - {Function} ({JobId}) from {EnqueuedFrom}",
            jobType,
            functionName,
            jobId,
            enqueuedFrom ?? "Unknown"
        );
    }

    public virtual void LogJobStarted(Guid jobId, string functionName, string jobType)
    {
        logger.LogInformation("TickerQ Job started: {JobType} - {Function} ({JobId})", jobType, functionName, jobId);
    }

    public virtual void LogJobCompleted(Guid jobId, string functionName, long executionTimeMs, bool success)
    {
        logger.LogInformation(
            "TickerQ Job completed: {Function} ({JobId}) in {ExecutionTime}ms - Success: {Success}",
            functionName,
            jobId,
            executionTimeMs,
            success
        );
    }

    public virtual void LogJobFailed(Guid jobId, string functionName, Exception exception, int retryCount)
    {
        logger.LogError(
            exception,
            "TickerQ Job failed: {Function} ({JobId}) - Retry {RetryCount} - {Error}",
            functionName,
            jobId,
            retryCount,
            exception.Message
        );
    }

    public virtual void LogJobCancelled(Guid jobId, string functionName, string reason)
    {
        logger.LogWarning("TickerQ Job cancelled: {Function} ({JobId}) - {Reason}", functionName, jobId, reason);
    }

    public virtual void LogJobSkipped(Guid jobId, string functionName, string reason)
    {
        logger.LogInformation("TickerQ Job skipped: {Function} ({JobId}) - {Reason}", functionName, jobId, reason);
    }

    public virtual void LogSeedingDataStarted(string seedingDataType)
    {
        logger.LogInformation(
            "TickerQ start seeding data: {TickerType} ({EnvironmentName})",
            seedingDataType,
            InstanceIdentifier
        );
    }

    public virtual void LogSeedingDataCompleted(string seedingDataType)
    {
        logger.LogInformation(
            "TickerQ completed seeding data: {TickerType} ({EnvironmentName})",
            seedingDataType,
            InstanceIdentifier
        );
    }

    public virtual void LogRequestDeserializationFailure(
        string requestType,
        string functionName,
        Guid tickerId,
        TickerType type,
        Exception exception
    )
    {
        logger.LogError(
            "Failed to deserialize request to {RequestType} - {TickerId} - {TickerType}: {Exception}",
            requestType,
            tickerId,
            type,
            exception
        );
    }
}
