// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using tusdotnet;

namespace Headless.Tus;

/// <summary>Maps the upload endpoints declared inside <c>AddHeadlessTus</c>.</summary>
[PublicAPI]
public static class TusEndpointRouteBuilderExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the default upload endpoint, the one declared through the <c>AddUploadEndpoint</c> overload that
        /// takes no name, at <paramref name="pattern"/>.
        /// </summary>
        /// <param name="pattern">
        /// The route the endpoint listens on, for example <c>/files</c>. An upload's own URL is the pattern followed
        /// by its file id. The pattern may hold other route parameters, but not one named <c>TusFileId</c>.
        /// </param>
        /// <returns>The endpoint's convention builder, to attach authorization, CORS, or other conventions.</returns>
        /// <remarks>
        /// <para>
        /// Every request is served by tusdotnet under the endpoint's <see cref="TusUploadEndpointOptions"/>: the
        /// host's registered store, the maximum size, the expiration window, and the file lock provider when one is
        /// registered. The endpoint offers the tus extensions the store supports except concatenation, so it
        /// neither advertises nor accepts parallel uploads.
        /// </para>
        /// <para>
        /// The name keeps the <c>Headless</c> prefix because tusdotnet already declares <c>MapTus</c> on
        /// <see cref="IEndpointRouteBuilder"/> in the <c>tusdotnet</c> namespace, which every tus host imports.
        /// <c>MapTus</c> maps an endpoint from a configuration the application builds by hand and applies none of
        /// what <c>AddHeadlessTus</c> declares; this method maps a declared endpoint.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pattern"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="pattern"/> declares a <c>TusFileId</c> route parameter.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <c>AddHeadlessTus</c> was not called, or when it declared no default upload endpoint.
        /// </exception>
        public IEndpointConventionBuilder MapHeadlessTus([StringSyntax("Route")] string pattern)
        {
            Argument.IsNotNull(pattern);

            return _Map(endpoints, pattern, Options.DefaultName);
        }

        /// <summary>
        /// Maps the upload endpoint declared as <paramref name="name"/> through <c>AddUploadEndpoint</c> at
        /// <paramref name="pattern"/>.
        /// </summary>
        /// <param name="pattern">
        /// The route the endpoint listens on, for example <c>/videos</c>. An upload's own URL is the pattern followed
        /// by its file id. The pattern may hold other route parameters, but not one named <c>TusFileId</c>.
        /// </param>
        /// <param name="name">The name the endpoint was declared with.</param>
        /// <returns>The endpoint's convention builder, to attach authorization, CORS, or other conventions.</returns>
        /// <remarks><inheritdoc cref="MapHeadlessTus(IEndpointRouteBuilder, string)" path="/remarks"/></remarks>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="pattern"/> or <paramref name="name"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// Thrown when <paramref name="name"/> is empty or white space, or when <paramref name="pattern"/> declares
        /// a <c>TusFileId</c> route parameter.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <c>AddHeadlessTus</c> was not called, or when it declared no upload endpoint named
        /// <paramref name="name"/>.
        /// </exception>
        public IEndpointConventionBuilder MapHeadlessTus([StringSyntax("Route")] string pattern, string name)
        {
            Argument.IsNotNull(pattern);
            Argument.IsNotNullOrWhiteSpace(name);

            return _Map(endpoints, pattern, name);
        }
    }

    private static IEndpointConventionBuilder _Map(IEndpointRouteBuilder endpoints, string pattern, string name)
    {
        var registry =
            endpoints.ServiceProvider.GetService<TusEndpointRegistry>()
            ?? throw new InvalidOperationException(
                "Headless TUS is not registered. Call services.AddHeadlessTus(tus => …) before mapping an upload "
                    + "endpoint."
            );

        if (!registry.TryGet(name, out var endpoint))
        {
            throw new InvalidOperationException(
                string.Equals(name, Options.DefaultName, StringComparison.Ordinal)
                    ? "The default upload endpoint is not declared. Call tus.AddUploadEndpoint(options => …) inside "
                        + "AddHeadlessTus, or pass the name of a declared endpoint to MapHeadlessTus."
                    : $"The upload endpoint \"{name}\" is not declared. Call tus.AddUploadEndpoint(\"{name}\", …) "
                        + "inside AddHeadlessTus."
            );
        }

        var factory = endpoints.ServiceProvider.GetRequiredService<TusConfigurationFactory>();

        return endpoints.MapTus(pattern, context => factory.CreateAsync(context, endpoint));
    }
}
