using Headless.Jobs.Base;

namespace Headless.Jobs.Api.Demo;

public class SampleJobs
{
    [TickerFunction("WebApiSample_HelloWorld")]
    public Task HelloWorldAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[WebApi] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
