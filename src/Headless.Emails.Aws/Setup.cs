// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails.Aws;

/// <summary>
/// Registers the Amazon SES v2 email sender with the DI container.
/// </summary>
[PublicAPI]
public static class SetupAwsSes
{
    /// <summary>
    /// Registers <see cref="AwsSesEmailSender"/> as the <see cref="IEmailSender"/> singleton
    /// backed by Amazon Simple Email Service v2.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="options">
    /// AWS configuration (region, credentials). Pass <see langword="null"/> to use the
    /// default <see cref="AWSOptions"/> already registered in the DI container (for example
    /// from <c>builder.Configuration.GetAWSOptions()</c>).
    /// </param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    /// <remarks>
    /// <code>
    /// // From configuration:
    /// var awsOptions = builder.Configuration.GetAWSOptions();
    ///
    /// // Explicit credentials:
    /// var awsOptions = new AWSOptions
    /// {
    ///     Region = RegionEndpoint.GetBySystemName(builder.Configuration["AWS:Region"]),
    ///     Credentials = new BasicAWSCredentials(
    ///         builder.Configuration["AWS:AccessKey"],
    ///         builder.Configuration["AWS:SecretKey"]),
    /// };
    ///
    /// // Or pass null to rely on the AWSOptions already in DI:
    /// services.AddAwsSesEmailSender(null);
    /// </code>
    /// </remarks>
    public static IServiceCollection AddAwsSesEmailSender(this IServiceCollection services, AWSOptions? options)
    {
        services.TryAddAWSService<IAmazonSimpleEmailServiceV2>(options);
        services.AddSingleton<IEmailSender, AwsSesEmailSender>();

        return services;
    }
}
