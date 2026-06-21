// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Blobs.Aws;

/// <summary>Extension methods to register the AWS S3 blob storage provider.</summary>
[PublicAPI]
public static class SetupAwsS3
{
    /// <summary>
    /// Registers <see cref="AwsBlobStorage"/> as <see cref="IBlobStorage"/> and <see cref="IPresignedUrlBlobStorage"/>
    /// (same singleton instance) using the provided <see cref="AWSOptions"/> for S3 client configuration.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="awsOptions">
    /// AWS SDK options specifying credentials and region. Pass <see langword="null"/> to resolve from the
    /// <see cref="AWSOptions"/> already registered in DI (for example via <c>GetAWSOptions()</c>):
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
    /// </param>
    /// <param name="setupAction">Optional delegate to configure <see cref="AwsBlobStorageOptions"/>.</param>
    public static IServiceCollection AddAwsS3BlobStorage(
        this IServiceCollection services,
        AWSOptions? awsOptions = null,
        Action<AwsBlobStorageOptions>? setupAction = null
    )
    {
        var optionsBuilder = services.AddOptions<AwsBlobStorageOptions, AwsBlobStorageOptionsValidator>();

        if (setupAction is not null)
        {
            optionsBuilder.Configure(setupAction);
        }

        services.TryAddAWSService<IAmazonS3>(awsOptions);
        services.TryAddSingleton<IBlobNamingNormalizer, AwsBlobNamingNormalizer>();
        services.AddSingleton<IBlobStorage, AwsBlobStorage>();

        // The engine implements IPresignedUrlBlobStorage; expose it for direct injection too (same instance).
        services.TryAddSingleton<IPresignedUrlBlobStorage>(serviceProvider =>
            (IPresignedUrlBlobStorage)serviceProvider.GetRequiredService<IBlobStorage>()
        );

        return services;
    }
}
