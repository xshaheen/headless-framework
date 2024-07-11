using System.Text.Json.Serialization;

namespace Framework.Integrations.Recaptcha.Internals;

internal sealed class InternalRecaptchaV2Response
{
    private IReadOnlyList<string>? _errorCodes;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>Timestamp of the challenge load.</summary>
    [JsonPropertyName("challenge_ts")]
    public DateTimeOffset ChallengeTimeStamp { get; set; }

    /// <summary>The hostname of the site where the reCAPTCHA was solved.</summary>
    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = default!;

    /// <summary>Error code if not <see cref="Success"/>.</summary>
    [JsonPropertyName("error-codes")]
    public IReadOnlyList<string> ErrorCodes
    {
        get => _errorCodes ?? Array.Empty<string>();
        set => _errorCodes = value;
    }
}
