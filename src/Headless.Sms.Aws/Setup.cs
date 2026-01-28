// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Sms.Aws;

[PublicAPI]
public static class AwsSnsSetup
{
    /// <summary>
    /// AWSOptions usage:
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
    public static IServiceCollection AddAwsSnsSmsSender(
        this IServiceCollection services,
        IConfiguration config,
        AWSOptions? awsOptions = null
    )
    {
        services.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(config);

        return _AddCore(services, awsOptions);
    }

    /// <inheritdoc cref="AddAwsSnsSmsSender(IServiceCollection,IConfiguration,AWSOptions?)"/>
    public static IServiceCollection AddAwsSnsSmsSender(
        this IServiceCollection services,
        Action<AwsSnsSmsOptions> setupAction,
        AWSOptions? awsOptions = null
    )
    {
        services.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction);

        return _AddCore(services, awsOptions);
    }

    /// <inheritdoc cref="AddAwsSnsSmsSender(IServiceCollection,IConfiguration,AWSOptions?)"/>
    public static IServiceCollection AddAwsSnsSmsSender(
        this IServiceCollection services,
        Action<AwsSnsSmsOptions, IServiceProvider> setupAction,
        AWSOptions? awsOptions = null
    )
    {
        services.Configure<AwsSnsSmsOptions, AwsSnsSmsOptionsValidator>(setupAction);

        return _AddCore(services, awsOptions);
    }

    private static IServiceCollection _AddCore(IServiceCollection services, AWSOptions? awsOptions)
    {
        services.TryAddAWSService<IAmazonSimpleNotificationService>(awsOptions);
        services.AddSingleton<ISmsSender, AwsSnsSmsSender>();

        return services;
    }
}
