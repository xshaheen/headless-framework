// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleNotificationService;
using Headless.Checks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms.Aws;

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
            setup.RegisterExtension(new AwsSnsProviderOptionsExtension(config, awsOptions));

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
            setup.RegisterExtension(new AwsSnsProviderOptionsExtension(setupAction, awsOptions));

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
            setup.RegisterExtension(new AwsSnsProviderOptionsExtension(setupAction, awsOptions));

            return setup;
        }
    }

    private sealed class AwsSnsProviderOptionsExtension : ISmsProviderOptionsExtension
    {
        private readonly Action<IServiceCollection> _configureOptions;
        private readonly AWSOptions? _awsOptions;

        public AwsSnsProviderOptionsExtension(IConfiguration config, AWSOptions? awsOptions)
        {
            _configureOptions = services => services.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(config);
            _awsOptions = awsOptions;
        }

        public AwsSnsProviderOptionsExtension(Action<AwsSnsSmsOptions> setupAction, AWSOptions? awsOptions)
        {
            _configureOptions = services =>
                services.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction);
            _awsOptions = awsOptions;
        }

        public AwsSnsProviderOptionsExtension(
            Action<AwsSnsSmsOptions, IServiceProvider> setupAction,
            AWSOptions? awsOptions
        )
        {
            _configureOptions = services =>
                services.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction);
            _awsOptions = awsOptions;
        }

        public void AddServices(IServiceCollection services)
        {
            _configureOptions(services);
            services.TryAddAWSService<IAmazonSimpleNotificationService>(_awsOptions);
            services.AddSingleton<ISmsSender, AwsSnsSmsSender>();
        }
    }
}
