// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Emails.Aws;

[PublicAPI]
public static class AddAwsSesExtensions
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
    public static IServiceCollection AddAwsSesEmailSender(this IServiceCollection services, AWSOptions? options)
    {
        services.TryAddAWSService<IAmazonSimpleEmailServiceV2>(options);
        services.AddSingleton<IEmailSender, AwsSesEmailSender>();

        return services;
    }
}
