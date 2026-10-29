// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.RateLimiting;

/// <summary>
/// Fails startup when the cache <see cref="AttemptLimiterOptions.CacheName"/> selects is not registered, so a missing
/// provider or a misspelled instance name refuses the start instead of failing the first attempt.
/// </summary>
internal sealed class AttemptLimiterCacheStartupValidator(
    IOptions<AttemptLimiterOptions> options,
    IServiceProvider services
) : IHeadlessStartupValidator
{
    private const string _RequiredBy = "Headless attempt limiter counters";

    /// <inheritdoc/>
    /// <exception cref="MissingRequiredServiceException">The selected cache has no registration.</exception>
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        // Probe the registration, never resolve it: resolving would build the cache (a Redis client, for example)
        // and turn a provider misconfiguration into a failure attributed to this check.
        var cacheName = options.Value.CacheName;

        if (cacheName is null)
        {
            if (!services.GetRequiredService<IServiceProviderIsService>().IsService(typeof(ICache)))
            {
                _Throw(
                    "ICache",
                    "Call AddHeadlessCaching(...) with a shared provider (UseRedis / UseHybrid); UseInMemory counts "
                        + "per process."
                );
            }
        }
        else if (
            !services.GetRequiredService<IServiceProviderIsKeyedService>().IsKeyedService(typeof(ICache), cacheName)
        )
        {
            _Throw(
                $"attempt limiter cache '{cacheName}'",
                $"Add setup.AddNamed(\"{cacheName}\", ...) inside AddHeadlessCaching(...), or leave "
                    + $"{nameof(AttemptLimiterOptions)}.{nameof(AttemptLimiterOptions.CacheName)} null to use the "
                    + "default cache."
            );
        }

        return Task.CompletedTask;
    }

    private static void _Throw(string service, string remedy)
    {
        throw new MissingRequiredServiceException(
            $"Headless startup validation failed: {service} is not registered (required by {_RequiredBy}). {remedy}",
            [new RequiredServiceRegistration(typeof(ICache), _RequiredBy, remedy)]
        );
    }
}
