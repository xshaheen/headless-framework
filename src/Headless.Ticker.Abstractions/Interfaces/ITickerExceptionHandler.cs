using Headless.Ticker.Enums;

namespace Headless.Ticker.Interfaces;

public interface ITickerExceptionHandler
{
    Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
    Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
}
