// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.IO;
using Microsoft.AspNetCore.Http;

namespace Headless.Jobs.Endpoints;

internal static class DashboardRequestBodyReader
{
    public static async Task<(T? Value, IResult? Error)> ReadAsync<T>(
        HttpContext context,
        JsonSerializerOptions? options,
        CancellationToken cancellationToken
    )
    {
        if (context.Request.ContentLength > DashboardOptionsBuilder.MaxRequestBodyBytes)
        {
            return (default, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
        }

        // A cross-site <form enctype="text/plain"> can shape its body into valid JSON; only a script can send
        // application/json, and a cross-origin script needs a CORS preflight to do it.
        if (!context.Request.HasJsonContentType())
        {
            return (default, Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
        }

        try
        {
            await using var limitedBody = new SizeLimitedReadStream(
                context.Request.Body,
                DashboardOptionsBuilder.MaxRequestBodyBytes,
                leaveOpen: true
            );
            var value = await JsonSerializer
                .DeserializeAsync<T>(limitedBody, options, cancellationToken)
                .ConfigureAwait(false);

            return value is null ? (default, Results.BadRequest("A JSON request body is required.")) : (value, null);
        }
        catch (StreamSizeLimitExceededException)
        {
            return (default, Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
        }
        catch (JsonException)
        {
            return (default, Results.BadRequest("The JSON request body is invalid."));
        }
    }
}
