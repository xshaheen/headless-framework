using Headless.Jobs.Base;

namespace Headless.Jobs.Api.Demo;

public sealed record WebApiHelloRequest(string Message);

public class SampleJobs
{
    [JobFunction("WebApiSample_HelloWorld")]
    public Task HelloWorldAsync(JobFunctionContext<WebApiHelloRequest> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[WebApi] {context.Request.Message} Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return Task.CompletedTask;
    }
}
