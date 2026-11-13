// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Tus.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Tus;

/// <summary>
/// One upload endpoint declared through <c>AddUploadEndpoint</c> inside <c>AddHeadlessTus</c>. Its limits are the
/// endpoint's <see cref="TusUploadEndpointOptions"/>, its members say who owns its uploads and which application
/// checks they must pass, and <c>MapHeadlessTus</c> maps it to a route.
/// </summary>
[PublicAPI]
public sealed class HeadlessTusUploadEndpointBuilder
{
    private readonly HeadlessTusSetupBuilder _setup;

    internal HeadlessTusUploadEndpointBuilder(HeadlessTusSetupBuilder setup, TusEndpointRegistration endpoint)
    {
        _setup = setup;
        Endpoint = endpoint;
    }

    internal TusEndpointRegistration Endpoint { get; }

    /// <summary>
    /// Adds an application check that every upload must pass before it starts, for example a quota.
    /// </summary>
    /// <typeparam name="TValidator">The validator type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// The endpoint's validators run in the order they are declared, after the endpoint's own rules, and the first
    /// failure answers the creating request with that validator's status and message. The validator is resolved
    /// from the request's services on every creating request. A type the application has not registered is
    /// registered as a scoped service.
    /// </remarks>
    public HeadlessTusUploadEndpointBuilder ValidateWith<TValidator>()
        where TValidator : class, ITusUploadValidator
    {
        Endpoint.Validators.Add(typeof(TValidator));
        _setup.RegisterCrossCuttingExtension(static services => services.TryAddScoped<TValidator>());

        return this;
    }

    /// <summary>
    /// Names the application's handler for the endpoint's finished uploads, which takes each one over or refuses
    /// it.
    /// </summary>
    /// <typeparam name="THandler">The handler type.</typeparam>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// An endpoint has one handler. It is resolved from the request's services each time it runs, and a type the
    /// application has not registered is registered as a scoped service. <see cref="ITusUploadCompletedHandler"/>
    /// describes when it runs and how often.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the endpoint already has a completion handler.</exception>
    public HeadlessTusUploadEndpointBuilder OnCompleted<THandler>()
        where THandler : class, ITusUploadCompletedHandler
    {
        if (Endpoint.CompletedHandler is { } declared)
        {
            // A second handler would replace the first without a word, and the uploads meant for it would never
            // reach it.
            throw new InvalidOperationException(
                $"{Endpoint.DisplayName} already has the completion handler {declared.Name}. An upload endpoint "
                    + "has one completion handler."
            );
        }

        Endpoint.CompletedHandler = typeof(THandler);
        _setup.RegisterCrossCuttingExtension(static services => services.TryAddScoped<THandler>());

        return this;
    }

    /// <summary>
    /// Names the owner of the endpoint's uploads from the request, in place of the signed-in user.
    /// </summary>
    /// <param name="ownerKey">
    /// Returns the owner key of the request, for example a workspace id read from the route, or
    /// <see langword="null"/> when the request has none.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// The key a creating request returns is recorded on the upload. Every later request for that upload must
    /// return the same key, compared by ordinal, or it is answered as not found, exactly like a request for an
    /// upload that does not exist. A request whose key is <see langword="null"/>, empty, or white space is refused
    /// with 401, and a creating request whose key is longer than 256 characters with 400.
    /// </para>
    /// <para>
    /// The endpoint trusts the key as returned: it does not check that the caller may act for that owner. Return
    /// a value the application has already authorized for the caller, for example behind an authorization policy
    /// on the mapped endpoint. The function runs on every request to the endpoint. Without it the owner is the
    /// signed-in user's id, read from <c>ICurrentUser</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ownerKey"/> is <see langword="null"/>.</exception>
    public HeadlessTusUploadEndpointBuilder OwnedBy(Func<HttpContext, string?> ownerKey)
    {
        Argument.IsNotNull(ownerKey);

        Endpoint.OwnerKey = context => ValueTask.FromResult(ownerKey(context));

        return this;
    }

    /// <inheritdoc cref="OwnedBy(Func{HttpContext, string})"/>
    /// <param name="ownerKey">
    /// Returns the owner key of the request, for example one looked up for the signed-in user, or
    /// <see langword="null"/> when the request has none.
    /// </param>
    public HeadlessTusUploadEndpointBuilder OwnedBy(Func<HttpContext, ValueTask<string?>> ownerKey)
    {
        Argument.IsNotNull(ownerKey);

        Endpoint.OwnerKey = ownerKey;

        return this;
    }
}
