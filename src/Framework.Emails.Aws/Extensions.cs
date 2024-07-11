using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Framework.Emails.Aws;

public static class Extensions
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
    public static void AddAwsSesEmailSender(this IHostApplicationBuilder builder, AWSOptions? options)
    {
        builder.Services.TryAddAWSService<IAmazonSimpleEmailServiceV2>(options);
        builder.Services.AddSingleton<IEmailSender, AwsSesEmailSender>();
    }
}
