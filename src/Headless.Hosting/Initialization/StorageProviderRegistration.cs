// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.DependencyInjection;

namespace Headless.Hosting.Initialization;

/// <summary>
/// Shared helpers for the storage-provider registration pattern used by
/// <c>Headless.{AuditLog,Features,Permissions,Settings}</c> Setup classes:
/// exactly-one-extension guard, cross-call duplicate-registration guard via a domain-specific
/// sentinel record, and the matching diagnostic exceptions. Centralises the wording so the
/// 4 domain Setup classes never drift on it.
/// </summary>
[PublicAPI]
public static class StorageProviderRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Validates that exactly one storage-provider extension was registered on a setup builder
        /// and that the host has not already wired another storage provider for the same domain
        /// (tracked by the <typeparamref name="TSentinel"/> singleton). Registers a fresh sentinel
        /// on success. Throws <see cref="InvalidOperationException"/> on either failure with a
        /// message that names the calling domain (e.g. <c>"Headless.Features"</c>) and lists the
        /// valid <c>Use…</c> entry points.
        /// </summary>
        /// <typeparam name="TSentinel">
        /// Per-domain marker record that the caller defines (and constructs from
        /// <paramref name="extensionTypeName"/>). Looking up its <see cref="ServiceDescriptor.ServiceType"/>
        /// across the service collection is the duplicate-call guard.
        /// </typeparam>
        /// <param name="extensionCount">Number of registered storage-options extensions on the setup builder.</param>
        /// <param name="extensionTypeName">Type name of the chosen storage extension (for diagnostics).</param>
        /// <param name="domainName">Domain label used in the diagnostic message (e.g. <c>"Headless.Features"</c>).</param>
        /// <param name="validProviderNames">The domain's valid <c>Use…</c> entry points, listed in the zero-provider diagnostic.</param>
        /// <param name="sentinelFactory">Constructs the per-domain sentinel from the extension type name.</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown when not exactly one storage provider was configured, or when a storage provider was
        /// already registered for the same domain.
        /// </exception>
        public void GuardSingleStorageProvider<TSentinel>(
            int extensionCount,
            string extensionTypeName,
            string domainName,
            IReadOnlyList<string> validProviderNames,
            Func<string, TSentinel> sentinelFactory
        )
            where TSentinel : class
        {
            if (extensionCount != 1)
            {
                throw new InvalidOperationException(
                    extensionCount == 0
                        ? $"{domainName} requires exactly one storage provider. Call one of {_FormatProviderNames(validProviderNames)}."
                        : $"{domainName} requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            // Cross-call guard — calling Add…(setup => …) twice on the same IServiceCollection
            // would otherwise wire two storage providers (and duplicate initializers/options/
            // services) without diagnostic. The sentinel singleton is unique per domain.
            if (services.Any(static d => d.ServiceType == typeof(TSentinel)))
            {
                throw new InvalidOperationException(
                    $"{domainName} requires exactly one storage provider. Multiple storage providers were configured."
                );
            }

            services.AddSingleton(sentinelFactory(extensionTypeName));
        }
    }

    private static string _FormatProviderNames(IReadOnlyList<string> names)
    {
        var quoted = names.Select(static name => $"`{name}`").ToArray();

        return quoted.Length switch
        {
            0 => "a storage provider",
            1 => quoted[0],
            _ => $"{string.Join(", ", quoted[..^1])}, or {quoted[^1]}",
        };
    }
}
