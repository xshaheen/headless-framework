using Demo.Consumers;
using Headless.Messaging;

namespace Demo;

public static class Endpoints
{
    public static void MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        app.MapGet("/", () => Results.LocalRedirect("/messaging", true));

        // Basic liveness endpoint.
        app.MapGet("/api/status", () => Results.Ok(new { Status = "ok", Time = DateTimeOffset.UtcNow }));

        app.MapPost(
            "/api/messages/ping",
            async (IOutboxPublisher publisher, PingRequest request, CancellationToken cancellationToken) =>
            {
                var message = new PingMessage(request.Text ?? "ping", DateTimeOffset.UtcNow);
                await publisher.PublishAsync(message, cancellationToken: cancellationToken);
                return Results.Accepted(value: message);
            }
        );

        // Publish a work item (can be configured to fail a few times to show retries).
        app.MapPost(
            "/api/messages/work",
            async (IOutboxPublisher publisher, WorkItemRequest request, CancellationToken cancellationToken) =>
            {
                var message = new WorkItemMessage(
                    request.WorkId ?? Guid.NewGuid().ToString("N"),
                    request.ShouldFail,
                    request.FailuresBeforeSuccess
                );
                await publisher.PublishAsync(message, cancellationToken: cancellationToken);
                return Results.Accepted(value: message);
            }
        );

        // Schedule a one-time job that will publish a work item when it fires.
        app.MapPost(
            "/api/schedule/once",
            async (IScheduledJobManager jobManager, ScheduleOnceRequest request, CancellationToken cancellationToken) =>
            {
                var runAt = DateTimeOffset.UtcNow.AddSeconds(request.DelaySeconds);
                var payload = request.Payload is null ? null : JsonSerializer.Serialize(request.Payload, jsonOptions);
                var name = string.IsNullOrWhiteSpace(request.Name) ? $"one-time-{Guid.NewGuid():N}" : request.Name;

                await jobManager.ScheduleOnceAsync(name, runAt, typeof(OneTimeJobConsumer), payload, cancellationToken);

                return Results.Accepted(
                    value: new
                    {
                        name,
                        runAt,
                        payload = request.Payload,
                    }
                );
            }
        );

        // Trigger a job immediately (by name).
        app.MapPost(
            "/api/schedule/trigger/{name}",
            async (IScheduledJobManager jobManager, string name, CancellationToken cancellationToken) =>
            {
                var result = await jobManager.TriggerAsync(name, cancellationToken);
                return result.IsSuccess ? Results.Ok() : Results.NotFound(result.Error);
            }
        );

        // Enable a scheduled job (if disabled).
        app.MapPost(
            "/api/schedule/enable/{name}",
            async (IScheduledJobManager jobManager, string name, CancellationToken cancellationToken) =>
            {
                var result = await jobManager.EnableAsync(name, cancellationToken);
                return result.IsSuccess ? Results.Ok() : Results.NotFound(result.Error);
            }
        );

        // Disable a scheduled job.
        app.MapPost(
            "/api/schedule/disable/{name}",
            async (IScheduledJobManager jobManager, string name, CancellationToken cancellationToken) =>
            {
                var result = await jobManager.DisableAsync(name, cancellationToken);
                return result.IsSuccess ? Results.Ok() : Results.NotFound(result.Error);
            }
        );

        // List all scheduled jobs.
        app.MapGet(
            "/api/schedule/jobs",
            async (IScheduledJobManager jobManager, CancellationToken cancellationToken) =>
            {
                var jobs = await jobManager.GetAllAsync(cancellationToken);
                return Results.Ok(jobs);
            }
        );
    }
}
