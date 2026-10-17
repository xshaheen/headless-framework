// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Headless.OpenApi;

/// <summary>
/// Adds the rate limiter's rejection response to every operation a limiter applies to: one with
/// <see cref="EnableRateLimitingAttribute"/> metadata (<c>RequireRateLimiting(...)</c> or <c>[EnableRateLimiting]</c>),
/// or any operation when <see cref="RateLimiterOptions.GlobalLimiter"/> is set.
/// </summary>
/// <remarks>
/// <see cref="DisableRateLimitingAttribute"/> anywhere in the metadata turns every limiter off, as it does in the
/// middleware. The status is <see cref="RateLimiterOptions.RejectionStatusCode"/>: 429 under
/// <c>UseHeadlessProblemDetails()</c>, the native 503 otherwise. The response starts without content;
/// <see cref="ProblemDetailsTransformer"/> then describes its typed problem body and its <c>Retry-After</c> header. An
/// operation that already declares the status keeps its own response.
/// </remarks>
internal sealed class RateLimitingResponseTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken
    )
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<DisableRateLimitingAttribute>().Any())
        {
            return Task.CompletedTask;
        }

        var options = context.ApplicationServices.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        if (!metadata.OfType<EnableRateLimitingAttribute>().Any() && options.GlobalLimiter is null)
        {
            return Task.CompletedTask;
        }

        (operation.Responses ??= []).TryAdd(
            options.RejectionStatusCode.ToString(CultureInfo.InvariantCulture),
            new OpenApiResponse { Description = "The request was rejected by a rate limiter. Retry later." }
        );

        return Task.CompletedTask;
    }
}
