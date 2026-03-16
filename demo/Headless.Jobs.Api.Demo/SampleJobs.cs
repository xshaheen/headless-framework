using Headless.Jobs.Base;

namespace Headless.Jobs.Api.Demo;

public class SampleJobs
{
    [JobFunction("WebApiSample_HelloWorld")]
    public Task HelloWorldAsync(JobFunctionContext context, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[WebApi] Hello from Jobs! Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
