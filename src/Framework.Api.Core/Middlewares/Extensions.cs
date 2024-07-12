using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Core.Middlewares;

public static class Extensions
{
    /// <summary>Adds the server timing middleware.</summary>
    public static IServiceCollection AddServerTimingMiddleware(this IServiceCollection services)
    {
        return services.AddSingleton<ServerTimingMiddleware>();
    }

    /// <summary>
    /// Measures the time the request takes to process and returns this in a Server-Timing trailing HTTP header.
    /// It is used to surface any back-end server timing metrics (e.g. database read/write, CPU time, file system
    /// access, etc.) to the developer tools in the user's browser.
    /// </summary>
    public static IApplicationBuilder UseServerTiming(this IApplicationBuilder application)
    {
        return application.UseMiddleware<ServerTimingMiddleware>();
    }

    /// <summary>This is a custom middleware that rewrites the status code of the response.</summary>
    public static IServiceCollection AddCustomStatusCodesRewriterMiddleware(this IServiceCollection services)
    {
        return services.AddSingleton<StatusCodesRewriterMiddleware>();
    }

    /// <summary>
    /// Add the status codes rewriter middleware to the pipeline to rewrite the endpoint not found status code as problem details response.
    /// When request URL does not match any route, status code 404 is returned with a problem details response.
    /// </summary>
    public static IApplicationBuilder UseCustomStatusCodesRewriter(this IApplicationBuilder app)
    {
        return app.UseMiddleware<StatusCodesRewriterMiddleware>();
    }

    /// <summary>
    /// Handles <see cref="OperationCanceledException"/> caused by the HTTP request being aborted,
    /// then shortcuts and returns an error status code.
    /// </summary>
    public static IServiceCollection AddRequestCanceledMiddleware(this IServiceCollection services)
    {
        return services.AddSingleton<RequestCanceledMiddleware>();
    }

    /// <summary>
    /// Handles <see cref="OperationCanceledException"/> caused by the HTTP request being aborted, then shortcuts and
    /// returns an error status code.
    /// See https://andrewlock.net/using-cancellationtokens-in-asp-net-core-minimal-apis/.
    /// </summary>
    public static IApplicationBuilder UseRequestCanceled(this IApplicationBuilder application)
    {
        return application.UseMiddleware<RequestCanceledMiddleware>();
    }
}
