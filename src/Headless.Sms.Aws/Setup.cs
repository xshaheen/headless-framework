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
        /// <para>AWSOptions usage:</para>
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
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseAwsSns(IConfiguration config, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(config);
            setup.RegisterExtension(new AwsSnsProviderOptionsExtension(config, awsOptions));

            return setup;
        }

        /// <summary>Selects AWS SNS, configuring <see cref="AwsSnsSmsOptions"/> via a delegate (see the configuration overload for AWS credential/region setup).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="setupAction"/> is <see langword="null"/>.</exception>
        public HeadlessSmsSetupBuilder UseAwsSns(Action<AwsSnsSmsOptions> setupAction, AWSOptions? awsOptions = null)
        {
            Argument.IsNotNull(setupAction);
            setup.RegisterExtension(new AwsSnsProviderOptionsExtension(setupAction, awsOptions));

            return setup;
        }

        /// <summary>Selects AWS SNS, configuring <see cref="AwsSnsSmsOptions"/> via a delegate (see the configuration overload for AWS credential/region setup).</summary>
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
