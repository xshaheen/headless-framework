// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Recaptcha.Contracts;

public sealed class ReCaptchaSiteVerifyRequest
{
    /// <summary>Required. The user response token provided by the reCAPTCHA client-side integration on your site.</summary>
    public required string Response { get; init; }

    /// <summary>Optional. The user's IP address.</summary>
    public string? RemoteIp { get; init; }
}
