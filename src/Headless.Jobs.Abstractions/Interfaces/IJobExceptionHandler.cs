using Headless.Jobs.Enums;

namespace Headless.Jobs.Interfaces;

public interface IJobExceptionHandler
{
    Task HandleExceptionAsync(Exception exception, Guid jobId, JobType jobType);
    Task HandleCanceledExceptionAsync(Exception exception, Guid jobId, JobType jobType);
}
