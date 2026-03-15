using Headless.Jobs.Enums;

namespace Headless.Jobs.Interfaces;

public interface ITickerExceptionHandler
{
    Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
    Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
}
