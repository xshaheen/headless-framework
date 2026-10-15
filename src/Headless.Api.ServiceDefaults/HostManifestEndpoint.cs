// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Hosting;
using Headless.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Api.ServiceDefaults;

/// <summary>Maps the HTTP form of the host manifest that <c>RunHeadlessAsync(args)</c> prints for <c>describe</c>.</summary>
[PublicAPI]
public static partial class HostManifestEndpoint
{
    private const string _LoggerCategory = "Headless.Api.ServiceDefaults.HostManifest";

    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps <c>GET</c> at <paramref name="pattern" /> to the host manifest JSON: what each feature registered in
        /// this host, such as Messaging consumers and Jobs functions.
        /// </summary>
        /// <param name="pattern">The route pattern, such as <c>/_headless/describe</c>.</param>
        /// <param name="authorizationPolicy">
        /// The authorization policy a caller must satisfy. When <see langword="null" />, any authenticated caller is
        /// admitted.
        /// </param>
        /// <returns>The endpoint's builder, to add conventions such as rate limiting or <c>AllowAnonymous()</c>.</returns>
        /// <remarks>
        /// <para>
        /// The endpoint requires authorization unless the caller opts out with <c>.AllowAnonymous()</c> on the returned
        /// builder, because a forgotten authorization call would publish the host's whole consumer and job topology.
        /// The default admits any authenticated principal, so a host that accepts end-user tokens should name an
        /// operator policy.
        /// </para>
        /// <para>
        /// The document is the one <c>describe</c> prints, served as <c>application/json</c>. When it cannot be built,
        /// the endpoint logs the error and answers a fixed 500 problem response that never carries the error text,
        /// because a section's message can name internal configuration.
        /// </para>
        /// <para>The endpoint is excluded from OpenAPI documents.</para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// <paramref name="pattern" />, or a non-null <paramref name="authorizationPolicy" />, is empty or whitespace.
        /// </exception>
        public RouteHandlerBuilder MapHeadlessHostManifest(
            [StringSyntax("Route")] string pattern,
            string? authorizationPolicy = null
        )
        {
            Argument.IsNotNull(endpoints);
            Argument.IsNotNullOrWhiteSpace(pattern);

            if (authorizationPolicy is not null)
            {
                Argument.IsNotNullOrWhiteSpace(authorizationPolicy);
            }

            // A typed delegate binds the Delegate overload, which writes the returned IResult; the method group alone
            // binds RequestDelegate and would discard it.
            Func<HttpContext, Task<IResult>> handler = _DescribeAsync;
            var endpoint = endpoints.MapGet(pattern, handler).ExcludeFromDescription();

            return authorizationPolicy is null
                ? endpoint.RequireAuthorization()
                : endpoint.RequireAuthorization(authorizationPolicy);
        }
    }

    private static async Task<IResult> _DescribeAsync(HttpContext context)
    {
        await using var document = new MemoryStream();

        try
        {
            await HostManifest
                .WriteAsync(context.RequestServices, document, context.RequestAborted)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // False positive: the endpoint is the boundary that turns any section failure into a logged, fixed problem response.
        catch (Exception exception) when (exception is not OperationCanceledException)
#pragma warning restore CA1031
        {
            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(_LoggerCategory);
            LogHostManifestFailed(logger, exception);

            return TypedResults.Problem(
                detail: "The host manifest could not be built. The host's logs carry the cause.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Host manifest failed"
            );
        }

        return TypedResults.Bytes(document.ToArray(), ContentTypes.Applications.Json);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "HEADLESS_HOST_MANIFEST_FAILED",
        Level = LogLevel.Error,
        Message = "The host manifest endpoint could not build the manifest."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogHostManifestFailed(ILogger logger, Exception exception);
}
