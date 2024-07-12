using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Blobs.Aws;

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
    public static IHostApplicationBuilder AddAwsS3BlobStorage(
        this IHostApplicationBuilder builder,
        AWSOptions? awsOptions = null
    )
    {
        builder.Services.TryAddAWSService<IAmazonS3>(awsOptions);
        builder.Services.AddSingleton<IBlobNamingNormalizer, AwsBlobNamingNormalizer>();
        builder.Services.AddSingleton<IBlobStorage, AwsBlobStorage>();

        return builder;
    }
}
