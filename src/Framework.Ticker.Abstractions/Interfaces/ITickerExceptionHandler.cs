using Framework.Ticker.Utilities.Enums;

namespace Framework.Ticker.Utilities.Interfaces;

public interface ITickerExceptionHandler
{
    Task HandleExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
    Task HandleCanceledExceptionAsync(Exception exception, Guid tickerId, TickerType tickerType);
}
