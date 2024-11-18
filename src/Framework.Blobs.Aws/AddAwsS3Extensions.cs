// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Blobs.Aws;

[PublicAPI]
public static class AddAwsS3Extensions
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
    public static IServiceCollection AddAwsS3BlobStorage(
        this IServiceCollection services,
        AWSOptions? awsOptions = null,
        Action<AwsBlobStorageOptions>? setupAction = null
    )
    {
        var optionsBuilder = services.AddOptions<AwsBlobStorageOptions>();

        if (setupAction is not null)
        {
            optionsBuilder.Configure(setupAction);
        }

        services.TryAddAWSService<IAmazonS3>(awsOptions);
        services.AddSingleton<IBlobNamingNormalizer, AwsBlobNamingNormalizer>();
        services.AddSingleton<IBlobStorage, AwsBlobStorage>();

        return services;
    }
}
