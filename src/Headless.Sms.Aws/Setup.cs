// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleNotificationService;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Aws;

/// <summary>
/// Extension members for selecting AWS SNS as the default (unkeyed) SMS provider on
/// <see cref="HeadlessSmsSetupBuilder"/>. Named instances are configured through
/// <see cref="SetupAwsSnsNamed"/>.
/// </summary>
[PublicAPI]
public static class SetupAwsSns
{
    extension(HeadlessSmsSetupBuilder setup)
    {
        /// <summary>
        /// Selects AWS SNS, binding and validating <see cref="AwsSnsSmsOptions"/> from configuration.
        /// </summary>
        /// <remarks>
        /// <para>AWS credentials and region are taken from <paramref name="awsOptions"/> when provided, or from the
        /// default AWS SDK credential chain (environment variables, instance profile, etc.) when
        /// <see langword="null"/>.</para>
        /// <para>Example <c>AWSOptions</c> patterns:</para>
        /// <code>
        /// var awsOptions = builder.Configuration.GetAWSOptions();
        /// // or
        /// var awsOptions = new AWSOptions
        /// {
        ///     Region = RegionEndpoint.GetBySystemName(builder.Configuration["AWS:Region"]),
        ///     Credentials = new BasicAWSCredentials(builder.Configuration["AWS:AccessKey"], builder.Configuration["AWS:SecretKey"]),
        /// };
        /// // or pass null to use the default AWSOptions registered in the DI container
        /// </code>
        /// </remarks>
        /// <param name="config">Configuration section containing <see cref="AwsSnsSmsOptions"/> values.</param>
        /// <param name="awsOptions">Optional AWS credentials and region override. <see langword="null"/> uses the SDK default chain.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseAwsSns(IConfiguration config, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(config);

            setup.RegisterDefaultProvider(services =>
                AddAwsSnsSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(config, n),
                    awsOptions
                )
            );

            return setup;
        }

        /// <summary>Selects AWS SNS, configuring <see cref="AwsSnsSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="awsOptions">Optional AWS credentials and region override. <see langword="null"/> uses the SDK default chain.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseAwsSns(Action<AwsSnsSmsOptions> setupAction, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddAwsSnsSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction, n),
                    awsOptions
                )
            );

            return setup;
        }

        /// <summary>Selects AWS SNS, configuring <see cref="AwsSnsSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="awsOptions">Optional AWS credentials and region override. <see langword="null"/> uses the SDK default chain.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseAwsSns(
            Action<AwsSnsSmsOptions, IServiceProvider> setupAction,
            AWSOptions? awsOptions = null
        )
        {
            Argument.IsNotNull(setupAction);

            setup.RegisterDefaultProvider(services =>
                AddAwsSnsSmsCore(
                    services,
                    name: null,
                    (s, n) => s.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction, n),
                    awsOptions
                )
            );

            return setup;
        }
    }

    /// <summary>
    /// Registers the AWS SNS SMS sender. <paramref name="name"/> <see langword="null"/> registers the default
    /// (unkeyed) sender via <c>TryAddAWSService</c>; a non-null name registers a keyed sender and a keyed
    /// <see cref="IAmazonSimpleNotificationService"/> client built from the supplied (or ambient)
    /// <see cref="AWSOptions"/>. <c>TryAddAWSService</c> has no keyed overload, so the named client is
    /// constructed explicitly via <see cref="AWSOptions.CreateServiceClient{T}"/>. Every sender factory reads
    /// the options snapshot for its own name (<c>IOptionsMonitor.Get(name)</c>) so keyed settings never bleed
    /// across instances. AWS SNS sends to one recipient per API call, so no <see cref="IBulkSmsSender"/>
    /// forward is registered.
    /// </summary>
    internal static void AddAwsSnsSmsCore(
        IServiceCollection services,
        string? name,
        Action<IServiceCollection, string?> configureOptions,
        AWSOptions? awsOptions
    )
    {
        configureOptions(services, name);

        if (name is null)
        {
            services.TryAddAWSService<IAmazonSimpleNotificationService>(awsOptions);

            services.AddSingleton<ISmsSender>(static sp => new AwsSnsSmsSender(
                sp.GetRequiredService<IAmazonSimpleNotificationService>(),
                sp.GetRequiredService<IOptionsMonitor<AwsSnsSmsOptions>>(),
                optionsName: null,
                sp.GetRequiredService<ILogger<AwsSnsSmsSender>>()
            ));

            return;
        }

        // Capture `name` (keyed DI does not cascade the key) and resolve the client through the same lookup
        // order TryAddAWSService(null) uses: supplied options, then the ambient AWSOptions in DI, then
        // IConfiguration (AWS:* via GetAWSOptions), then defaults. Skipping the IConfiguration step would make
        // a null-options named sender diverge from the null-options default sender.
        services.AddKeyedSingleton<IAmazonSimpleNotificationService>(
            name,
            (sp, _) =>
            {
                var resolved =
                    awsOptions
                    ?? sp.GetService<AWSOptions>()
                    ?? sp.GetService<IConfiguration>()?.GetAWSOptions()
                    ?? new AWSOptions();

                return resolved.CreateServiceClient<IAmazonSimpleNotificationService>();
            }
        );

        services.AddKeyedSingleton<ISmsSender>(
            name,
            (sp, _) =>
                new AwsSnsSmsSender(
                    sp.GetRequiredKeyedService<IAmazonSimpleNotificationService>(name),
                    sp.GetRequiredService<IOptionsMonitor<AwsSnsSmsOptions>>(),
                    name,
                    sp.GetRequiredService<ILogger<AwsSnsSmsSender>>()
                )
        );
    }
}

/// <summary>
/// Extension members for selecting AWS SNS for a named SMS instance on
/// <see cref="HeadlessSmsInstanceBuilder"/>. The instance owns its own named options, keyed
/// <see cref="IAmazonSimpleNotificationService"/> client, and keyed sender; it never shares them with the
/// default sender or other named instances.
/// </summary>
[PublicAPI]
public static class SetupAwsSnsNamed
{
    extension(HeadlessSmsInstanceBuilder instance)
    {
        /// <summary>
        /// Uses AWS SNS for this named instance, binding and validating <see cref="AwsSnsSmsOptions"/> from configuration.
        /// </summary>
        /// <param name="config">Configuration section containing <see cref="AwsSnsSmsOptions"/> values.</param>
        /// <param name="awsOptions">Optional AWS credentials and region override. <see langword="null"/> uses the ambient <see cref="AWSOptions"/>, <c>AWS:*</c> configuration, or SDK defaults.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseAwsSns(IConfiguration config, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(config);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupAwsSns.AddAwsSnsSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(config, n),
                    awsOptions
                )
            );

            return instance;
        }

        /// <summary>Uses AWS SNS for this named instance, configuring <see cref="AwsSnsSmsOptions"/> via a delegate.</summary>
        /// <param name="setupAction">Delegate that populates the options.</param>
        /// <param name="awsOptions">Optional AWS credentials and region override. <see langword="null"/> uses the ambient <see cref="AWSOptions"/>, <c>AWS:*</c> configuration, or SDK defaults.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseAwsSns(Action<AwsSnsSmsOptions> setupAction, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupAwsSns.AddAwsSnsSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction, n),
                    awsOptions
                )
            );

            return instance;
        }

        /// <summary>Uses AWS SNS for this named instance, configuring <see cref="AwsSnsSmsOptions"/> with access to the service provider.</summary>
        /// <param name="setupAction">Delegate that populates the options, with access to the resolved service provider.</param>
        /// <param name="awsOptions">Optional AWS credentials and region override. <see langword="null"/> uses the ambient <see cref="AWSOptions"/>, <c>AWS:*</c> configuration, or SDK defaults.</param>
        /// <returns>The instance builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsInstanceBuilder UseAwsSns(
            Action<AwsSnsSmsOptions, IServiceProvider> setupAction,
            AWSOptions? awsOptions = null
        )
        {
            Argument.IsNotNull(setupAction);

            var name = instance.Name;

            instance.RegisterProvider(services =>
                SetupAwsSns.AddAwsSnsSmsCore(
                    services,
                    name,
                    (s, n) => s.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction, n),
                    awsOptions
                )
            );

            return instance;
        }
    }
}
