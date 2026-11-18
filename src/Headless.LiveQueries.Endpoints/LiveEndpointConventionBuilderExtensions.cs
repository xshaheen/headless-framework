// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Checks;
using Headless.LiveQueries.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.LiveQueries;

/// <summary>Endpoint conventions of live queries.</summary>
[PublicAPI]
public static class LiveEndpointConventionBuilderExtensions
{
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Makes a GET endpoint live. A request that asks for no stream still receives the endpoint's ordinary
        /// single response. A request whose <c>Accept</c> header names a stream format (<c>text/event-stream</c>,
        /// <c>application/x-ndjson</c>, or one a registered <see cref="ILiveTransport" /> accepts) receives the
        /// result now and again after every announced change to what the handler read.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every run, the first included, executes the whole endpoint again on a new service scope: arguments are
        /// bound again, endpoint filters run again, the caller is authenticated again and the endpoint's
        /// authorization is evaluated again. The handler must be read-only, must return its values materialized,
        /// and must record at least one dependency, through a tracked data source or
        /// <see cref="ILiveQueryDependencies" />.
        /// </para>
        /// <para>
        /// On a route group it applies to every endpoint of the group. The host must register live endpoints with
        /// <c>AddHeadlessLiveQueries(setup =&gt; setup.UseEndpoints())</c>.
        /// </para>
        /// <para>
        /// It also attaches <see cref="LiveUpdatesMetadata" />, which Headless.OpenApi renders as the
        /// <c>x-headless-live</c> operation extension and the stream media types of the success response.
        /// </para>
        /// </remarks>
        /// <returns>The builder, to allow chaining.</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the endpoints are built, for an endpoint that accepts a method other than GET, and when
        /// live endpoints are not registered.
        /// </exception>
        public TBuilder WithLiveUpdates()
        {
            Argument.IsNotNull(builder);

            builder.AddEndpointFilter(LiveEndpointFilter.Instance);
            // Read by API documentation only, which describes the stream without a reference to this package.
            builder.WithMetadata(new LiveUpdatesMetadata());

            // Finally, so the check sees the methods whatever order the conventions were added in.
            builder.Finally(static endpoint =>
            {
                var methods = endpoint.Metadata.OfType<IHttpMethodMetadata>().LastOrDefault()?.HttpMethods;

                if (methods is not { Count: > 0 } || methods.Any(method => !HttpMethods.IsGet(method)))
                {
                    throw new InvalidOperationException(
                        $"WithLiveUpdates() is only valid on a GET endpoint, and '{endpoint.DisplayName}' accepts "
                            + $"{(methods is { Count: > 0 } ? string.Join(", ", methods) : "every method")}. A live "
                            + "endpoint is run again after every change, so it must be a read."
                    );
                }

                if (endpoint.ApplicationServices.GetService<LiveStreamHandler>() is null)
                {
                    throw new InvalidOperationException(
                        $"'{endpoint.DisplayName}' uses WithLiveUpdates(), but live endpoints are not registered. "
                            + "Call services.AddHeadlessLiveQueries(setup => setup.UseEndpoints())."
                    );
                }
            });

            return builder;
        }
    }
}
