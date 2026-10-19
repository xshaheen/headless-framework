// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Headless.Idempotency;

/// <summary>
/// Describes what the idempotency middleware adds to an endpoint's HTTP contract, as <see cref="HttpContractMetadata"/>
/// that API documentation renders without knowing about idempotency.
/// </summary>
/// <remarks>
/// It mirrors the middleware: the key header named by the application-level <see cref="HttpIdempotencyOptions.HeaderName"/>
/// (the middleware ignores a per-endpoint one), the 400 for a missing or malformed key, the 409 for a key another
/// attempt still holds (with <c>Retry-After</c>), the reused-key 409 or 422 of
/// <see cref="HttpIdempotencyOptions.MismatchStatusCode"/>, the 413 of <see cref="OversizeBehavior.Reject"/>, and
/// <c>Idempotent-Replayed</c> on replayed success responses. The options are read once, when the endpoint's conventions
/// run.
/// </remarks>
internal static class HttpIdempotencyContract
{
    /// <summary>Attaches, or refreshes, the contract of an endpoint that declared idempotency overrides.</summary>
    /// <param name="endpoint">The endpoint being built.</param>
    /// <remarks>
    /// <c>RequireIdempotencyKey()</c> and <c>WithIdempotency(...)</c> each run it right after adding their override, so
    /// the last run sees every override of the endpoint, its route group's included, and replaces the contract the
    /// earlier runs attached. It is a plain convention rather than a <c>Finally</c> one because a convention builder
    /// need not implement <c>Finally</c>.
    /// </remarks>
    public static void Attach(EndpointBuilder endpoint)
    {
        if (endpoint.Metadata.OfType<AttachedContract>().FirstOrDefault() is { } previous)
        {
            endpoint.Metadata.Remove(previous.Contract);
            endpoint.Metadata.Remove(previous);
        }

        var appOptions =
            endpoint.ApplicationServices.GetService<IOptionsMonitor<HttpIdempotencyOptions>>()?.CurrentValue
            ?? new HttpIdempotencyOptions();
        var options = appOptions.WithEndpointOverrides([.. endpoint.Metadata.OfType<HttpIdempotencyMetadata>()]);

        // The middleware passes a request whose method is not an idempotency method straight through.
        var methods = endpoint.Metadata.OfType<IHttpMethodMetadata>().LastOrDefault()?.HttpMethods;

        if (methods is { Count: > 0 } && !methods.Any(options.Methods.Contains))
        {
            return;
        }

        var contract = Create(appOptions.HeaderName, options);
        endpoint.Metadata.Add(contract);
        endpoint.Metadata.Add(new AttachedContract(contract));
    }

    /// <summary>Describes the contract of an endpoint running with <paramref name="options"/>.</summary>
    /// <param name="headerName">The idempotency-key header the middleware reads.</param>
    /// <param name="options">The endpoint's options, with its overrides applied.</param>
    /// <returns>The contract.</returns>
    public static HttpContractMetadata Create(string headerName, HttpIdempotencyOptions options)
    {
        var keyRequired = options.KeyRequired
            ? $"The request carried no {headerName} header ({HttpIdempotencyErrorCodes.KeyRequired}), or its value was malformed ({HttpIdempotencyErrorCodes.KeyMalformed})."
            : $"The {headerName} header was malformed ({HttpIdempotencyErrorCodes.KeyMalformed}).";

        var inFlight =
            options.InFlightStrategy == InFlightStrategy.WaitAndReplay
                ? $"Another request with the same {headerName} did not finish in time ({HttpIdempotencyErrorCodes.InFlightTimeout})."
                : $"Another request with the same {headerName} is still being processed ({HttpIdempotencyErrorCodes.InFlight}).";

        var reused =
            $"The {headerName} was reused with a different request body ({HttpIdempotencyErrorCodes.KeyReused}).";
        var mismatchIsConflict = options.MismatchStatusCode == StatusCodes.Status409Conflict;

        List<HttpResponseDescription> responses =
        [
            new(StatusCodes.Status400BadRequest, keyRequired),
            new(StatusCodes.Status409Conflict, mismatchIsConflict ? $"{inFlight} {reused}" : inFlight),
        ];

        if (!mismatchIsConflict)
        {
            responses.Add(new(options.MismatchStatusCode, reused));
        }

        if (options.OversizeBehavior == OversizeBehavior.Reject)
        {
            responses.Add(
                new(
                    StatusCodes.Status413PayloadTooLarge,
                    $"The request body is larger than the {options.MaxBodySizeForHashing.ToString(CultureInfo.InvariantCulture)} bytes an idempotent request may carry ({HttpIdempotencyErrorCodes.BodyTooLarge})."
                )
            );
        }

        return new HttpContractMetadata
        {
            RequestHeaders =
            [
                new(
                    headerName,
                    options.KeyRequired,
                    "A client-generated key, unique per operation and at most 255 characters, that makes retrying the "
                        + "request safe: a retry with the same key and body replays the first response instead of "
                        + "running the operation again."
                ),
            ],
            Responses = responses,
            ResponseHeaders =
            [
                new(
                    HeaderNames.RetryAfter,
                    StatusCodes.Status409Conflict,
                    "On an in-flight conflict, the number of seconds until the attempt holding the key loses its "
                        + "lease, the earliest moment a retry can take the key over."
                ),
                new(
                    HttpHeaderNames.IdempotentReplayed,
                    StatusCodes.Status200OK,
                    299,
                    "true when the response is the stored response of an earlier request with the same key, replayed "
                        + "instead of running the operation again."
                ),
            ],
        };
    }

    // Tells the contract this feature attached apart from any other HttpContractMetadata on the endpoint.
    private sealed record AttachedContract(HttpContractMetadata Contract);
}
