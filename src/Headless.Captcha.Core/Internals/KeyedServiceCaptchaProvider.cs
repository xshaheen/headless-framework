// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Captcha.Internals;

/// <summary>
/// <see cref="ICaptchaProvider"/> over the container's keyed <see cref="ICaptchaVerifier"/> registrations — resolves
/// both named instances (added through the name-taking setup overloads) and a default provider's canonical key.
/// </summary>
internal sealed class KeyedServiceCaptchaProvider(
    IServiceProvider serviceProvider,
    IReadOnlySet<string> registeredNames
) : ICaptchaProvider
{
    public IReadOnlySet<string> RegisteredNames => registeredNames;

    public ICaptchaVerifier GetVerifier(string name)
    {
        Argument.IsNotNullOrEmpty(name);

        return serviceProvider.GetKeyedService<ICaptchaVerifier>(name)
            ?? throw new InvalidOperationException(
                $"No captcha verifier is registered under the name '{name}'. "
                    + (
                        registeredNames.Count > 0
                            ? $"Registered names: {string.Join(", ", registeredNames)}. "
                            : "No captcha providers are registered. "
                    )
                    + $"Register it through the builder first — for example AddHeadlessCaptcha(b => b.UseTurnstile(\"{name}\", …)) "
                    + $"or b.UseReCaptchaV3(\"{name}\", …) — or use a default provider's canonical key (one of the "
                    + "'Headless.Captcha:' constants on CaptchaConstants)."
            );
    }

    public ICaptchaVerifier? GetVerifierOrNull(string name)
    {
        Argument.IsNotNullOrEmpty(name);

        return serviceProvider.GetKeyedService<ICaptchaVerifier>(name);
    }
}
