// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Context;
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
        /// Every upload is bound to the endpoint that created it, to its owner, and to the request's tenant when it
        /// has one. The owner is the key the endpoint's <c>OwnedBy</c> function returns, or the signed-in user's id
        /// from <c>ICurrentUser</c> when the endpoint has no such function. A request with no owner is refused with
        /// 401. A request for an upload that another endpoint, owner, or tenant created is answered as not found,
        /// exactly like a request for an upload that does not exist. Metadata keys that start with
        /// <c>headless-</c> are reserved for the server: a creating request that sends one is refused with 400.
        /// </para>
        /// <para>
        /// A creating request must pass the endpoint's rules before the upload starts: required metadata and
        /// empty uploads (400), allowed content types and file extensions (415), then the validators named
        /// through <c>ValidateWith</c>, in order. While the endpoint lists content types or extensions, the
        /// upload's first write is also checked against the format its leading bytes show, and an upload that
        /// fails that check is deleted; <see cref="TusUploadEndpointOptions.VerifyFileSignature"/> describes the
        /// check. Every refusal is a plain-text response.
        /// </para>
        /// <para>
        /// An endpoint that names a completion handler through <c>OnCompleted</c> hands it each upload whose last
        /// byte is stored, and <see cref="ITusUploadCompletedHandler"/> describes when the handler runs and runs
        /// again. An upload the handler refuses is removed and its request answered with 400 and the handler's
        /// reason. A client may delete its upload until the handler has taken it over, and is answered with 403
        /// afterwards. A status request for an upload whose handler is still owed runs the handler first, and is
        /// answered with 423 when that fails, which is the status a tus client asks again after.
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
        /// Thrown when <c>AddHeadlessTus</c> was not called, when it declared no default upload endpoint, or when
        /// the endpoint has no <c>OwnedBy</c> function and no <c>ICurrentUser</c> is registered.
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
        /// Thrown when <c>AddHeadlessTus</c> was not called, when it declared no upload endpoint named
        /// <paramref name="name"/>, or when the endpoint has no <c>OwnedBy</c> function and no <c>ICurrentUser</c>
        /// is registered.
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

        // Without an owner every request would be refused, so the mistake is reported here, not on the first upload.
        if (endpoint.OwnerKey is null && !_IsRegistered<ICurrentUser>(endpoints.ServiceProvider))
        {
            throw new InvalidOperationException(
                $"{endpoint.DisplayName} has no owner for its uploads. Call OwnedBy(…) on the endpoint, or register "
                    + "ICurrentUser so that the signed-in user owns them."
            );
        }

        var factory = endpoints.ServiceProvider.GetRequiredService<TusConfigurationFactory>();
        var builder = endpoints.MapTus(pattern, context => factory.CreateAsync(context, endpoint));

        if (endpoint.CompletedHandler is not null)
        {
            endpoints.ServiceProvider.GetRequiredService<TusCompletionRunner>().WarnWhenTheHandlerRunsOnce(endpoint);

            // tusdotnet's completion event cannot fail its request, so a handler's refusal is answered around
            // tusdotnet's own request delegate.
            builder.Add(static endpointBuilder =>
            {
                if (endpointBuilder.RequestDelegate is { } requestDelegate)
                {
                    endpointBuilder.RequestDelegate = TusCompletionRunner.AnswerRejection(requestDelegate);
                }
            });
        }

        return builder;
    }

    /// <summary>
    /// Whether the container can resolve <typeparamref name="TService"/>, asked without resolving it: the service
    /// may be registered per request, and mapping runs outside one.
    /// </summary>
    private static bool _IsRegistered<TService>(IServiceProvider services)
    {
        // A container that cannot say is not held to the check: a request with no owner is still refused.
        return services.GetService<IServiceProviderIsService>()?.IsService(typeof(TService)) ?? true;
    }
}
