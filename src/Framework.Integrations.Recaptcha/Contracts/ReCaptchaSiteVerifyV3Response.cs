namespace Framework.Integrations.Recaptcha.Contracts;

public sealed class ReCaptchaSiteVerifyV3Response : ReCaptchaSiteVerifyResponse
{
    /// <summary>The score for this request (0.0 - 1.0)</summary>
    public float Score { get; set; }

    /// <summary>The action name for this request (important to verify)</summary>
    public string? Action { get; set; }
}
