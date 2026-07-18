// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;
using Headless.FluentValidation;

#pragma warning disable IDE0130 // FluentValidation integrations intentionally live in the integrated namespace.

namespace FluentValidation;

/// <summary>
/// FluentValidation extension rules for rejecting markup in free-text string properties.
/// </summary>
/// <remarks>
/// These rules are an input-hygiene aid, not a sanitizer or an XSS defense. Output must still be
/// encoded for the context it is rendered in; do not rely on them as the only barrier against markup
/// injection.
/// </remarks>
[PublicAPI]
public static class SafeTextValidators
{
#nullable disable // keep the builder nullability-agnostic: binds to nullable and non-nullable properties, preserving the caller's nullability
    extension<T>(IRuleBuilder<T, string> rule)
    {
        /// <summary>
        /// Validates that the value contains no HTML <c>&lt;script&gt;</c> element. Passes
        /// <see langword="null"/> through without failure.
        /// </summary>
        /// <returns>The rule builder options for chaining.</returns>
        public IRuleBuilderOptions<T, string> NoScripts()
        {
            return rule.Must(static value => value is null || !RegexPatterns.HtmlScripts.IsMatch(value))
                .WithErrorDescriptor(FluentValidatorErrorDescriber.Strings.ContainsScripts());
        }
    }
}
