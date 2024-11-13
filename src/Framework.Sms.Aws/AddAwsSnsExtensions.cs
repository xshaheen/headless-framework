// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Sms.Aws;

[PublicAPI]
public static class AddAwsSnsExtensions
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
    public static IServiceCollection AddAwsSnsSmsSender(this IServiceCollection services, AWSOptions? awsOptions = null)
    {
        services.TryAddAWSService<IAmazonSimpleNotificationService>(awsOptions);
        services.AddSingleton<ISmsSender, AwsSnsSmsSender>();

        return services;
    }
}
