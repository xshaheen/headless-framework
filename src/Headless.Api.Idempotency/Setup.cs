// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.Api.Idempotency;

[PublicAPI]
public static class SetupIdempotency
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddIdempotency(IConfiguration configuration)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(configuration);
            return services._AddIdempotencyCore();
        }

        public IServiceCollection AddIdempotency(Action<IdempotencyOptions> setupAction)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(setupAction);
            return services._AddIdempotencyCore();
        }

        public IServiceCollection AddIdempotency(Action<IdempotencyOptions, IServiceProvider> setupAction)
        {
            services.Configure<IdempotencyOptions, IdempotencyOptionsValidator>(setupAction);
            return services._AddIdempotencyCore();
        }

        private IServiceCollection _AddIdempotencyCore()
        {
            services.TryAddScoped<IdempotencyMiddleware>();

            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsDIValidator>()
            );

            services.PostConfigure<IdempotencyOptions>(o =>
                o.ShouldCacheResponse ??= DefaultCachePredicate.Instance
            );

            return services;
        }
    }
}

/// <summary>
/// DI-aware validator that fails fast at host startup when
/// <see cref="IdempotencyOptions.InFlightStrategy"/> is
/// <see cref="InFlightStrategy.WaitAndReplay"/> but no
/// <see cref="IDistributedLockProvider"/> is registered.
/// </summary>
internal sealed class IdempotencyOptionsDIValidator(IServiceProvider serviceProvider)
    : IValidateOptions<IdempotencyOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options)
    {
        if (options.InFlightStrategy != InFlightStrategy.WaitAndReplay)
        {
            return ValidateOptionsResult.Success;
        }

        var lockProvider = serviceProvider.GetService<IDistributedLockProvider>();

        if (lockProvider is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{nameof(IdempotencyOptions)}.{nameof(IdempotencyOptions.InFlightStrategy)} = "
            + $"{nameof(InFlightStrategy.WaitAndReplay)} requires {nameof(IDistributedLockProvider)} "
            + "to be registered. Either switch InFlightStrategy to Reject or register a distributed-lock provider."
        );
    }
}
