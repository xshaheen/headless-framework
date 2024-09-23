// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Sms.Aws;

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
    public static IHostApplicationBuilder AddAwsSnsSmsSender(
        this IHostApplicationBuilder builder,
        AWSOptions? awsOptions = null
    )
    {
        builder.Services.TryAddAWSService<IAmazonSimpleNotificationService>(awsOptions);
        builder.Services.AddSingleton<ISmsSender, AwsSnsSmsSender>();

        return builder;
    }
}
