using Headless.Jobs.Enums;

namespace Headless.Jobs.Interfaces;

public interface IJobExceptionHandler
{
    Task HandleExceptionAsync(Exception exception, Guid tickerId, JobType tickerType);
    Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, JobType tickerType);
}
