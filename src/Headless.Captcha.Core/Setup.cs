// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha.Internals;
using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Captcha;

[PublicAPI]
public static class SetupCaptcha
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers Headless captcha from a single setup builder. Provider packages contribute through
        /// <c>Use*</c> extensions on <see cref="HeadlessCaptchaSetupBuilder"/>; at least one provider is required.
        /// All contributions are deferred until the setup gates pass, so a failed setup leaves the service
        /// collection unchanged.
        /// </summary>
        /// <param name="configure">The setup action selecting the providers.</param>
        /// <returns>The service collection for chaining.</returns>
        public IServiceCollection AddHeadlessCaptcha(Action<HeadlessCaptchaSetupBuilder> configure)
        {
            Argument.IsNotNull(configure);

            var setup = new HeadlessCaptchaSetupBuilder(services);
            configure(setup);

            return _AddCaptchaCore(services, setup);
        }
    }

    private static IServiceCollection _AddCaptchaCore(IServiceCollection services, HeadlessCaptchaSetupBuilder setup)
    {
        if (setup.DefaultRegistrations.Count == 0 && setup.NamedRegistrations.Count == 0)
        {
            throw new InvalidOperationException(
                "Headless.Captcha requires at least one provider. Call one of `UseReCaptchaV2`, `UseReCaptchaV3`, "
                    + "or `UseTurnstile`."
            );
        }

        if (services.Any(static descriptor => descriptor.ServiceType == typeof(CaptchaProviderRegistration)))
        {
            throw new InvalidOperationException(
                "AddHeadlessCaptcha was already called on this service collection. Configure all captcha providers "
                    + "in a single AddHeadlessCaptcha call."
            );
        }

        // Snapshot the finalized name set so ICaptchaProvider can enumerate registered names and produce an
        // actionable error listing them. Covers a default's canonical key plus every named instance.
        var registeredNames = setup.RegisteredNames.ToFrozenSet(StringComparer.Ordinal);

        services.AddSingleton(new CaptchaProviderRegistration());
        services.TryAddSingleton<ICaptchaProvider>(provider => new KeyedServiceCaptchaProvider(
            provider,
            registeredNames
        ));

        foreach (var action in setup.DefaultRegistrations)
        {
            action(services);
        }

        foreach (var action in setup.NamedRegistrations)
        {
            action(services);
        }

        return services;
    }

    private sealed record CaptchaProviderRegistration;
}
