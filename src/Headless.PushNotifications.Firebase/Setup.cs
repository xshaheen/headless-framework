// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using FirebaseAdmin.Messaging;
using Headless.Checks;
using Headless.PushNotifications.Firebase.Internals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Registers the Firebase Cloud Messaging provider on a <see cref="HeadlessPushNotificationsSetupBuilder"/>.
/// </summary>
/// <remarks>
/// The Firebase app and credentials are created lazily on the first send (not during registration), so
/// configuration errors surface through the options validator at startup rather than as registration-time
/// side effects, and several hosts can coexist in one process with different credentials.
/// </remarks>
[PublicAPI]
public static class SetupFirebasePushNotifications
{
    extension(HeadlessPushNotificationsSetupBuilder setup)
    {
        /// <summary>Selects Firebase, binding and validating <see cref="FirebaseOptions"/> from configuration.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(IConfiguration configuration)
        {
            Argument.IsNotNull(configuration);
            setup.RegisterExtension(new FirebaseProviderOptionsExtension(configuration));

            return setup;
        }

        /// <summary>Selects Firebase, configuring <see cref="FirebaseOptions"/> via a delegate.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(Action<FirebaseOptions> configure)
        {
            Argument.IsNotNull(configure);
            setup.RegisterExtension(new FirebaseProviderOptionsExtension(configure));

            return setup;
        }

        /// <summary>Selects Firebase, configuring <see cref="FirebaseOptions"/> with access to the service provider.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="configure"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(Action<FirebaseOptions, IServiceProvider> configure)
        {
            Argument.IsNotNull(configure);
            setup.RegisterExtension(new FirebaseProviderOptionsExtension(configure));

            return setup;
        }

        /// <summary>Selects Firebase from a pre-built options instance (validated at startup).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
        public HeadlessPushNotificationsSetupBuilder UseFirebase(FirebaseOptions options)
        {
            Argument.IsNotNull(options);
            setup.RegisterExtension(new FirebaseProviderOptionsExtension(options));

            return setup;
        }
    }

    private static void _AddFirebaseRetryPipeline(IServiceCollection services)
    {
        services.AddResiliencePipeline(
            FcmResilienceKeys.RetryPipeline,
            static (builder, context) =>
            {
                var serviceProvider = context.ServiceProvider;
                var retry = serviceProvider.GetRequiredService<IOptions<FirebaseOptions>>().Value.Retry;
                var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
                var logger = serviceProvider.GetRequiredService<ILogger<FcmMessageSender>>();

                // MaxAttempts == 0 disables retry: leave the pipeline empty (pass-through).
                if (retry.MaxAttempts == 0)
                {
                    return;
                }

                builder.AddRetry(
                    new RetryStrategyOptions
                    {
                        ShouldHandle = new PredicateBuilder()
                            .Handle<FirebaseMessagingException>(RetryHelper.IsTransientError)
                            .Handle<HttpRequestException>()
                            // Only retry timeouts, not user-initiated cancellation.
                            .Handle<TaskCanceledException>(static ex => !ex.CancellationToken.IsCancellationRequested),
                        MaxRetryAttempts = retry.MaxAttempts,
                        Delay = TimeSpan.FromSeconds(1), // Initial delay.
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = retry.UseJitter,
                        MaxDelay = retry.MaxDelay,
                        DelayGenerator = args =>
                        {
                            // Honor (and cap) the Retry-After header for rate limits. Polly ignores MaxDelay for
                            // DelayGenerator output, so GetRetryAfterDelay applies the cap itself.
                            if (
                                args.Outcome.Exception is FirebaseMessagingException
                                {
                                    MessagingErrorCode: MessagingErrorCode.QuotaExceeded,
                                } ex
                            )
                            {
                                var delay = RetryHelper.GetRetryAfterDelay(
                                    ex,
                                    retry.RateLimitDelay,
                                    retry.MaxDelay,
                                    timeProvider
                                );

                                return ValueTask.FromResult<TimeSpan?>(delay);
                            }

                            // Use default exponential backoff for other transient errors.
                            return ValueTask.FromResult<TimeSpan?>(null);
                        },
                        OnRetry = args =>
                        {
                            logger.LogRetryAttempt(
                                args.AttemptNumber,
                                args.RetryDelay.TotalSeconds,
                                args.Outcome.Exception?.Message ?? "Unknown error"
                            );

                            var activity = Activity.Current;
                            activity?.AddEvent(
                                new ActivityEvent(
                                    "FCM Retry",
                                    tags: new ActivityTagsCollection
                                    {
                                        ["retry.attempt"] = args.AttemptNumber,
                                        ["retry.delay_seconds"] = args.RetryDelay.TotalSeconds,
                                        ["error.type"] = args.Outcome.Exception?.GetType().Name,
                                    }
                                )
                            );

                            return default;
                        },
                    }
                );
            }
        );
    }

    private sealed class FirebaseProviderOptionsExtension : IPushNotificationsProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions;

        public FirebaseProviderOptionsExtension(IConfiguration configuration)
        {
            _configureOptions = services =>
                services.Configure<FirebaseOptions, FirebaseOptionsValidator>(configuration);
        }

        public FirebaseProviderOptionsExtension(Action<FirebaseOptions> configure)
        {
            _configureOptions = services => services.Configure<FirebaseOptions, FirebaseOptionsValidator>(configure);
        }

        public FirebaseProviderOptionsExtension(Action<FirebaseOptions, IServiceProvider> configure)
        {
            _configureOptions = services => services.Configure<FirebaseOptions, FirebaseOptionsValidator>(configure);
        }

        public FirebaseProviderOptionsExtension(FirebaseOptions options)
        {
            _configureOptions = services =>
                services.Configure<FirebaseOptions, FirebaseOptionsValidator>(target =>
                {
                    target.Json = options.Json;
                    target.Retry = options.Retry;
                });
        }

        public void AddServices(IServiceCollection services)
        {
            _configureOptions(services);
            services.TryAddSingleton(TimeProvider.System);
            _AddFirebaseRetryPipeline(services);
            services.TryAddSingleton<IFcmMessageSender, FcmMessageSender>();
            services.AddSingleton<IPushNotificationService, FcmPushNotificationService>();
        }
    }
}
