#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Integrations.Recaptcha;

public sealed class RecaptchaV2Response
{
    private readonly IReadOnlyList<RecaptchaV2Error>? _errorCodes;

    public bool Success { get; init; }

    /// <summary>Timestamp of the challenge load.</summary>
    public DateTimeOffset ChallengeTimeStamp { get; init; }

    /// <summary>The hostname of the site where the reCAPTCHA was solved.</summary>
    public string Hostname { get; init; } = default!;

    /// <summary>Error code if not <see cref="Success"/>.</summary>
    public IReadOnlyList<RecaptchaV2Error> ErrorCodes
    {
        get => _errorCodes ?? Array.Empty<RecaptchaV2Error>();
        init => _errorCodes = value;
    }
}

public enum RecaptchaV2Error
{
    /// <summary>The secret parameter is missing.</summary>
    MissingInputSecret,

    /// <summary>The secret parameter is invalid or malformed.</summary>
    InvalidInputSecret,

    /// <summary>The response parameter is missing.</summary>
    MissingInputResponse,

    /// <summary>The response parameter is invalid or malformed.</summary>
    InvalidInputResponse,

    /// <summary>The request is invalid or malformed.</summary>
    BadRequest,

    /// <summary>The response is no longer valid: either is too old or has been used previously.</summary>
    TimeOutOrDuplicate,
}
