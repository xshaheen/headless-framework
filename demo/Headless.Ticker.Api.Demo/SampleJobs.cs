using Headless.Ticker.Base;

namespace Headless.Ticker.Api.Demo;

public class SampleJobs
{
    [TickerFunction("WebApiSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[WebApi] Hello from TickerQ! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
