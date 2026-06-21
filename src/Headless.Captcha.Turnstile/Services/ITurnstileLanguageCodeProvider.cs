// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>Supplies the language code rendered into the Turnstile widget's <c>data-language</c> attribute.</summary>
[PublicAPI]
public interface ITurnstileLanguageCodeProvider
{
    /// <summary>Gets the widget language code (a Turnstile language code such as <c>en</c> / <c>en-us</c>, or <c>auto</c>).</summary>
    string GetLanguageCode();
}

/// <summary>Default <see cref="ITurnstileLanguageCodeProvider"/> that uses the current UI culture.</summary>
[PublicAPI]
public sealed class CultureInfoTurnstileLanguageCodeProvider : ITurnstileLanguageCodeProvider
{
    public string GetLanguageCode() => CultureInfo.CurrentUICulture.ToString();
}
