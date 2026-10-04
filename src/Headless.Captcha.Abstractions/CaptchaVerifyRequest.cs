// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Captcha;

/// <summary>
/// Represents common inputs accepted when verifying a token across providers: a required client response token and an
/// optional remote IP address.
/// </summary>
[PublicAPI]
public class CaptchaVerifyRequest
{
    /// <summary>The response token produced by the client widget.</summary>
    public required string Response { get; init; }

    /// <summary>The IP address of the end user.</summary>
    public string? RemoteIp { get; init; }
}
