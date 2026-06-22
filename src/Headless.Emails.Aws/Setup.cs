// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Emails.Aws;

/// <summary>
/// Extension members for selecting Amazon SES v2 as a Headless email provider — the default (unkeyed)
/// sender on <see cref="HeadlessEmailsSetupBuilder"/> or a named sender on
/// <see cref="HeadlessEmailInstanceBuilder"/>.
/// </summary>
[PublicAPI]
public static class SetupAwsSes
{
    extension(HeadlessEmailsSetupBuilder setup)
    {
        /// <summary>
        /// Selects Amazon Simple Email Service v2 as the default email provider.
        /// </summary>
        /// <param name="options">
        /// AWS configuration (region, credentials). Pass <see langword="null"/> to use the
        /// default <see cref="AWSOptions"/> already registered in the DI container (for example
        /// from <c>builder.Configuration.GetAWSOptions()</c>).
        /// </param>
        /// <returns>The same builder for chaining.</returns>
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
        /// services.AddHeadlessEmails(setup => setup.UseAwsSes(awsOptions));
        ///
        /// // Or pass null to rely on the AWSOptions already in DI:
        /// services.AddHeadlessEmails(setup => setup.UseAwsSes(null));
        /// </code>
        /// </remarks>
        public HeadlessEmailsSetupBuilder UseAwsSes(AWSOptions? options)
        {
            setup.RegisterDefaultProvider(services => _AddAwsSes(services, options));

            return setup;
        }
    }

    private static void _AddAwsSes(IServiceCollection services, AWSOptions? options)
    {
        services.TryAddAWSService<IAmazonSimpleEmailServiceV2>(options);
        services.AddSingleton<IEmailSender, AwsSesEmailSender>();
    }
}
