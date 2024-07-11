using Framework.Arguments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Integrations.Recaptcha;

/// <summary>Extension methods for adding reCAPTCHA services to the DI container.</summary>
public static class Extensions
{
    /// <summary>Adds services required for using reCAPTCHA.</summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="section">The configuration section that contains reCAPTCHA settings.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddRecaptcha(this IServiceCollection services, IConfiguration section)
    {
        Argument.IsNotNull(services);
        Argument.IsNotNull(section);

        services.ConfigureSingleton<RecaptchaSettings, RecaptchaSettingsValidator>(section);

        services
            .AddSingleton<IRecaptchaV2Service, RecaptchaV2Service>()
            .AddHttpClient<IRecaptchaV2Service, RecaptchaV2Service>(
                name: "recaptcha-client",
                configureClient: client => client.BaseAddress = new Uri("https://www.google.com")
            )
            // See: https://devblogs.microsoft.com/dotnet/building-resilient-cloud-services-with-dotnet-8/#standard-resilience-pipeline
            .AddStandardResilienceHandler();

        return services;
    }
}
