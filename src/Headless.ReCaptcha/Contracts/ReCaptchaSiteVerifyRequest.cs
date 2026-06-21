// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.ReCaptcha.Contracts;

/// <summary>Parameters sent to the Google reCAPTCHA siteverify endpoint to validate a user response token.</summary>
public sealed class ReCaptchaSiteVerifyRequest
{
    /// <summary>Required. The user response token provided by the reCAPTCHA client-side integration on your site.</summary>
    public required string Response { get; init; }

    /// <summary>Optional. The user's IP address.</summary>
    public string? RemoteIp { get; init; }
}
