// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using Headless.Checks;
using Headless.IO;
using Headless.Tus.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using tusdotnet.Interfaces;

namespace Headless.Tus;

/// <summary>
/// Entry point for registering Headless TUS resumable uploads. Call
/// <c>services.AddHeadlessTus(tus => tus.Use…(…))</c> once per application to select the upload store, declare
/// the upload endpoints, and configure what surrounds them.
/// </summary>
[PublicAPI]
public static class SetupTus
{
    private const string _StoresHint = "`UseS3`, `UseAzure`, or `UseStore`";

    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the host's tus upload store, the upload endpoints declared through <c>AddUploadEndpoint</c>,
        /// and the job that removes expired incomplete uploads. The <paramref name="configure"/> callback must
        /// select exactly one store through a <c>Use*</c> member, such as <c>tus.UseS3(…)</c>,
        /// <c>tus.UseAzure(…)</c>, or <c>tus.UseStore(…)</c>.
        /// </summary>
        /// <param name="configure">
        /// Delegate that selects the store, declares the upload endpoints, and configures locks and cleanup.
        /// </param>
        /// <returns>The same service collection for chaining.</returns>
        /// <remarks>
        /// The selected store resolves as the host's <see cref="ITusStore"/> and as its own type. Every
        /// contribution is deferred until the gates below pass, so a failed gate leaves the service collection
        /// unchanged. A declared upload endpoint serves nothing until <c>MapHeadlessTus</c> maps it. The cleanup
        /// job is on by default; <c>ConfigureExpiredUploadsCleanup</c> changes its interval or turns it off.
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when no store or more than one store is selected, or when <c>AddHeadlessTus</c> was already called
        /// on this service collection.
        /// </exception>
        public IServiceCollection AddHeadlessTus(Action<HeadlessTusSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessTusSetupBuilder();
            configure(setup);

            return _AddTusCore(services, setup);
        }
    }

    private static IServiceCollection _AddTusCore(IServiceCollection services, HeadlessTusSetupBuilder setup)
    {
        if (setup.StoreExtensions.Count != 1)
        {
            throw new InvalidOperationException(
                setup.StoreExtensions.Count == 0
                    ? $"Headless.Tus requires exactly one store. Call one of {_StoresHint}."
                    : $"Headless.Tus requires exactly one store. Multiple stores were configured; call only one of {_StoresHint}."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(TusRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessTus was already called on this service collection. Select the store and configure "
                    + "locks and cleanup in a single AddHeadlessTus call."
            );
        }

        services.AddSingleton(new TusRegistration());

        foreach (var action in setup.StoreExtensions)
        {
            action(services);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.Configure<TusExpiredUploadsCleanupOptions, TusExpiredUploadsCleanupOptionsValidator>(_ => { });

        // Registered even when no endpoint is declared, so MapHeadlessTus can tell a missing AddHeadlessTus call from
        // a name that was never declared.
        services.AddSingleton(new TusEndpointRegistry(setup.Endpoints.Values));
        services.AddSingleton<TusUploadAuthorizer>();
        services.AddSingleton<TusContentCheck>();
        services.AddSingleton<TusConfigurationFactory>();

        // The content check's collaborators. Both are TryAdd, so the ones an application registered are kept, for
        // example an inspector limited to the formats it accepts. The factory defers building the inspector's
        // format list to its first use.
        services.TryAddSingleton<IFileFormatInspector>(static _ => new FileFormatInspector(
            FileFormatLocator.GetFormats()
        ));
        services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();

        foreach (var action in setup.CrossCuttingExtensions)
        {
            action(services);
        }

        // Always registered: the options that turn the job off are only known once the host resolves them, so the
        // service decides at start whether it has anything to do.
        services.AddHostedService<TusExpiredUploadsCleanupService>();

        return services;
    }

    private sealed record TusRegistration;
}
