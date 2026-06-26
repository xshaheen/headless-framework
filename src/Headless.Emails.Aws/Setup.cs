// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Extensions.NETCore.Setup;
using Amazon.SimpleEmailV2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1708 // multiple extension blocks emit marker members differing only by case
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
            setup.RegisterDefaultProvider(services => _AddEmailsCore(services, name: null, options));

            return setup;
        }
    }

    extension(HeadlessEmailInstanceBuilder instance)
    {
        /// <summary>
        /// Uses Amazon Simple Email Service v2 for this named instance. The instance owns its own keyed
        /// <see cref="IAmazonSimpleEmailServiceV2"/> client built from <paramref name="options"/>; it never
        /// touches the default sender.
        /// </summary>
        /// <param name="options">
        /// AWS configuration (region, credentials). Pass <see langword="null"/> to use the default
        /// <see cref="AWSOptions"/> already registered in the DI container.
        /// </param>
        /// <returns>The instance builder for chaining.</returns>
        public HeadlessEmailInstanceBuilder UseAwsSes(AWSOptions? options)
        {
            var name = instance.Name;

            instance.RegisterProvider(services => _AddEmailsCore(services, name, options));

            return instance;
        }
    }

    /// <summary>
    /// Registers the AWS SES email sender. <paramref name="name"/> <see langword="null"/> registers the default
    /// (unkeyed) sender via <c>TryAddAWSService</c>; a non-null name registers a keyed sender and a keyed
    /// <see cref="IAmazonSimpleEmailServiceV2"/> client built from the supplied (or ambient)
    /// <see cref="AWSOptions"/>. <c>TryAddAWSService</c> has no keyed overload, so the named client is
    /// constructed explicitly via <see cref="AWSOptions.CreateServiceClient{T}"/>.
    /// </summary>
    private static void _AddEmailsCore(IServiceCollection services, string? name, AWSOptions? options)
    {
        if (name is null)
        {
            services.TryAddAWSService<IAmazonSimpleEmailServiceV2>(options);
            services.AddSingleton<IEmailSender, AwsSesEmailSender>();

            return;
        }

        // Capture `name` (keyed DI does not cascade the key) and resolve the client through the same lookup
        // order TryAddAWSService(null) uses: supplied options, then the ambient AWSOptions in DI, then
        // IConfiguration (AWS:* via GetAWSOptions), then defaults. Skipping the IConfiguration step would make
        // a null-options named sender diverge from the null-options default sender.
        services.AddKeyedSingleton<IAmazonSimpleEmailServiceV2>(
            name,
            (sp, _) =>
            {
                var resolved =
                    options
                    ?? sp.GetService<AWSOptions>()
                    ?? sp.GetService<IConfiguration>()?.GetAWSOptions()
                    ?? new AWSOptions();

                return resolved.CreateServiceClient<IAmazonSimpleEmailServiceV2>();
            }
        );

        services.AddKeyedSingleton<IEmailSender>(
            name,
            (sp, _) =>
                new AwsSesEmailSender(
                    sp.GetRequiredKeyedService<IAmazonSimpleEmailServiceV2>(name),
                    sp.GetRequiredService<ILogger<AwsSesEmailSender>>()
                )
        );
    }
}
