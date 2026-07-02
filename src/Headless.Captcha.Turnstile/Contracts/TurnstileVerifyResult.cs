// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Captcha;

/// <summary>
/// A Turnstile verification result. Extends the common result with Turnstile's provider-only fields — the
/// <c>cdata</c> echoed back from the widget and, for Enterprise accounts, the <c>metadata</c> object when present.
/// </summary>
[PublicAPI]
public sealed class TurnstileVerifyResult : CaptchaVerifyResult
{
    /// <summary>The customer data (<c>cdata</c>) supplied to the widget and echoed back on verification.</summary>
    public string? CData { get; init; }

    /// <summary>The Enterprise <c>metadata</c> object when the response includes it; otherwise <see langword="null"/>.</summary>
    public JsonElement? Metadata { get; init; }
}
