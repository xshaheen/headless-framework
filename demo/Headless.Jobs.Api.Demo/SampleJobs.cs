using Headless.Jobs.Base;

namespace Headless.Jobs.Api.Demo;

public sealed record WebApiHelloRequest(string Message);

[Job("webapi-demo.hello-world")]
public sealed class HelloWorldJob : IJob<WebApiHelloRequest>
{
    public ValueTask ExecuteAsync(JobContext<WebApiHelloRequest> context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.WriteLine($"[WebApi] {context.Request.Message} Id={context.Id}, ScheduledFor={context.ScheduledFor:O}");
        return ValueTask.CompletedTask;
    }
}
