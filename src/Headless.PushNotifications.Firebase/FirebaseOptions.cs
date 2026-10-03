// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.PushNotifications.Firebase;

/// <summary>
/// Firebase configuration options.
/// </summary>
[PublicAPI]
public sealed class FirebaseOptions
{
    /// <summary>
    /// Firebase service account JSON credentials.
    /// </summary>
    /// <remarks>
    /// Contains sensitive private key data. Do not log or serialize.
    /// </remarks>
    [JsonIgnore]
    public required string Json { get; set; }

    /// <summary>
    /// Retry policy configuration for FCM API calls.
    /// </summary>
    public FirebaseRetryOptions Retry { get; set; } = new();

    /// <summary>
    /// Whether FCM's <c>SENDER_ID_MISMATCH</c> error is reported as
    /// <see cref="PushNotificationResponseStatus.Unregistered"/>, which tells the caller to delete the token.
    /// </summary>
    /// <remarks>
    /// Default: <see langword="false"/>, so the error is reported as
    /// <see cref="PushNotificationResponseStatus.Failure"/>. FCM returns it when the token belongs to a different
    /// Firebase project than the credentials. That is a dead token only when the credentials are right; a host
    /// configured with the wrong project's credentials gets it for every token, and treating it as unregistered
    /// would delete every valid token. Enable it once the credentials are known to be correct.
    /// </remarks>
    public bool TreatSenderIdMismatchAsUnregistered { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "FirebaseOptions { Json = [REDACTED] }";
    }
}

/// <summary>
/// FluentValidation validator for <see cref="FirebaseOptions"/>. Wired up and executed automatically by the
/// <c>UseFirebase</c> setup methods.
/// </summary>
internal sealed class FirebaseOptionsValidator : AbstractValidator<FirebaseOptions>
{
    public FirebaseOptionsValidator()
    {
        RuleFor(x => x.Json).NotEmpty().WithMessage("Firebase JSON credentials must be provided.");

        RuleFor(x => x.Retry.MaxAttempts)
            .InclusiveBetween(0, 5)
            .WithMessage("Retry MaxAttempts must be between 0 and 5.");

        RuleFor(x => x.Retry.MaxDelay)
            .InclusiveBetween(TimeSpan.FromMinutes(1), TimeSpan.FromHours(1))
            .WithMessage("Retry MaxDelay must be between 1 minute and 1 hour.");
    }
}
