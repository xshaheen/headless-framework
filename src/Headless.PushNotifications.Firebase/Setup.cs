// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Headless.Checks;
using Headless.PushNotifications.Abstractions;
using Headless.PushNotifications.Firebase.Internals;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Headless.PushNotifications.Firebase;

[PublicAPI]
public static class FirebaseSetup
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFirebasePushNotifications(IConfigurationSection configuration)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(configuration);

            var firebaseOptions = configuration.GetRequired<FirebaseOptions, FirebaseOptionsValidator>();
            services.Configure<FirebaseOptions, FirebaseOptionsValidator>(configuration);
            return services._AddCore(firebaseOptions);
        }

        public IServiceCollection AddFirebasePushNotifications(Action<FirebaseOptions> options)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(options);

            var firebaseOptions = new FirebaseOptions { Json = string.Empty };
            options(firebaseOptions);
            services.Configure<FirebaseOptions, FirebaseOptionsValidator>(options);

            return services._AddCore(firebaseOptions);
        }

        public IServiceCollection AddFirebasePushNotifications(FirebaseOptions options)
        {
            Argument.IsNotNull(services);
            Argument.IsNotNull(options);

            return services._AddCore(options);
        }

        private IServiceCollection _AddCore(FirebaseOptions options)
        {
            if (FirebaseApp.DefaultInstance is null)
            {
                FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(options.Json) });
            }

            // Register resilience pipeline for FCM retry logic
            services.AddResiliencePipeline(
                "Headless:FcmRetry",
                builder =>
                {
                    builder.AddRetry(
                        new RetryStrategyOptions
                        {
                            ShouldHandle = new PredicateBuilder()
                                .Handle<FirebaseMessagingException>(RetryHelper.IsTransientError)
                                .Handle<HttpRequestException>()
                                // Only retry timeouts, not user-initiated cancellation
                                .Handle<TaskCanceledException>(ex => !ex.CancellationToken.IsCancellationRequested),
                            MaxRetryAttempts = options.Retry.MaxAttempts,
                            Delay = TimeSpan.FromSeconds(1), // Initial delay
                            BackoffType = DelayBackoffType.Exponential,
                            UseJitter = options.Retry.UseJitter,
                            MaxDelay = options.Retry.MaxDelay,
                            DelayGenerator = args =>
                            {
                                // Honor Retry-After header for rate limits
                                if (
                                    args.Outcome.Exception is FirebaseMessagingException
                                    {
                                        MessagingErrorCode: MessagingErrorCode.QuotaExceeded,
                                    } ex
                                )
                                {
                                    var delay = RetryHelper.GetRetryAfterDelay(ex, options.Retry.RateLimitDelay);
                                    return ValueTask.FromResult<TimeSpan?>(delay);
                                }

                                // Use default exponential backoff for other transient errors
                                return ValueTask.FromResult<TimeSpan?>(null);
                            },
                            OnRetry = args =>
                            {
                                // Respect user cancellation immediately
                                args.Context.CancellationToken.ThrowIfCancellationRequested();

                                // Log retry attempt
                                var loggerKey = new ResiliencePropertyKey<ILogger>("logger");
                                if (args.Context.Properties.TryGetValue(loggerKey, out var loggerInstance))
                                {
                                    loggerInstance.LogRetryAttempt(
                                        args.AttemptNumber,
                                        args.RetryDelay.TotalSeconds,
                                        args.Outcome.Exception?.Message ?? "Unknown error"
                                    );
                                }

                                // OpenTelemetry Activity tracking
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

            services.AddSingleton<IPushNotificationService, FcmPushNotificationService>();

            return services;
        }
    }
}
